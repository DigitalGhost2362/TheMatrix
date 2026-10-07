#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using Feeder.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Feeder.MCP.Editor.MatrixSpace
{
    /// <summary>
    /// Live model catalog for the pane model dropdowns. Primary source is the open
    /// models.dev database (no API key required); CLIs that can enumerate their own
    /// models (preset.ListModelsArgs, e.g. `cursor-agent --list-models`) override it.
    /// Responses are cached under Library/ with a 24h TTL so dropdowns populate
    /// instantly after domain reloads and keep working offline.
    /// </summary>
    public static class ModelCatalogService
    {
        private const string ApiUrl = "https://models.dev/api.json";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
        private static readonly Regex ModelIdPattern = new(@"^[A-Za-z0-9][\w.\-/]*$", RegexOptions.Compiled);

        private static readonly object _lock = new();
        private static Dictionary<string, List<string>> _byProvider = new();
        private static Dictionary<string, List<string>> _cliModels = new();
        private static bool _loaded;
        private static bool _cliProbed;

        /// <summary>Raised on the main thread whenever the catalog or a CLI model list updates.</summary>
        public static event Action? Updated;

        public static DateTime? LastUpdatedUtc { get; private set; }
        public static string? LastError { get; private set; }
        public static bool IsRefreshing { get; private set; }

        private static string CacheDir => Path.Combine(MatrixSpacePaths.ProjectRoot, "Library", "com.feeder.mcp");
        private static string CatalogCachePath => Path.Combine(CacheDir, "models-dev.json");
        private static string CliCachePath => Path.Combine(CacheDir, "models-cli.json");

        /// <summary>Loads the disk cache once and refreshes in the background when the TTL expired.</summary>
        public static void EnsureLoaded()
        {
            var needsFetch = false;
            var needsCliProbe = false;
            lock (_lock)
            {
                if (!_loaded)
                {
                    _loaded = true;
                    LoadCacheFromDisk();
                    needsFetch = IsCacheExpired();
                }

                if (!_cliProbed)
                {
                    _cliProbed = true;
                    needsCliProbe = true;
                }
            }

            if (needsFetch)
                Refresh(force: false);
            if (needsCliProbe)
                ProbeCliListings();
        }

        /// <summary>Re-fetches models.dev and re-runs CLI listings, ignoring the TTL.</summary>
        public static void ForceRefresh()
        {
            EnsureLoaded();
            Refresh(force: true);
            ProbeCliListings();
        }

        /// <summary>Model ids for a models.dev provider ("anthropic", "openai", ...), newest first. Null = not loaded.</summary>
        public static IReadOnlyList<string>? GetModelsForProvider(string providerId)
        {
            lock (_lock)
                return _byProvider.TryGetValue(providerId, out var models) && models.Count > 0 ? models : null;
        }

        /// <summary>Model ids reported by the CLI itself (preset.ListModelsArgs). Null = not available.</summary>
        public static IReadOnlyList<string>? GetCliListedModels(AgentBackendPreset preset)
        {
            lock (_lock)
                return _cliModels.TryGetValue(preset.Id, out var models) && models.Count > 0 ? models : null;
        }

        // ── models.dev fetch ─────────────────────────────────────────────

        private static bool IsCacheExpired()
        {
            try
            {
                return !File.Exists(CatalogCachePath) ||
                       DateTime.UtcNow - File.GetLastWriteTimeUtc(CatalogCachePath) > CacheTtl;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static void Refresh(bool force)
        {
            if (IsRefreshing)
                return;
            if (!force && !IsCacheExpired())
                return;

            IsRefreshing = true;
            var context = SynchronizationContext.Current;
            Task.Run(() =>
            {
                string? json = null;
                string? error = null;
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                    json = client.GetStringAsync(ApiUrl).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    error = ex.InnerException?.Message ?? ex.Message;
                }

                Dictionary<string, List<string>>? parsed = null;
                if (json != null)
                {
                    parsed = ParseCatalog(json);
                    if (parsed == null)
                        error = "Unexpected models.dev response format.";
                }

                void Apply()
                {
                    IsRefreshing = false;
                    if (parsed != null)
                    {
                        lock (_lock)
                            _byProvider = parsed;
                        LastUpdatedUtc = DateTime.UtcNow;
                        LastError = null;
                        WriteCacheFile(CatalogCachePath, json!);
                    }
                    else
                    {
                        // Keep previous data and cache on failure.
                        LastError = error;
                    }

                    Updated?.Invoke();
                }

                if (context != null)
                    context.Post(_ => Apply(), null);
                else
                    Apply();
            });
        }

        /// <summary>
        /// api.json shape: { providerId: { models: { modelId: { id, release_date, tool_call, ... } } } }.
        /// Returns null only when nothing usable could be parsed.
        /// </summary>
        private static Dictionary<string, List<string>>? ParseCatalog(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return null;

                var result = new Dictionary<string, List<string>>();
                foreach (var provider in doc.RootElement.EnumerateObject())
                {
                    var models = ParseProviderModels(provider.Value);
                    if (models.Count > 0)
                        result[provider.Name] = models;
                }

                return result.Count > 0 ? result : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static List<string> ParseProviderModels(JsonElement provider)
        {
            var entries = new List<(string Id, string ReleaseDate, string Family)>();
            if (provider.ValueKind == JsonValueKind.Object &&
                provider.TryGetProperty("models", out var models) &&
                models.ValueKind == JsonValueKind.Object)
            {
                foreach (var model in models.EnumerateObject())
                {
                    try
                    {
                        var element = model.Value;
                        // Coding CLIs need tool use; skip embedding/image/legacy chat models.
                        if (element.TryGetProperty("tool_call", out var toolCall) &&
                            toolCall.ValueKind == JsonValueKind.False)
                            continue;

                        var id = element.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                            ? idProp.GetString()!
                            : model.Name;
                        if (string.IsNullOrWhiteSpace(id))
                            continue;

                        var releaseDate = element.TryGetProperty("release_date", out var dateProp) &&
                                          dateProp.ValueKind == JsonValueKind.String
                            ? dateProp.GetString()!
                            : string.Empty;
                        var family = element.TryGetProperty("family", out var familyProp) &&
                                     familyProp.ValueKind == JsonValueKind.String
                            ? familyProp.GetString()!
                            : string.Empty;
                        entries.Add((id.Trim(), releaseDate, family));
                    }
                    catch (Exception)
                    {
                        // Skip malformed entries; the schema is community-maintained.
                    }
                }
            }

            var ids = new HashSet<string>(entries.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);
            bool IsAlias((string Id, string ReleaseDate, string Family) entry)
            {
                // Rolling aliases duplicate a concrete model (gemini-flash-latest, gpt-5.x-chat-latest).
                if (entry.Id.Contains("latest"))
                    return true;

                // Dated snapshot of an undated id (claude-opus-4-5-20251101 vs claude-opus-4-5).
                var dateSuffix = Regex.Match(entry.Id, @"^(..+)-\d{8}$");
                if (dateSuffix.Success && ids.Contains(dateSuffix.Groups[1].Value))
                    return true;

                // Bare alias of a named variant: its family names the variant tier it points to
                // (gpt-5.6 has family "gpt-sol" and gpt-5.6-sol exists → show only the tiers).
                // Genuine siblings (kimi-k2.7-code vs -highspeed) share a family that does not
                // end with the variant suffix, so they are kept.
                if (entry.Family.Length > 0)
                {
                    foreach (var other in entries)
                    {
                        if (other.Id.Length > entry.Id.Length + 1 &&
                            other.Id.StartsWith(entry.Id + "-", StringComparison.OrdinalIgnoreCase) &&
                            entry.Family.EndsWith(other.Id.Substring(entry.Id.Length + 1),
                                StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }

                return false;
            }

            // ISO dates sort lexically; newest first, undated last.
            return entries
                .Where(e => !IsAlias(e))
                .OrderByDescending(e => e.ReleaseDate, StringComparer.Ordinal)
                .Select(e => e.Id)
                .Distinct()
                .ToList();
        }

        // ── CLI-native listings (e.g. cursor-agent --list-models) ────────

        private static void ProbeCliListings()
        {
            var presets = AgentBackendCatalog.Presets
                .Where(p => !string.IsNullOrEmpty(p.ListModelsArgs))
                .ToArray();
            if (presets.Length == 0)
                return;

            var context = SynchronizationContext.Current;
            Task.Run(() =>
            {
                var results = new Dictionary<string, List<string>>();
                foreach (var preset in presets)
                {
                    var models = ListModelsSync(preset);
                    if (models != null)
                        results[preset.Id] = models;
                }

                if (results.Count == 0)
                    return;

                void Apply()
                {
                    lock (_lock)
                    {
                        foreach (var pair in results)
                            _cliModels[pair.Key] = pair.Value;
                    }

                    try
                    {
                        WriteCacheFile(CliCachePath, JsonSerializer.Serialize(results));
                    }
                    catch (Exception)
                    {
                        // Cache write is best-effort.
                    }

                    Updated?.Invoke();
                }

                if (context != null)
                    context.Post(_ => Apply(), null);
                else
                    Apply();
            });
        }

        private static List<string>? ListModelsSync(AgentBackendPreset preset)
        {
            try
            {
                var path = AgentBackendCatalog.ResolveExecutablePath(preset);
                if (path == null)
                    return null;

                var launch = new CliLaunchInfo(path);
                var startInfo = new ProcessStartInfo
                {
                    FileName = launch.FileName,
                    Arguments = launch.BuildArguments(preset.ListModelsArgs),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                    return null;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(15000);
                if (process.ExitCode != 0)
                    return null;

                var models = new List<string>();
                foreach (var rawLine in output.Split('\n'))
                {
                    // Keep bare model ids; drop banners, prose, and auth errors.
                    var line = rawLine.Trim().TrimStart('-', '*', ' ');
                    if (line.Length > 0 && ModelIdPattern.IsMatch(line) && !models.Contains(line))
                        models.Add(line);
                }

                return models.Count > 0 ? models : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ── Disk cache ───────────────────────────────────────────────────

        private static void LoadCacheFromDisk()
        {
            try
            {
                if (File.Exists(CatalogCachePath))
                {
                    var parsed = ParseCatalog(File.ReadAllText(CatalogCachePath));
                    if (parsed != null)
                    {
                        _byProvider = parsed;
                        LastUpdatedUtc = File.GetLastWriteTimeUtc(CatalogCachePath);
                    }
                }

                if (File.Exists(CliCachePath))
                {
                    var cli = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(
                        File.ReadAllText(CliCachePath));
                    if (cli != null)
                        _cliModels = cli;
                }
            }
            catch (Exception)
            {
                // Corrupt cache is ignored; the next refresh rewrites it.
            }
        }

        private static void WriteCacheFile(string path, string content)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                File.WriteAllText(path, content);
            }
            catch (Exception)
            {
                // Cache write is best-effort.
            }
        }
    }
}
