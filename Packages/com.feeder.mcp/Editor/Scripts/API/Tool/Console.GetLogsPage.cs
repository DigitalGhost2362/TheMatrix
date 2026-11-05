#nullable enable
using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
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

        [Description("This log's cursor as a string (a number this large loses digits in a JSON number).")]
        public string? Seq { get; set; }
    }

    [Description("One page of console logs and the cursor to continue from.")]
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
        public const string ConsoleGetLogsPageToolId = "console-get-logs-page";

        [AiTool
        (
            ConsoleGetLogsPageToolId,
            Title = "Console / Get Logs Page",
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [AiSkillDescription("Read console logs page by page with a cursor: each result has `nextCursor`; pass it as `afterCursor` " +
            "to get only logs written since, in order and without gaps. Also says when it cut results (`truncated`, `hint`).")]
        [AiSkillBody("Pages through the console with a cursor.\n\n" +
            "## Inputs\n\n" +
            "- `maxEntries` (default 100, minimum 1) — page size.\n" +
            "- `afterCursor` — the `nextCursor` of the previous call, passed back unchanged as a string (cursors are 18-digit numbers; a JSON number would lose digits). Returns the oldest matching logs after it, so repeated calls walk forward without gaps. " +
            "Omit it for the newest `maxEntries` logs.\n" +
            "- `logTypes`, `sinceUtc`, `include`, `exclude`, `includeStackTrace` — same as 'console-get-logs'.\n\n" +
            "## Output\n\n" +
            "`entries` (oldest first, each with `Seq`, a string), `nextCursor` (a string), `truncated` and `hint`. " +
            "Cursors keep working across domain reloads. A cursor of 0 or a missing one starts from the newest.")]
        [Description("Reads console logs with a cursor. Pass the returned 'nextCursor' back unchanged, as a string, as 'afterCursor' to get only newer logs. " +
            "'truncated' says when results were cut.")]
        public LogPage GetLogsPage
        (
            [Description("Page size. Minimum: 1. Default: 100")]
            int maxEntries = 100,
            [Description("Only logs newer than this cursor: pass the 'nextCursor' of a previous call back unchanged, as a string.")]
            string? afterCursor = null,
            [Description("Only these log types, for example ['Error','Exception']. Default: all.")]
            LogType[]? logTypes = null,
            [Description("Include stack traces. Default: false")]
            bool includeStackTrace = false,
            [Description("ISO 8601 time; only logs from that moment on.")]
            string? sinceUtc = null,
            [Description("Regular expression the message must match.")]
            string? include = null,
            [Description("Regular expression the message must not match.")]
            string? exclude = null
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
                maxEntries: cursor > 0 ? GroupScanLimit : maxEntries + 1,
                includeStackTrace: includeStackTrace,
                since: since,
                filter: filter);

            return Page(logs, maxEntries, cursor, includeStackTrace);
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
