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
    /// Reads the ChatGPT/Codex subscription usage from the same undocumented backend
    /// endpoint the Codex CLI polls for its rate-limit display, using the OAuth token
    /// Codex stores in ~/.codex/auth.json. Only the `wham/usage` path works — the
    /// `codex/usage` variants return 403. Parsing is lenient: missing fields are
    /// skipped, never thrown on. The token is never logged or surfaced.
    /// </summary>
    public sealed class CodexUsageProvider : IUsageProvider
    {
        private const string UsageUrl = "https://chatgpt.com/backend-api/wham/usage";
        private const string LoginHint = "Not signed in. Open a terminal and run `codex login`.";

        // The backend rejects requests without a codex originator/User-Agent.
        private const string Originator = "codex_cli_rs";
        private const string UserAgent = "codex_cli_rs/0.145.0";

        public string PresetId => "codex";

        public ProviderUsageSnapshot Fetch()
        {
            var snapshot = new ProviderUsageSnapshot
            {
                PresetId = PresetId,
                FetchedAtUtc = DateTime.UtcNow,
            };

            string? accessToken = null;
            string? accountId = null;
            try
            {
                accessToken = ReadAccessToken(snapshot, out accountId);
            }
            catch (Exception)
            {
                // Fall through to AuthRequired below.
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
                if (!string.IsNullOrEmpty(accountId))
                    request.Headers.TryAddWithoutValidation("chatgpt-account-id", accountId);
                request.Headers.TryAddWithoutValidation("originator", Originator);
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

                using var response = client.SendAsync(request).GetAwaiter().GetResult();
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    snapshot.Status = UsageAvailability.AuthRequired;
                    snapshot.Error = "ChatGPT session expired. Run any `codex` turn (it refreshes the token), or `codex login`.";
                    return snapshot;
                }

                if (!response.IsSuccessStatusCode)
                {
                    snapshot.Status = UsageAvailability.Error;
                    snapshot.Error = $"Usage endpoint returned HTTP {(int)response.StatusCode}.";
                    return snapshot;
                }

                var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                ParseUsage(json, snapshot);
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

        private static string AuthPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "auth.json");

        /// <summary>Returns the OAuth access token, or null (with snapshot.Error set) when unusable.</summary>
        private static string? ReadAccessToken(ProviderUsageSnapshot snapshot, out string? accountId)
        {
            accountId = null;
            if (!File.Exists(AuthPath))
            {
                snapshot.Error = LoginHint;
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(AuthPath));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tokens", out var tokens) ||
                tokens.ValueKind != JsonValueKind.Object)
            {
                // API-key mode has no ChatGPT subscription to report on.
                snapshot.Error = root.TryGetProperty("OPENAI_API_KEY", out var key) &&
                                 key.ValueKind == JsonValueKind.String
                    ? "Codex is using an API key, not a ChatGPT subscription. Run `codex login` to sign in with ChatGPT."
                    : LoginHint;
                return null;
            }

            if (tokens.TryGetProperty("account_id", out var account) &&
                account.ValueKind == JsonValueKind.String)
            {
                accountId = account.GetString();
            }

            if (tokens.TryGetProperty("access_token", out var token) &&
                token.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(token.GetString()))
            {
                return token.GetString();
            }

            snapshot.Error = LoginHint;
            return null;
        }

        // ── Response parsing ─────────────────────────────────────────────

        private static void ParseUsage(string json, ProviderUsageSnapshot snapshot)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return;

            if (root.TryGetProperty("plan_type", out var plan) &&
                plan.ValueKind == JsonValueKind.String)
            {
                snapshot.PlanName = plan.GetString();
            }

            if (!root.TryGetProperty("rate_limit", out var rateLimit) ||
                rateLimit.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var windows = new List<UsageWindowSnapshot>();
            AddWindow(windows, rateLimit, "primary_window");
            AddWindow(windows, rateLimit, "secondary_window");
            snapshot.Windows = windows;
        }

        private static void AddWindow(List<UsageWindowSnapshot> windows, JsonElement rateLimit, string id)
        {
            if (!rateLimit.TryGetProperty(id, out var element) ||
                element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty("used_percent", out var used) ||
                used.ValueKind != JsonValueKind.Number)
            {
                return;
            }

            var window = new UsageWindowSnapshot
            {
                Id = id,
                Label = LabelFor(element),
                UtilizationPercent = Math.Clamp(used.GetDouble(), 0, 100),
            };

            if (element.TryGetProperty("reset_at", out var resets) &&
                resets.ValueKind == JsonValueKind.Number)
            {
                window.ResetsAt = DateTimeOffset.FromUnixTimeSeconds(resets.GetInt64());
            }

            windows.Add(window);
        }

        private static string LabelFor(JsonElement window)
        {
            if (!window.TryGetProperty("limit_window_seconds", out var seconds) ||
                seconds.ValueKind != JsonValueKind.Number)
            {
                return "USAGE LIMIT";
            }

            var span = TimeSpan.FromSeconds(seconds.GetDouble());
            if (span.TotalDays >= 6.5)
                return "WEEKLY";
            if (Math.Abs(span.TotalHours - 5) < 0.5)
                return "5-HOUR SESSION";
            return span.TotalHours >= 48
                ? $"{Math.Round(span.TotalDays):0}-DAY WINDOW"
                : $"{Math.Round(span.TotalHours):0}-HOUR WINDOW";
        }
    }
}
