#nullable enable
using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Feeder.ToolHost;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Console
    {
        public const string ConsoleGetLogsToolId = "console-get-logs";
        [AiTool
        (
            ConsoleGetLogsToolId,
            Title = "Console / Get Logs",
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [AiSkillDescription("Retrieve Unity Editor logs from the MCP plugin's `LogCollector`, " +
            "optionally filtered by log type or time window. Useful for debugging and monitoring Editor activity.")]
        [AiSkillBody("Retrieves Unity Editor logs. " +
            "Useful for debugging and monitoring Unity Editor activity.\n\n" +
            "## Inputs\n\n" +
            "- `maxEntries` (default 100, minimum 1) — caps the size of the returned array.\n" +
            "- `logTypeFilter` — Unity `LogType` filter; `null` returns all severities.\n" +
            "- `includeStackTrace` (default `false`) — include stack-trace strings in each entry.\n" +
            "- `lastMinutes` (default 0) — when non-zero, only logs from the last N minutes are returned.\n" +
            "- `logTypes` — several types at once, for example `[\"Error\",\"Exception\"]`; replaces `logTypeFilter`.\n" +
            "- `sinceUtc` — ISO 8601 time; only logs from then on. Note the time before your step and pass it to see only what the step logged.\n" +
            "- `include` / `exclude` — regular expressions on the message; `exclude: \"^\\\\[Matrix\"` hides Matrix's own lines.\n" +
            "- `groupDuplicates` — one entry per identical message with its `Count`.\n\n" +
            "Entries come oldest first. Without the new inputs the result holds the same entries as before.")]
        [Description("Retrieves Unity Editor logs. " +
            "Useful for debugging and monitoring Unity Editor activity.")]
        public LogEntry[] GetLogs
        (
            [Description("Maximum number of log entries to return. Minimum: 1. Default: 100")]
            int maxEntries = 100,
            [Description("Filter by log type. 'null' means All.")]
            LogType? logTypeFilter = null,
            [Description("Include stack traces in the output. Default: false")]
            bool includeStackTrace = false,
            [Description("Return logs from the last N minutes. If 0, returns all available logs. Default: 0")]
            int lastMinutes = 0,
            [Description("Only these log types; replaces 'logTypeFilter' when given.")]
            LogType[]? logTypes = null,
            [Description("ISO 8601 time; only logs from that moment on.")]
            string? sinceUtc = null,
            [Description("Regular expression the message must match.")]
            string? include = null,
            [Description("Regular expression the message must not match.")]
            string? exclude = null,
            [Description("Return one entry per identical message, with 'Count'. Default: false")]
            bool groupDuplicates = false
        )
        {
            // Validate parameters
            if (maxEntries < 1)
                throw new ArgumentException(Error.InvalidMaxEntries(maxEntries));

            if (!MatrixPluginEditor.HasInstance)
                throw new InvalidOperationException("MatrixPluginEditor is not initialized.");

            var logCollector = MatrixPluginEditor.Instance.LogCollector;
            if (logCollector == null)
                throw new InvalidOperationException("LogCollector is not initialized.");

            var since = ParseSince(sinceUtc);
            var filter = BuildFilter(logTypes, Pattern(include, nameof(include)), Pattern(exclude, nameof(exclude)));

            // Get all log entries as array to avoid concurrent modification
            var logs = logCollector.Query(
                maxEntries: groupDuplicates ? Math.Max(maxEntries, Math.Min(maxEntries * 20, GroupScanLimit)) : maxEntries,
                logTypeFilter: logTypes is { Length: > 0 } ? null : logTypeFilter,
                includeStackTrace: includeStackTrace,
                lastMinutes: lastMinutes,
                since: since,
                filter: filter
            );

            if (groupDuplicates)
                logs = Group(logs, maxEntries);

            if (includeStackTrace)
                return logs;

            return Array.ConvertAll(logs, log => new LogEntry(log.LogType, log.Message, log.Timestamp) { Count = log.Count });
        }

        const int GroupScanLimit = 10000;
        static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

        static DateTime? ParseSince(string? sinceUtc)
        {
            if (string.IsNullOrWhiteSpace(sinceUtc))
                return null;
            if (!DateTimeOffset.TryParse(sinceUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var since))
                throw new ToolCallerException($"'{nameof(sinceUtc)}' is not an ISO 8601 time: '{sinceUtc}'.");
            // stored entries carry local time
            return since.LocalDateTime;
        }

        static Regex? Pattern(string? pattern, string name)
        {
            if (string.IsNullOrEmpty(pattern))
                return null;
            try
            {
                return new Regex(pattern, RegexOptions.CultureInvariant, RegexTimeout);
            }
            catch (ArgumentException e)
            {
                throw new ToolCallerException($"'{name}' is not a valid regular expression: {e.Message}");
            }
        }

        static Func<LogEntry, bool>? BuildFilter(LogType[]? logTypes, Regex? include, Regex? exclude)
        {
            if (logTypes is not { Length: > 0 } && include == null && exclude == null)
                return null;
            return entry =>
                (logTypes is not { Length: > 0 } || logTypes.Contains(entry.LogType)) &&
                (include == null || Matches(include, entry.Message)) &&
                (exclude == null || !Matches(exclude, entry.Message));
        }

        // a pattern that runs too long on one message counts as no match rather than failing the whole read
        static bool Matches(Regex pattern, string message)
        {
            try { return pattern.IsMatch(message); }
            catch (RegexMatchTimeoutException) { return false; }
        }

        // oldest first like the plain result; each group sits where its last occurrence was
        static LogEntry[] Group(LogEntry[] logs, int maxEntries)
        {
            return logs
                .Select((log, index) => (log, index))
                .GroupBy(item => (item.log.LogType, item.log.Message))
                .Select(group =>
                {
                    var last = group.Last();
                    var entry = new LogEntry(last.log.LogType, last.log.Message, last.log.Timestamp, last.log.StackTrace) { Count = group.Count() };
                    return (entry, last.index);
                })
                .OrderBy(item => item.index)
                .Select(item => item.entry)
                .Reverse()
                .Take(maxEntries)
                .Reverse()
                .ToArray();
        }
    }
}