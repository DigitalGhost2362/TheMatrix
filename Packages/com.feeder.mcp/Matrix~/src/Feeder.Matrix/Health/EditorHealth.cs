using System.Diagnostics;
using System.Text;
using Feeder.Matrix.Protocol;
using Feeder.Matrix.Unity;
using Microsoft.Extensions.Options;

namespace Feeder.Matrix.Health;

/// <summary>
/// Reads the linked editor's health without needing its main thread: the background heartbeat,
/// the matrix's own view of in-flight calls, the editor process and its windows, and the tail of
/// the editor's log file. Serves the <c>editor-health</c> tool and enriches timeout errors.
/// </summary>
public sealed class EditorHealth(UnityLinkRegistry registry, IOptions<MatrixOptions> options, TimeProvider time)
{
    public const string ToolName = "editor-health";

    /// <summary>A main thread that has not ticked for this long is stalled (an unfocused editor still ticks about every 0.1 s).</summary>
    public static readonly TimeSpan StallAfter = TimeSpan.FromSeconds(10);

    /// <summary>Three missed beats (Unity sends one every 2 s from a pool thread): the whole editor is reloading or frozen.</summary>
    public static readonly TimeSpan HeartbeatStaleAfter = TimeSpan.FromSeconds(6);

    const int LogTailLines = 30;

    /// <summary>One line naming what holds the editor, derived from what the matrix can observe.</summary>
    public string Conclusion() => Conclude(Snapshot());

    /// <summary>True while the linked editor is compiling or reloading scripts, so a failed call is worth retrying shortly.</summary>
    public bool IsCompilingOrReloading()
        => registry.GetLatestLink() is { } link && IsCompilingOrReloading(link);

    public static bool IsCompilingOrReloading(UnityLink link)
        => link.EditorState is FmpEditorState.Compiling or FmpEditorState.Reloading && !ProcessExited(link);

    /// <summary>Conclusion plus in-flight calls and open editor windows; short enough for an error message.</summary>
    public string Summary()
    {
        var facts = Snapshot();
        var text = new StringBuilder(Conclude(facts));
        AppendWindows(text, facts, compact: true);
        return text.ToString();
    }

    /// <summary>The machine-readable answer of <c>editor-health</c>: can a call go through now, and if not, why and when to ask again.</summary>
    public Readiness GetReadiness() => ReadinessOf(Snapshot());

    internal static Readiness ReadinessOf(Facts facts)
    {
        var link = facts.Link;
        var heartbeat = link?.Heartbeat;
        var compiles = heartbeat?.CompileCount ?? 0;
        if (link is null)
            return new Readiness(false, "not_linked", 0, compiles);
        if (!link.Connected)
        {
            if (facts.ProcessAlive == false)
                return new Readiness(false, "not_linked", 0, compiles);
            var down = facts.Now - link.LastSeen;
            if (down > facts.ReconnectGrace)
                return new Readiness(false, "not_linked", 0, compiles);
            return link.EditorState == FmpEditorState.Reloading
                ? new Readiness(false, "reloading", 3000, compiles)
                : new Readiness(false, "reconnecting", 3000, compiles);
        }
        if (heartbeat is null)
            return new Readiness(true, "ready", 0, compiles);
        if (facts.Now - link.HeartbeatAt > HeartbeatStaleAfter)
            return new Readiness(false, "reloading", 3000, compiles);
        if (facts.MainTickAge > StallAfter)
            return new Readiness(false, "stalled", 10000, compiles);
        if ((heartbeat.Phase & FmpPhases.RunningTests) != 0)
            return new Readiness(false, "running_tests", 10000, compiles);
        if ((heartbeat.Phase & FmpPhases.PlaymodeTransition) != 0)
            return new Readiness(false, "playmode_transition", 1000, compiles);
        if (link.EditorState == FmpEditorState.Compiling)
            return new Readiness(false, "compiling", 2000, compiles);
        if (link.EditorState == FmpEditorState.Reloading)
            return new Readiness(false, "reloading", 3000, compiles);
        if (heartbeat.Updating)
            return new Readiness(false, "importing", 2000, compiles);
        return new Readiness(true, link.EditorState switch
        {
            FmpEditorState.Playing => "playing",
            FmpEditorState.Paused => "paused",
            _ => "ready",
        }, 0, compiles);
    }

    /// <summary>The full <c>editor-health</c> report.</summary>
    public string Report()
    {
        var facts = Snapshot();
        var text = new StringBuilder();
        text.AppendLine(Conclude(facts));
        text.AppendLine();

        var link = facts.Link;
        if (link is null)
        {
            text.AppendLine("No Unity editor has registered with this server since it started.");
            return text.ToString().TrimEnd();
        }

        var heartbeat = link.Heartbeat;
        text.AppendLine($"Project: {link.ProjectId} (plugin {link.PluginVersion ?? "?"})");
        text.AppendLine($"Link: {(link.Connected ? "connected" : $"down for {Seconds(facts.Now - link.LastSeen)}")}, editor state {link.EditorState}");
        if (heartbeat is null)
            text.AppendLine("Heartbeat: none (this plugin does not send one)");
        else
        {
            text.AppendLine($"Heartbeat: {Seconds(facts.Now - link.HeartbeatAt)} ago; main thread last ticked {Seconds(facts.MainTickAge)} ago; " +
                            $"importing {YesNo(heartbeat.Updating)}; focused {YesNo(heartbeat.Focused)}");
            text.AppendLine($"Unity process: {heartbeat.ProcessId}, {DescribeProcess(facts)}");
        }

        text.AppendLine(InFlightLine(facts));
        AppendWindows(text, facts, compact: false);

        var logPath = heartbeat?.ConsoleLogPath;
        if (!string.IsNullOrEmpty(logPath))
        {
            text.AppendLine();
            text.AppendLine($"Last {LogTailLines} lines of {logPath} (shared by every editor that writes the same file):");
            text.AppendLine(ReadTail(logPath, LogTailLines));
        }
        return text.ToString().TrimEnd();
    }

    public sealed record Readiness(bool Ready, string Phase, int RetryAfterMs, int CompileCount);

    internal sealed record Facts(
        DateTimeOffset Now,
        UnityLink? Link,
        TimeSpan MainTickAge,
        bool? ProcessAlive,
        bool? ProcessResponding,
        IReadOnlyList<PendingOperation> MatrixInFlight,
        IReadOnlyList<string>? Windows,
        TimeSpan ReconnectGrace);

    Facts Snapshot()
    {
        var now = time.GetUtcNow();
        var link = registry.GetLatestLink();
        var heartbeat = link?.Heartbeat;
        var tickAge = heartbeat is { LastMainTickUnixMs: > 0 }
            ? now - DateTimeOffset.FromUnixTimeMilliseconds(heartbeat.LastMainTickUnixMs)
            : TimeSpan.Zero;

        bool? alive = null, responding = null;
        IReadOnlyList<string>? windows = null;
        if (heartbeat is { ProcessId: > 0 })
        {
            try
            {
                using var process = Process.GetProcessById(heartbeat.ProcessId);
                alive = !process.HasExited;
                if (alive == true && OperatingSystem.IsWindows())
                {
                    responding = !WindowProbe.AnyHung(heartbeat.ProcessId);
                    windows = WindowProbe.ReadWindows(heartbeat.ProcessId);
                }
            }
            catch (ArgumentException)
            {
                alive = false;
            }
            catch (InvalidOperationException)
            {
                alive = false;
            }
        }

        var inFlight = registry.Operations
            .Where(op => link is null || op.ProjectId == link.ProjectId || op.ProjectId.Length == 0)
            .OrderBy(op => op.Started)
            .ToArray();
        return new Facts(now, link, tickAge, alive, responding, inFlight, windows, options.Value.ReconnectGrace);
    }

    internal static string Conclude(Facts facts)
    {
        var link = facts.Link;
        if (link is null)
            return "Not linked: no Unity editor has connected to this server.";

        if (!link.Connected)
        {
            var down = facts.Now - link.LastSeen;
            if (facts.ProcessAlive == false)
                return $"Not linked: the Unity process has exited (link down {Seconds(down)}).";
            if (link.EditorState == FmpEditorState.Reloading && down <= facts.ReconnectGrace)
                return $"Reloading: the editor is reloading its scripts (link down {Seconds(down)}).";
            if (down <= facts.ReconnectGrace)
                return $"Reconnecting: link down {Seconds(down)}, waiting up to {Seconds(facts.ReconnectGrace)} for the editor to come back.";
            return $"Not linked: link down {Seconds(down)}, longer than the {Seconds(facts.ReconnectGrace)} reconnect grace.";
        }

        var heartbeat = link.Heartbeat;
        if (heartbeat is null && !string.IsNullOrEmpty(link.ProjectRoot))
            return "Linked just now; waiting for the editor's first heartbeat. " + InFlightLine(facts);
        if (heartbeat is null)
            return "Linked, but this Unity plugin sends no heartbeat (older plugin), so its health is unknown. " + InFlightLine(facts);

        var heartbeatAge = facts.Now - link.HeartbeatAt;
        if (heartbeatAge > HeartbeatStaleAfter)
            return $"No heartbeat for {Seconds(heartbeatAge)} while the link is still open: the editor is reloading its scripts, " +
                   "or its whole process is frozen (for example under a debugger). " + InFlightLine(facts);

        var state = link.EditorState switch
        {
            FmpEditorState.Playing => "Playing",
            FmpEditorState.Paused => "Paused",
            _ => "Edit mode",
        };

        if (facts.MainTickAge > StallAfter)
        {
            var busy = link.EditorState == FmpEditorState.Compiling ? " while compiling scripts"
                : heartbeat.Updating ? " while importing assets"
                : string.Empty;
            // a call that was already running when the main thread stopped ticking can be the cause; later ones only wait
            var stallStart = facts.Now - facts.MainTickAge;
            var suspects = facts.MatrixInFlight.Where(op => op.Started <= stallStart + TimeSpan.FromSeconds(1)).ToArray();
            var cause = suspects.Length > 0
                ? $" Likely cause: {suspects[0].ToolName}, which was running when the main thread stopped."
                : " The cause is outside Matrix (no Matrix call was running when it stopped): read the editor windows.";
            return $"Main thread stalled {Seconds(facts.MainTickAge)}{busy} ({state}); Matrix tools in flight: {InFlightList(facts)}.{cause}";
        }

        if ((heartbeat.Phase & FmpPhases.RunningTests) != 0)
            return $"Busy: running tests (main thread ticked {Seconds(facts.MainTickAge)} ago).";
        if ((heartbeat.Phase & FmpPhases.PlaymodeTransition) != 0)
            return $"Busy: switching play mode (main thread ticked {Seconds(facts.MainTickAge)} ago).";
        if (link.EditorState == FmpEditorState.Compiling)
            return $"Busy: compiling scripts (main thread ticked {Seconds(facts.MainTickAge)} ago).";
        if (heartbeat.Updating)
            return $"Busy: importing assets (main thread ticked {Seconds(facts.MainTickAge)} ago).";
        if (!heartbeat.Focused)
            return $"Ticking slowly, Editor not focused ({state}, main thread ticked {Seconds(facts.MainTickAge)} ago); this is not a hang.";
        return $"Responsive ({state}, main thread ticked {Seconds(facts.MainTickAge)} ago).";
    }

    /// <summary>True only when the editor's process id is known and that process is gone.</summary>
    public static bool ProcessExited(UnityLink link)
    {
        var processId = link.Heartbeat?.ProcessId ?? 0;
        if (processId <= 0)
            return false;
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    static string InFlightLine(Facts facts) => $"Matrix tools in flight: {InFlightList(facts)}.";

    static string InFlightList(Facts facts)
    {
        if (facts.MatrixInFlight.Count == 0)
            return "none";
        return string.Join(", ", facts.MatrixInFlight.Select(op =>
            $"{(op.ToolName.Length == 0 ? op.OperationId : op.ToolName)} ({Seconds(facts.Now - op.Started)}{(op.Deferred ? ", waiting for a reload" : string.Empty)})"));
    }

    static void AppendWindows(StringBuilder text, Facts facts, bool compact)
    {
        if (facts.Link?.Heartbeat is null || facts.ProcessAlive != true)
            return;
        if (!OperatingSystem.IsWindows())
        {
            text.Append(compact ? " " : "\n").Append("Editor windows: not readable on this operating system.");
            return;
        }
        var windows = facts.Windows ?? [];
        if (compact)
        {
            text.Append(" Editor windows: ").Append(windows.Count == 0 ? "none with text" : string.Join(" | ", windows.Take(8)));
            return;
        }
        text.AppendLine(windows.Count == 0 ? "Editor windows: none with text" : "Editor windows (title, then the text inside):");
        foreach (var window in windows)
            text.Append("  ").AppendLine(window);
    }

    static string DescribeProcess(Facts facts) => facts.ProcessAlive switch
    {
        false => "not running",
        true when facts.ProcessResponding is { } responding => responding ? "running, window responding" : "running, window NOT responding",
        true => "running",
        _ => "unknown",
    };

    internal static string ReadTail(string path, int lines)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            const int window = 32 * 1024;
            stream.Seek(Math.Max(0, stream.Length - window), SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var all = reader.ReadToEnd().Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            return string.Join("\n", all.Skip(Math.Max(0, all.Length - lines)));
        }
        catch (Exception e)
        {
            return $"(could not read the log: {e.Message})";
        }
    }

    static string YesNo(bool value) => value ? "yes" : "no";

    internal static string Seconds(TimeSpan span)
        => span.TotalSeconds < 10 ? $"{Math.Max(0, span.TotalSeconds):0.0} s" : $"{span.TotalSeconds:0} s";
}
