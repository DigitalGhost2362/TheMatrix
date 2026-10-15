using Feeder.Matrix.Health;
using Feeder.Matrix.Protocol;
using Feeder.Matrix.Unity;

namespace Feeder.Matrix.UnitTests;

public class EditorHealthTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(180);

    private static UnityLink Link(bool connected = true, FmpEditorState state = FmpEditorState.Ready, HeartbeatMessage? heartbeat = null, TimeSpan? downFor = null)
        => new()
        {
            ConnectionId = "c",
            ProjectId = "p",
            UnityInstanceId = "i",
            SessionId = "s",
            Connected = connected,
            EditorState = state,
            LastSeen = Now - (downFor ?? TimeSpan.Zero),
            Heartbeat = heartbeat,
            HeartbeatAt = Now - TimeSpan.FromSeconds(1),
        };

    private static HeartbeatMessage Beat(TimeSpan tickAge, bool focused = true, bool updating = false)
        => new()
        {
            UnityInstanceId = "i",
            LastMainTickUnixMs = (Now - tickAge).ToUnixTimeMilliseconds(),
            Focused = focused,
            Updating = updating,
            ProcessId = 42,
        };

    private static string Conclude(UnityLink? link, TimeSpan? tickAge = null, bool? alive = true, params PendingOperation[] inFlight)
        => EditorHealth.Conclude(new EditorHealth.Facts(Now, link, tickAge ?? TimeSpan.Zero, alive, true, inFlight, [], Grace));

    [Fact]
    public void No_link_is_not_linked()
        => Assert.StartsWith("Not linked", Conclude(null));

    [Fact]
    public void Reload_state_reads_reloading()
        => Assert.StartsWith("Reloading", Conclude(Link(connected: false, state: FmpEditorState.Reloading, downFor: TimeSpan.FromSeconds(5))));

    [Fact]
    public void Exited_process_is_not_linked()
        => Assert.Contains("exited", Conclude(Link(connected: false, downFor: TimeSpan.FromSeconds(5)), alive: false));

    [Fact]
    public void Down_past_grace_is_not_linked()
        => Assert.StartsWith("Not linked", Conclude(Link(connected: false, downFor: TimeSpan.FromSeconds(400))));

    [Fact]
    public void Silent_heartbeat_on_an_open_link_reads_reloading_or_frozen()
    {
        var link = Link(heartbeat: Beat(TimeSpan.FromSeconds(12)));
        link.HeartbeatAt = Now - TimeSpan.FromSeconds(12);
        Assert.StartsWith("No heartbeat for 12 s", Conclude(link, TimeSpan.FromSeconds(12)));
    }

    [Fact]
    public void Relinked_new_plugin_waits_for_its_first_heartbeat()
    {
        var link = Link();
        link.ProjectRoot = "D:/Project";
        Assert.StartsWith("Linked just now", Conclude(link));
    }

    [Fact]
    public void Reregistering_drops_the_old_domain_heartbeat()
    {
        var registry = new UnityLinkRegistry();
        var request = new UnityRegisterRequest
        {
            ProtocolVersion = "1", ProjectId = "p", UnityInstanceId = "i", ProjectPathHash = "h", UnityVersion = "6",
            PluginVersion = "x", SupportedProtocolVersions = ["1"], ToolRegistryHash = "r", AuthToken = "", ProjectRoot = "D:/Project",
        };
        var link = registry.Register(request, "c1");
        link.Heartbeat = Beat(TimeSpan.FromSeconds(1));
        Assert.Null(registry.Register(request, "c2").Heartbeat);
    }

    [Fact]
    public void Old_plugin_without_heartbeat_is_unknown()
        => Assert.Contains("older plugin", Conclude(Link()));

    [Fact]
    public void Stall_without_matrix_calls_points_outside()
    {
        var text = Conclude(Link(heartbeat: Beat(TimeSpan.FromSeconds(340))), TimeSpan.FromSeconds(340));
        Assert.StartsWith("Main thread stalled 340 s", text);
        Assert.Contains("in flight: none", text);
    }

    [Fact]
    public void Stall_with_a_matrix_call_names_it()
    {
        var operation = new PendingOperation("op", "script-execute", "p", Now - TimeSpan.FromSeconds(300), Now);
        var text = Conclude(Link(state: FmpEditorState.Playing, heartbeat: Beat(TimeSpan.FromSeconds(300))), TimeSpan.FromSeconds(300), true, operation);
        Assert.Contains("script-execute (300 s)", text);
        Assert.Contains("Likely cause: script-execute", text);
        Assert.Contains("Playing", text);
    }

    [Fact]
    public void Call_that_arrived_after_the_stall_is_not_blamed()
    {
        var late = new PendingOperation("op", "scene-list-opened", "p", Now - TimeSpan.FromSeconds(60), Now);
        var text = Conclude(Link(heartbeat: Beat(TimeSpan.FromSeconds(77))), TimeSpan.FromSeconds(77), true, late);
        Assert.Contains("scene-list-opened (60 s)", text);
        Assert.Contains("outside Matrix", text);
        Assert.DoesNotContain("Likely cause", text);
    }

    [Fact]
    public void Unfocused_editor_ticking_is_slow_not_stalled()
    {
        var text = Conclude(Link(heartbeat: Beat(TimeSpan.FromSeconds(2), focused: false)), TimeSpan.FromSeconds(2));
        Assert.StartsWith("Ticking slowly, Editor not focused", text);
    }

    [Fact]
    public void Importing_and_compiling_are_busy()
    {
        Assert.StartsWith("Busy: importing", Conclude(Link(heartbeat: Beat(TimeSpan.FromSeconds(1), updating: true)), TimeSpan.FromSeconds(1)));
        Assert.StartsWith("Busy: compiling", Conclude(Link(state: FmpEditorState.Compiling, heartbeat: Beat(TimeSpan.FromSeconds(1))), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Healthy_focused_editor_is_responsive()
        => Assert.StartsWith("Responsive", Conclude(Link(heartbeat: Beat(TimeSpan.FromMilliseconds(50))), TimeSpan.FromMilliseconds(50)));

    [Fact]
    public void Log_tail_reads_a_file_another_process_writes()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var writer = new StreamWriter(new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));
            for (var i = 0; i < 100; i++)
                writer.WriteLine($"line {i}");
            writer.Flush();
            var tail = EditorHealth.ReadTail(path, 3);
            Assert.Equal("line 97\nline 98\nline 99", tail);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Deadline_only_moves_later()
    {
        var operation = new PendingOperation("op", "t", "p", Now, Now + TimeSpan.FromSeconds(60));
        operation.ExtendTo(Now + TimeSpan.FromSeconds(30));
        Assert.Equal(Now + TimeSpan.FromSeconds(60), operation.Deadline);
        operation.ExtendTo(Now + TimeSpan.FromSeconds(90));
        Assert.Equal(Now + TimeSpan.FromSeconds(90), operation.Deadline);
    }
}
