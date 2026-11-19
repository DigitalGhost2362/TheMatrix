#nullable enable
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using Feeder.Json.Serialization;
using Feeder.MCP.Editor.Playtest;
using Feeder.Reflect.Utils;
using Feeder.ToolHost;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    [Description("What wait-for saw before it returned.")]
    public class WaitForResult
    {
        [JsonInclude, JsonPropertyName("met")]
        [Description("True when the condition held. False: call again with nextCursor.")]
        public bool Met { get; set; }

        [JsonInclude, JsonPropertyName("waited_ms")]
        public long WaitedMs { get; set; }

        [JsonInclude, JsonPropertyName("matches")]
        [Description("until=log: the matching logs, oldest first.")]
        public LogPageEntry[] Matches { get; set; } = Array.Empty<LogPageEntry>();

        [JsonInclude, JsonPropertyName("nextCursor")]
        [Description("Pass back unchanged, as a string, as 'afterCursor' to keep waiting without missing a log.")]
        public string NextCursor { get; set; } = "0";

        [JsonInclude, JsonPropertyName("note")]
        [Description("Why it stopped without the condition: the last value read, or a compile starting.")]
        public string? Note { get; set; }
    }

    [AiToolType]
    public partial class Tool_Wait
    {
        public const string WaitForToolId = "wait-for";
        const int DefaultTimeoutSec = 30;
        // one call never holds a client past its timeout; a longer wait is several calls with nextCursor
        const int MaxTimeoutSec = 45;
        const int PollMs = 100;

        [AiTool
        (
            WaitForToolId,
            Title = "Editor / Wait For",
            ReadOnlyHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Wait inside the Editor until a console log matches a pattern or a static member reads a value, instead of sleeping and polling from outside. Returns after at most 45 s; call again with nextCursor to keep waiting.")]
        [AiSkillBody("Waits in slices so no call holds the client for minutes.\n\n" +
            "- `until=log` + `pattern` - a regular expression; met when a console log newer than `afterCursor` matches it. Without `afterCursor` only logs from the call on count.\n" +
            "- `until=member` + `member` (`Type.Member`: a static field, property or parameterless method) + `value` - met when it reads `value` (invariant text, any case).\n" +
            "- `timeoutSec` - default 30, at most 45.\n\n" +
            "Answers `met`, `waited_ms`, `matches` and `nextCursor`. `met=false` means not yet: call again with `afterCursor=nextCursor`. " +
            "A compile starting ends the call early with a note, since the domain reload would cut it off. " +
            "Compiles and play mode have their own waits: `assets-refresh`, `editor-application-set-state`.")]
        [Description("Waits until a console log matches 'pattern' (until=log) or a static Type.Member reads 'value' (until=member). Returns after at most timeoutSec (max 45); met=false means call again with afterCursor=nextCursor.")]
        public async Task<WaitForResult> WaitFor
        (
            [Description("log | member")]
            string until = "log",
            [Description("until=log: regular expression a new console message must match.")]
            string? pattern = null,
            [Description("until=member: Type.Member, a static field, property or parameterless method.")]
            string? member = null,
            [Description("until=member: the value to wait for, compared as invariant text, any case.")]
            string? value = null,
            [Description("until=log: the nextCursor of an earlier call or of console-get-logs, unchanged. Empty = logs from now on.")]
            string? afterCursor = null,
            [Description("Seconds this call waits at most. Default 30, max 45.")]
            int timeoutSec = DefaultTimeoutSec
        )
        {
            var mode = (until ?? "log").Trim().ToLowerInvariant();
            if (mode == "log" && string.IsNullOrEmpty(pattern))
                throw new ToolCallerException("until=log needs 'pattern', a regular expression.");
            if (mode == "member" && (string.IsNullOrWhiteSpace(member) || value == null))
                throw new ToolCallerException("until=member needs 'member' (Type.Member) and 'value'.");
            if (mode != "log" && mode != "member")
                throw new ToolCallerException($"until must be log or member, got '{until}'.");

            var limit = TimeSpan.FromSeconds(Math.Clamp(timeoutSec <= 0 ? DefaultTimeoutSec : timeoutSec, 1, MaxTimeoutSec));
            var cursor = string.IsNullOrEmpty(afterCursor) ? DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture) : afterCursor!;
            var console = new Tool_Console();
            var clock = Stopwatch.StartNew();
            var last = string.Empty;
            while (true)
            {
                if (mode == "log")
                {
                    var page = console.GetLogs(maxEntries: 20, include: pattern, afterCursor: cursor);
                    cursor = page.NextCursor;
                    if (page.Entries.Length > 0)
                        return Result(true, clock, cursor, null, page.Entries);
                }
                else
                {
                    last = await MainThread.Instance.RunAsync(() => Read(member!));
                    if (string.Equals(last, value, StringComparison.OrdinalIgnoreCase))
                        return Result(true, clock, cursor, null);
                }
                if (clock.Elapsed >= limit)
                    return Result(false, clock, cursor, mode == "member" ? $"still {last}" : null);
                if (await MainThread.Instance.RunAsync(() => EditorApplication.isCompiling))
                    return Result(false, clock, cursor, "a compile started and a domain reload follows: call again with afterCursor=nextCursor once it ends");
                await Task.Delay(PollMs).ConfigureAwait(false);
            }
        }

        static string Read(string member)
        {
            try
            {
                return PlaytestRunner.MemberText(PlaytestRunner.ReadMember(member, WaitForToolId));
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                throw new ToolCallerException($"{member}: {e.InnerException.GetType().Name}: {e.InnerException.Message}");
            }
            catch (ArgumentException e)
            {
                throw new ToolCallerException(e.Message);
            }
        }

        static WaitForResult Result(bool met, Stopwatch clock, string cursor, string? note, LogPageEntry[]? matches = null) => new WaitForResult
        {
            Met = met,
            WaitedMs = clock.ElapsedMilliseconds,
            NextCursor = cursor,
            Note = note,
            Matches = matches ?? Array.Empty<LogPageEntry>(),
        };
    }
}
