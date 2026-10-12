#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using Feeder.Json;

namespace Feeder.MCP.Editor.MatrixSpace
{
    /// <summary>
    /// Reads the Claude subscription usage windows (5-hour session, weekly caps) from the
    /// same undocumented OAuth endpoint that powers Claude Code's /usage command, using the
    /// access token Claude Code stores in ~/.claude/.credentials.json. The endpoint is not
    /// documented, so parsing is lenient: unknown fields are ignored and any object with a
    /// numeric "utilization" is treated as a window. The token is never logged or surfaced.
    /// </summary>
    public sealed class ClaudeUsageProvider : IUsageProvider
    {
        private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
        private const string LoginHint = "Not signed in. Open a terminal, run `claude`, then `/login`.";

        // Without a claude-code User-Agent the endpoint rate-limits aggressively.
        private const string UserAgent = "claude-code/2.0.0";

        public string PresetId => "claude";

        public ProviderUsageSnapshot Fetch()
        {
            var snapshot = new ProviderUsageSnapshot
            {
                PresetId = PresetId,
                FetchedAtUtc = DateTime.UtcNow,
            };

            string? accessToken;
            try
            {
                accessToken = ReadAccessToken(snapshot);
            }
            catch (Exception)
            {
                accessToken = null;
            }

            if (accessToken == null)
            {
                snapshot.Status = UsageAvailability.AuthRequired;
                snapshot.Error ??= LoginHint;
                return snapshot;
            }

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessToken);
                request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

                using var response = client.SendAsync(request).GetAwaiter().GetResult();
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    snapshot.Status = UsageAvailability.AuthRequired;
                    snapshot.Error = "Claude session expired. " + LoginHint;
                    return snapshot;
                }

                if (!response.IsSuccessStatusCode)
                {
                    snapshot.Status = UsageAvailability.Error;
                    snapshot.Error = $"Usage endpoint returned HTTP {(int)response.StatusCode}.";
                    return snapshot;
                }

                var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                snapshot.Windows = ParseWindows(json);
                snapshot.Status = UsageAvailability.Ok;
                if (snapshot.Windows.Count == 0)
                    snapshot.Error = "No usage windows reported for this plan.";
                return snapshot;
            }
            catch (Exception ex)
            {
                snapshot.Status = UsageAvailability.Error;
                snapshot.Error = ex.InnerException?.Message ?? ex.Message;
                return snapshot;
            }
        }

        // ── Credentials ──────────────────────────────────────────────────

        private static string CredentialsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

        /// <summary>Returns the OAuth access token, or null (with snapshot.Error set) when unusable.</summary>
        private static string? ReadAccessToken(ProviderUsageSnapshot snapshot)
        {
            if (!File.Exists(CredentialsPath))
            {
                snapshot.Error = LoginHint;
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(CredentialsPath));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) ||
                oauth.ValueKind != JsonValueKind.Object)
            {
                snapshot.Error = LoginHint;
                return null;
            }

            if (oauth.TryGetProperty("subscriptionType", out var plan) &&
                plan.ValueKind == JsonValueKind.String)
            {
                snapshot.PlanName = plan.GetString();
            }

            // expiresAt is epoch milliseconds; an expired token would only yield a 401.
            if (oauth.TryGetProperty("expiresAt", out var expires) &&
                expires.ValueKind == JsonValueKind.Number &&
                DateTimeOffset.FromUnixTimeMilliseconds(expires.GetInt64()) < DateTimeOffset.UtcNow)
            {
                snapshot.Error = "Claude access token expired. Run `claude` once (it refreshes the token), or `/login`.";
                return null;
            }

            if (oauth.TryGetProperty("accessToken", out var token) &&
                token.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(token.GetString()))
            {
                return token.GetString();
            }

            snapshot.Error = LoginHint;
            return null;
        }

        // ── Response parsing ─────────────────────────────────────────────

        /// <summary>Known window ids in display order; unknown windows are appended prettified.</summary>
        private static readonly (string Id, string Label)[] KnownWindows =
        {
            ("five_hour", "5-HOUR SESSION"),
            ("seven_day", "WEEKLY · ALL MODELS"),
            ("seven_day_opus", "WEEKLY · OPUS"),
            ("seven_day_sonnet", "WEEKLY · SONNET"),
        };

        private static List<UsageWindowSnapshot> ParseWindows(string json)
        {
            var found = new Dictionary<string, UsageWindowSnapshot>(StringComparer.OrdinalIgnoreCase);

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return new List<UsageWindowSnapshot>();

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var window = TryParseWindow(prop.Name, prop.Value);
                if (window != null)
                    found[prop.Name] = window;
            }

            var ordered = new List<UsageWindowSnapshot>();
            foreach (var (id, _) in KnownWindows)
            {
                if (found.TryGetValue(id, out var window))
                {
                    ordered.Add(window);
                    found.Remove(id);
                }
            }

            ordered.AddRange(found.Values);
            return ordered;
        }

        private static UsageWindowSnapshot? TryParseWindow(string id, JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty("utilization", out var utilization) ||
                utilization.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            var window = new UsageWindowSnapshot
            {
                Id = id,
                Label = LabelFor(id),
                UtilizationPercent = Math.Clamp(utilization.GetDouble(), 0, 100),
            };

            if (element.TryGetProperty("resets_at", out var resets))
            {
                if (resets.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(resets.GetString(), out var parsed))
                    window.ResetsAt = parsed;
                else if (resets.ValueKind == JsonValueKind.Number)
                    window.ResetsAt = DateTimeOffset.FromUnixTimeSeconds(resets.GetInt64());
            }

            return window;
        }

        private static string LabelFor(string id)
        {
            foreach (var (knownId, label) in KnownWindows)
            {
                if (string.Equals(knownId, id, StringComparison.OrdinalIgnoreCase))
                    return label;
            }

            return id.Replace('_', ' ').ToUpperInvariant();
        }
    }
}
