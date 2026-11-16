#nullable enable
using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Feeder.Json.Serialization;
using Feeder.ToolHost;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    [Description("A console log in a page. 'Seq' is its cursor, as a string.")]
    public class LogPageEntry
    {
        public LogType LogType { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string? StackTrace { get; set; }

        [Description("How many identical entries this one stands for; set only with groupDuplicates.")]
        public int? Count { get; set; }

        [Description("This log's cursor as a string (a number this large loses digits in a JSON number).")]
        public string? Seq { get; set; }
    }

    [Description("Console logs and the cursor to continue from.")]
    public class LogPage
    {
        [JsonInclude, JsonPropertyName("entries")]
        [Description("Log entries, oldest first. Each has a 'Seq'.")]
        public LogPageEntry[] Entries { get; set; } = Array.Empty<LogPageEntry>();

        [JsonInclude, JsonPropertyName("nextCursor")]
        [Description("Pass this back unchanged, as a string, as 'afterCursor' to get only logs newer than this page.")]
        public string NextCursor { get; set; } = "0";

        [JsonInclude, JsonPropertyName("truncated")]
        [Description("True when more matching logs exist than were returned.")]
        public bool Truncated { get; set; }

        [JsonInclude, JsonPropertyName("hint")]
        [Description("How to fetch the rest; empty when nothing was cut.")]
        public string? Hint { get; set; }
    }

    public partial class Tool_Console
    {
        public const string ConsoleGetLogsToolId = "console-get-logs";
        [AiTool
        (
            ConsoleGetLogsToolId,
            Title = "Console / Get Logs",
            Enabled = true,
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [AiSkillDescription("Retrieve Unity Editor logs from the MCP plugin's `LogCollector`, " +
            "optionally filtered by log type or time window. Useful for debugging and monitoring Editor activity.")]
        [AiSkillBody("Retrieves Unity Editor logs. " +
            "Useful for debugging and monitoring Unity Editor activity.\n\n" +
            "## Inputs\n\n" +
            "- `maxEntries` (default 50, minimum 1) — caps the number of returned entries.\n" +
            "- `logTypeFilter` — Unity `LogType` filter; `null` returns all severities.\n" +
            "- `includeStackTrace` (default `false`) — include stack-trace strings in each entry.\n" +
            "- `lastMinutes` (default 0) — when non-zero, only logs from the last N minutes are returned.\n" +
            "- `logTypes` — several types at once, for example `[\"Error\",\"Exception\"]`; replaces `logTypeFilter`.\n" +
            "- `sinceUtc` — ISO 8601 time; only logs from then on. Note the time before your step and pass it to see only what the step logged.\n" +
            "- `include` / `exclude` — regular expressions on the message; `exclude: \"^\\\\[Matrix\"` hides Matrix's own lines.\n" +
            "- `groupDuplicates` — one entry per identical message with its `Count`.\n" +
            "- `afterCursor` — the `nextCursor` of the previous call, passed back unchanged as a string (cursors are 18-digit numbers; a JSON number would lose digits). " +
            "Returns the oldest matching logs after it, so repeated calls walk forward without gaps. Omit it for the newest `maxEntries` logs.\n\n" +
            "## Output\n\n" +
            "`entries` (oldest first, each with `Seq`, a string), `nextCursor` (a string), `truncated` and `hint`. " +
            "Cursors keep working across domain reloads.")]
        [Description("Retrieves Unity Editor logs. " +
            "Useful for debugging and monitoring Unity Editor activity.")]
        public LogPage GetLogs
        (
            [Description("Maximum number of log entries to return. Minimum: 1. Default: 50")]
            int maxEntries = 50,
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
            bool groupDuplicates = false,
            [Description("Only logs newer than this cursor: pass the 'nextCursor' of a previous call back unchanged, as a string.")]
            string? afterCursor = null
        )
        {
            if (maxEntries < 1)
                throw new ArgumentException(Error.InvalidMaxEntries(maxEntries));
            var cursor = ParseCursor(afterCursor);

            if (!MatrixPluginEditor.HasInstance)
                throw new InvalidOperationException("MatrixPluginEditor is not initialized.");
            var logCollector = MatrixPluginEditor.Instance.LogCollector
                ?? throw new InvalidOperationException("LogCollector is not initialized.");

            var since = ParseSince(sinceUtc);
            if (cursor > 0)
            {
                // a cursor is UTC ticks; stop reading back a little before it (entries are stamped just before they get their id)
                var cursorTime = new DateTime(Math.Min(cursor, DateTime.MaxValue.Ticks), DateTimeKind.Utc).ToLocalTime().AddSeconds(-2);
                if (since == null || cursorTime > since)
                    since = cursorTime;
            }

            var typeFilter = BuildFilter(logTypes, Pattern(include, nameof(include)), Pattern(exclude, nameof(exclude)));
            Func<LogEntry, bool> filter = entry => (cursor == 0 || (entry.Seq ?? 0) > cursor) && (typeFilter == null || typeFilter(entry));

            var logs = logCollector.Query(
                maxEntries: ScanLimit(cursor, groupDuplicates, maxEntries),
                logTypeFilter: logTypes is { Length: > 0 } ? null : logTypeFilter,
                includeStackTrace: includeStackTrace,
                lastMinutes: lastMinutes,
                since: since,
                filter: filter);

            if (groupDuplicates)
                logs = Group(logs);

            return Page(logs, maxEntries, cursor, includeStackTrace);
        }

        const int GroupScanLimit = 10000;

        // the store hands back the newest N matches. After a cursor the page is the oldest ones, so every match is read
        // (the cursor's time already bounds the scan); a cap here would silently drop the oldest logs after the cursor.
        internal static int ScanLimit(long cursor, bool groupDuplicates, int maxEntries)
            => cursor > 0 ? int.MaxValue : groupDuplicates ? GroupScanLimit : maxEntries + 1;
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
        static LogEntry[] Group(LogEntry[] logs)
        {
            return logs
                .Select((log, index) => (log, index))
                .GroupBy(item => (item.log.LogType, item.log.Message))
                .Select(group =>
                {
                    var last = group.Last();
                    var entry = new LogEntry(last.log.LogType, last.log.Message, last.log.Timestamp, last.log.StackTrace) { Count = group.Count(), Seq = last.log.Seq };
                    return (entry, last.index);
                })
                .OrderBy(item => item.index)
                .Select(item => item.entry)
                .ToArray();
        }

        internal static long ParseCursor(string? cursor)
        {
            if (string.IsNullOrWhiteSpace(cursor))
                return 0;
            if (!long.TryParse(cursor!.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                throw new ToolCallerException($"'afterCursor' must be the 'nextCursor' of a previous call, unchanged. Got '{cursor}'.");
            return value;
        }

        // after a cursor the page is the oldest matches so a caller walks forward; without one it is the newest
        internal static LogPage Page(LogEntry[] oldestFirst, int maxEntries, long afterCursor, bool includeStackTrace)
        {
            var truncated = oldestFirst.Length > maxEntries;
            var kept = !truncated ? oldestFirst
                : afterCursor > 0 ? oldestFirst.Take(maxEntries).ToArray()
                : oldestFirst.Skip(oldestFirst.Length - maxEntries).ToArray();

            var last = kept.Length > 0 ? kept[kept.Length - 1].Seq ?? afterCursor : afterCursor;
            var page = new LogPage
            {
                Entries = Array.ConvertAll(kept, log => new LogPageEntry
                {
                    LogType = log.LogType,
                    Message = log.Message,
                    Timestamp = log.Timestamp,
                    StackTrace = includeStackTrace ? log.StackTrace : null,
                    Count = log.Count,
                    Seq = log.Seq?.ToString(CultureInfo.InvariantCulture),
                }),
                NextCursor = Math.Max(last, afterCursor).ToString(CultureInfo.InvariantCulture),
                Truncated = truncated,
            };
            if (truncated)
                page.Hint = afterCursor > 0
                    ? $"More logs follow: call again with afterCursor={page.NextCursor}."
                    : $"Older logs were cut: raise maxEntries, or use afterCursor={page.NextCursor} later for newer ones.";
            return page;
        }
    }
}