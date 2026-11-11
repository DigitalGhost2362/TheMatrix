using System.Text.Json;
using Feeder.Matrix.Backends;
using Feeder.Matrix.Health;
using Feeder.Matrix.Protocol;
using Feeder.Matrix.Tools;
using Feeder.Matrix.Unity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace Feeder.Matrix.UnitTests;

public sealed class EditorLockTests : IDisposable
{
    private readonly ManualTimeProvider _time = new();
    private readonly LockHub _hub = new();
    private readonly UnityLinkRegistry _registry = new();
    private readonly McpClientTracker _clients;
    private readonly AgentDirectory _agents;
    private readonly MatrixOptions _options = new();
    private readonly EditorLock _lock;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "matrix-lock-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, McpClientTracker.Entry> _entries = [];

    public EditorLockTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Temp"));
        _clients = new McpClientTracker(_hub, _time, NullLogger<McpClientTracker>.Instance);
        _agents = new AgentDirectory(_time, NullLogger<AgentDirectory>.Instance);
        _lock = new EditorLock(_registry, _clients, _agents, _hub, _time, Options.Create(_options), NullLogger<EditorLock>.Instance);
        Open("sid-a", "claude-code");
        Open("sid-b", "codex");
        Open("sid-c", "gemini");
        _options.EditorLockWaitSlice = TimeSpan.FromSeconds(30);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private string LockDir => Path.Combine(_root, "Temp", "agent-editor-lock");
    private string LockFile => Path.Combine(LockDir, "lock.json");

    private void Open(string sessionId, string client)
        => _entries[sessionId] = _clients.Register(sessionId, () => new Implementation { Name = client, Version = "2.0" });

    private UnityLink Link(FmpEditorState state = FmpEditorState.Ready, bool showsLock = true, bool agentPlay = false)
    {
        var link = _registry.Register(new UnityRegisterRequest
        {
            ProtocolVersion = "1",
            ProjectId = "proj",
            UnityInstanceId = "inst",
            ProjectPathHash = "proj",
            UnityVersion = "6000.0.32f1",
            PluginVersion = "1.0.2",
            SupportedProtocolVersions = ["1"],
            ToolRegistryHash = "hash",
            AuthToken = string.Empty,
            EditorState = state,
            ProjectRoot = _root,
            Features = [.. showsLock ? [FmpCapabilities.EditorLock] : Array.Empty<string>(), .. agentPlay ? [FmpCapabilities.AgentPlay] : Array.Empty<string>()],
        }, "conn-1");
        link.Registry = new RegistrySnapshot
        {
            ProjectId = "proj",
            RegistryHash = "hash",
            Tools =
            [
                new ToolDescriptor { Name = "rql-query", InputSchemaJsonUtf8 = [], ReadOnly = true },
                new ToolDescriptor { Name = "script-execute", InputSchemaJsonUtf8 = [] },
            ],
        };
        return link;
    }

    private static string Text(CallToolResult result) => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text));

    private void WriteOutsideLock(string owner, long heartbeat)
    {
        Directory.CreateDirectory(LockDir);
        File.WriteAllText(LockFile, $$"""{"owner":"{{owner}}","task":"catwalk score","acquired":"2026-10-05T07:00:00+07:00","heartbeat":{{heartbeat}}}""");
    }

    [Fact]
    public void Holder_runs_everything_others_only_read_only_tools()
    {
        Link();
        var acquired = _lock.Acquire("sid-a", "claude-catwalk", "catwalk score");

        Assert.NotEqual(true, acquired.IsError);
        Assert.StartsWith("acquired", Text(acquired));
        Assert.Null(_lock.Refusal("sid-a", "script-execute"));
        Assert.Null(_lock.Refusal("sid-b", "rql-query"));
        Assert.Null(_lock.Refusal("sid-b", "tool-set-enabled-state"));
        var refusal = _lock.Refusal("sid-b", "script-execute");
        Assert.NotNull(refusal);
        Assert.Contains("claude-catwalk", refusal);
        Assert.Contains("catwalk score", refusal);
        Assert.Contains("claude-code 2.0", refusal);
    }

    [Fact]
    public void Tests_run_is_refused_to_others_while_a_session_holds_the_lock()
    {
        Link();
        _lock.Acquire("sid-a", "claude-catwalk", "run edit mode tests");

        Assert.Null(_lock.Refusal("sid-a", "tests-run"));
        var refusal = _lock.Refusal("sid-b", "tests-run");
        Assert.NotNull(refusal);
        Assert.Contains("claude-catwalk", refusal);
    }

    [Fact]
    public void A_running_tests_call_keeps_the_claim_past_the_idle_timeout()
    {
        Link();
        _lock.Acquire("sid-a", "claude-catwalk", "run play mode tests");

        using (_lock.Track("sid-a", "script-execute"))
        {
            _time.Advance(_options.EditorLockIdleTimeout + TimeSpan.FromMinutes(5));
            Assert.NotNull(_lock.Refusal("sid-b", "tests-run"));
        }

        _time.Advance(_options.EditorLockIdleTimeout + TimeSpan.FromSeconds(1));
        Assert.Null(_lock.Refusal("sid-b", "tests-run"));
    }

    [Fact]
    public void Free_editor_refuses_nobody()
    {
        Link();
        Assert.Null(_lock.Refusal("sid-b", "script-execute"));
    }

    [Fact]
    public void Busy_acquire_names_the_holder_and_release_hands_it_over()
    {
        Link();
        _lock.Acquire("sid-a", "claude-catwalk", "catwalk score");

        var busy = _lock.Acquire("sid-b", null, "shop panel");
        Assert.True(busy.IsError);
        Assert.Contains("busy: held by claude-catwalk", Text(busy));

        var notMine = _lock.Release("sid-b");
        Assert.NotEqual(true, notMine.IsError);
        Assert.Contains("you do not hold the lock", Text(notMine));

        Assert.StartsWith("released", Text(_lock.Release("sid-a")));
        Assert.NotEqual(true, _lock.Acquire("sid-b", null, "shop panel").IsError);
        Assert.Equal("codex", _lock.Snapshot().Owner);
    }

    [Fact]
    public void Acquiring_again_renews_the_claim()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first burst");
        _time.Advance(TimeSpan.FromMinutes(5));

        var again = _lock.Acquire("sid-a", "claude", "second burst");

        Assert.Contains("new claim", Text(again));
        var state = _lock.Snapshot();
        Assert.Equal("second burst", state.Task);
        Assert.Equal(_time.GetUtcNow().ToUnixTimeMilliseconds(), state.AcquiredUnixMs);
    }

    [Fact]
    public async Task Closed_session_frees_its_claim()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "play test");

        await _clients.EndAsync("sid-a", _entries["sid-a"]);

        Assert.False(_lock.Snapshot().Held);
        Assert.Null(_lock.Refusal("sid-b", "script-execute"));
    }

    [Fact]
    public void Idle_claim_is_freed_but_a_running_call_keeps_it()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "long compile");

        using (_lock.Track("sid-a", "script-execute"))
        {
            _time.Advance(_options.EditorLockIdleTimeout + TimeSpan.FromMinutes(1));
            Assert.NotNull(_lock.Refusal("sid-b", "script-execute"));
        }

        _time.Advance(_options.EditorLockIdleTimeout - TimeSpan.FromSeconds(1));
        Assert.NotNull(_lock.Refusal("sid-b", "script-execute"));

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(_lock.Refusal("sid-b", "script-execute"));
        Assert.Contains("already freed", Text(_lock.Release("sid-a")));
    }

    [Fact]
    public void Claim_is_mirrored_to_the_lock_file_and_removed_on_release()
    {
        Link();
        _lock.Acquire("sid-a", "claude-catwalk", "catwalk score");

        using (var document = JsonDocument.Parse(File.ReadAllText(LockFile)))
        {
            var json = document.RootElement;
            Assert.Equal("claude-catwalk", json.GetProperty("owner").GetString());
            Assert.Equal("catwalk score", json.GetProperty("task").GetString());
            Assert.Equal(_time.GetUtcNow().ToUnixTimeSeconds(), json.GetProperty("heartbeat").GetInt64());
            Assert.True(json.GetProperty("matrix").GetBoolean());
        }
        // the lock-file scripts match on this exact text
        Assert.Contains("\"owner\":\"claude-catwalk\"", File.ReadAllText(LockFile));

        _lock.Release("sid-a");
        Assert.False(Directory.Exists(LockDir));
    }

    [Fact]
    public async Task Live_outside_lock_file_blocks_acquire_and_shows_in_the_editor()
    {
        var link = Link();
        WriteOutsideLock("gemini-shop", _time.GetUtcNow().ToUnixTimeSeconds() - 60);

        var busy = _lock.Acquire("sid-a", "claude", "play test");

        Assert.True(busy.IsError);
        Assert.Contains("gemini-shop", Text(busy));
        Assert.True(Directory.Exists(LockDir));

        await _lock.TickAsync();
        var shown = _hub.Sent[link.ConnectionId][^1];
        Assert.True(shown.Held);
        Assert.Equal(EditorLock.KindOutside, shown.Kind);
        Assert.Equal("gemini-shop", shown.Owner);
    }

    [Fact]
    public void Stale_outside_lock_file_is_removed_and_the_claim_taken()
    {
        Link();
        WriteOutsideLock("gemini-shop", _time.GetUtcNow().ToUnixTimeSeconds() - 1300);

        Assert.NotEqual(true, _lock.Acquire("sid-a", "claude", "play test").IsError);
        Assert.Contains("\"owner\":\"claude\"", File.ReadAllText(LockFile));
    }

    [Fact]
    public void Leftover_mirror_of_an_earlier_matrix_does_not_block()
    {
        Link();
        Directory.CreateDirectory(LockDir);
        File.WriteAllText(LockFile, """{"owner":"old","task":"t","acquired":"x","heartbeat":1,"matrix":true,"session":"gone"}""");
        File.SetLastWriteTimeUtc(LockFile, DateTime.UtcNow);

        Assert.NotEqual(true, _lock.Acquire("sid-a", "claude", "play test").IsError);
    }

    [Fact]
    public void Play_mode_with_no_holder_is_busy()
    {
        Link(FmpEditorState.Playing);

        var busy = _lock.Acquire("sid-a", "claude", "play test");

        Assert.True(busy.IsError);
        Assert.Contains("play mode", Text(busy));
    }

    [Fact]
    public void Editor_can_free_an_agent_claim()
    {
        Link();
        _lock.Acquire("sid-a", "claude-catwalk", "catwalk score");

        Assert.Equal("the claim of claude-catwalk", _lock.ForceRelease());
        Assert.Null(_lock.Refusal("sid-b", "script-execute"));
        Assert.Contains("already freed", Text(_lock.Release("sid-a")));
        Assert.Equal("nothing", _lock.ForceRelease());
    }

    [Fact]
    public void Editor_can_remove_a_lock_file()
    {
        Link();
        WriteOutsideLock("gemini-shop", _time.GetUtcNow().ToUnixTimeSeconds());

        Assert.Equal("the lock file of gemini-shop", _lock.ForceRelease());
        Assert.False(Directory.Exists(LockDir));
    }

    [Fact]
    public async Task Only_links_that_show_the_lock_get_it()
    {
        var link = Link(showsLock: false);
        _lock.Acquire("sid-a", "claude", "play test");
        await _lock.TickAsync();
        Assert.False(_hub.Sent.ContainsKey(link.ConnectionId));

        link.Features = [FmpCapabilities.EditorLock];
        await _lock.PublishAsync(link);
        Assert.Equal("claude", Assert.Single(_hub.Sent[link.ConnectionId]).Owner);
    }

    [Fact]
    public async Task Unchanged_state_is_not_sent_again()
    {
        var link = Link();
        _lock.Acquire("sid-a", "claude", "play test");
        await _lock.PublishAsync();
        var count = _hub.Sent[link.ConnectionId].Count;

        await _lock.TickAsync();
        await _lock.TickAsync();

        Assert.Equal(count, _hub.Sent[link.ConnectionId].Count);
    }

    [Fact]
    public void Status_says_whether_it_is_yours()
    {
        Link();
        Assert.StartsWith("free.", Text(_lock.Status("sid-a")));
        _lock.Acquire("sid-a", "claude", "play test");

        Assert.StartsWith("you hold it", Text(_lock.Status("sid-a")));
        Assert.StartsWith("held by claude", Text(_lock.Status("sid-b")));
    }

    [Fact]
    public void Acquire_needs_a_task()
    {
        Link();
        Assert.True(_lock.Acquire("sid-a", "claude", "  ").IsError);
        Assert.True(_lock.Acquire(null, "claude", "task").IsError);
    }

    private Task<CallToolResult> Wait(string sessionId, string? ticket = null, CancellationToken ct = default)
        => _lock.AcquireAsync(sessionId, null, "task of " + sessionId, true, ticket, ct);

    private static Task<T> Done<T>(Task<T> call) => call.WaitAsync(TimeSpan.FromSeconds(5));

    private static string Ticket(CallToolResult queued) => queued.StructuredContent!.Value.GetProperty("error").GetProperty("ticket").GetString()!;

    private static string[] Order(EditorLockState state) => state.Waiting.Select(w => w.Owner).ToArray();

    [Fact]
    public async Task Free_lock_with_an_empty_queue_is_granted_at_once()
    {
        Link();

        var result = await Done(Wait("sid-a"));

        Assert.NotEqual(true, result.IsError);
        Assert.StartsWith("acquired", Text(result));
        Assert.True(result.StructuredContent!.Value.GetProperty("acquired").GetBoolean());
        Assert.Empty(_lock.Snapshot().Waiting);
    }

    [Fact]
    public async Task Three_sessions_are_served_first_come_first_served()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");
        var b = Wait("sid-b");
        var c = Wait("sid-c");

        Assert.Equal(["codex", "gemini"], Order(_lock.Snapshot()));
        Assert.False(b.IsCompleted);

        _lock.Release("sid-a");
        var granted = await Done(b);
        Assert.StartsWith("acquired", Text(granted));
        Assert.Contains("after waiting", Text(granted));
        Assert.False(c.IsCompleted);
        Assert.Equal("codex", _lock.Snapshot().Owner);
        Assert.Equal(["gemini"], Order(_lock.Snapshot()));

        _lock.Release("sid-b");
        Assert.StartsWith("acquired", Text(await Done(c)));
        Assert.Equal("gemini", _lock.Snapshot().Owner);
    }

    [Fact]
    public async Task Releasing_and_asking_again_goes_to_the_end_of_the_queue()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");
        var b = Wait("sid-b");

        _lock.Release("sid-a");
        await Done(b);
        var c = Wait("sid-c");
        var a = Wait("sid-a");
        Assert.Equal(["gemini", "claude-code"], Order(_lock.Snapshot()));

        _lock.Release("sid-b");
        Assert.StartsWith("acquired", Text(await Done(c)));
        Assert.False(a.IsCompleted);
    }

    [Fact]
    public async Task Slice_that_runs_out_answers_queued_and_the_ticket_keeps_the_place()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");
        _options.EditorLockWaitSlice = TimeSpan.FromMilliseconds(100);

        var first = await Done(Wait("sid-b"));
        var behind = Wait("sid-c");

        Assert.NotEqual(true, first.IsError);
        Assert.Contains("queued: position 1 (0 ahead)", Text(first));
        var ticket = Ticket(first);
        Assert.Equal(1, first.StructuredContent!.Value.GetProperty("error").GetProperty("position").GetInt32());

        var again = await Done(Wait("sid-b", ticket));
        Assert.Equal(ticket, Ticket(again));
        Assert.Contains("position 1", Text(again));
        Assert.Equal(["codex", "gemini"], Order(_lock.Snapshot()));
        await Done(behind);
    }

    [Fact]
    public async Task Ticket_not_called_again_in_time_is_removed()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");
        _options.EditorLockWaitSlice = TimeSpan.FromMilliseconds(100);
        var queued = await Done(Wait("sid-b"));
        var ticket = Ticket(queued);

        _time.Advance(_options.EditorLockWaitSlice + _options.EditorLockTicketGrace - TimeSpan.FromSeconds(1));
        await _lock.TickAsync();
        Assert.Single(_lock.Snapshot().Waiting);

        _time.Advance(TimeSpan.FromSeconds(2));
        await _lock.TickAsync();
        Assert.Empty(_lock.Snapshot().Waiting);

        var late = await Done(Wait("sid-b", ticket));
        Assert.NotEqual(ticket, Ticket(late));
        Assert.Single(_lock.Snapshot().Waiting);
    }

    [Fact]
    public async Task Closed_session_leaves_the_queue_and_the_turn_passes_on()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");
        var b = Wait("sid-b");
        var c = Wait("sid-c");

        await _clients.EndAsync("sid-b", _entries["sid-b"]);
        Assert.Equal(["gemini"], Order(_lock.Snapshot()));
        Assert.True((await Done(b)).IsError);

        _lock.Release("sid-a");
        Assert.StartsWith("acquired", Text(await Done(c)));
    }

    [Fact]
    public async Task Cancelled_call_leaves_the_queue()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");
        using var cancel = new CancellationTokenSource();
        var b = Wait("sid-b", ct: cancel.Token);
        var c = Wait("sid-c");

        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b);
        Assert.Equal(["gemini"], Order(_lock.Snapshot()));

        _lock.Release("sid-a");
        Assert.StartsWith("acquired", Text(await Done(c)));
    }

    [Fact]
    public async Task Head_that_is_between_two_calls_is_skipped_but_keeps_its_place()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");
        _options.EditorLockWaitSlice = TimeSpan.FromMilliseconds(100);
        var queued = await Done(Wait("sid-b"));
        _options.EditorLockWaitSlice = TimeSpan.FromSeconds(30);
        var c = Wait("sid-c");

        _lock.Release("sid-a");

        Assert.StartsWith("acquired", Text(await Done(c)));
        Assert.Equal(["codex"], Order(_lock.Snapshot()));
        Assert.Equal(Ticket(queued), _lock.Snapshot().Waiting[0].Ticket);
    }

    [Fact]
    public async Task Granted_session_that_makes_no_call_loses_its_turn()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");
        var b = Wait("sid-b");
        var c = Wait("sid-c");
        _lock.Release("sid-a");
        await Done(b);

        _time.Advance(_options.EditorLockFirstCallDeadline - TimeSpan.FromSeconds(1));
        await _lock.TickAsync();
        Assert.Equal("codex", _lock.Snapshot().Owner);

        _time.Advance(TimeSpan.FromSeconds(2));
        await _lock.TickAsync();
        Assert.StartsWith("acquired", Text(await Done(c)));
        Assert.Equal("gemini", _lock.Snapshot().Owner);
        Assert.Contains("already freed", Text(_lock.Release("sid-b")));
    }

    [Fact]
    public async Task Granted_session_that_calls_a_tool_keeps_the_lock()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");
        var b = Wait("sid-b");
        _lock.Release("sid-a");
        await Done(b);

        _time.Advance(TimeSpan.FromSeconds(30));
        using (_lock.Track("sid-b", "script-execute"))
            _time.Advance(TimeSpan.FromSeconds(40));
        await _lock.TickAsync();

        Assert.Equal("codex", _lock.Snapshot().Owner);
    }

    [Fact]
    public async Task Idle_claim_is_freed_after_two_minutes_while_others_wait_and_after_ten_without()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");
        _time.Advance(TimeSpan.FromMinutes(9));
        await _lock.TickAsync();
        Assert.True(_lock.Snapshot().Held);

        var b = Wait("sid-b");
        await _lock.TickAsync();
        Assert.StartsWith("acquired", Text(await Done(b)));
        Assert.Equal("codex", _lock.Snapshot().Owner);

        var c = Wait("sid-c");
        using (_lock.Track("sid-b", "script-execute"))
        {
            _time.Advance(_options.EditorLockWaitingIdleTimeout + TimeSpan.FromMinutes(1));
            await _lock.TickAsync();
            Assert.Equal("codex", _lock.Snapshot().Owner);
        }

        _time.Advance(_options.EditorLockWaitingIdleTimeout - TimeSpan.FromSeconds(1));
        await _lock.TickAsync();
        Assert.Equal("codex", _lock.Snapshot().Owner);
        _time.Advance(TimeSpan.FromSeconds(2));
        await _lock.TickAsync();
        Assert.StartsWith("acquired", Text(await Done(c)));
    }

    [Fact]
    public async Task Queue_waits_while_a_person_plays_and_resumes_when_play_stops()
    {
        var link = Link(FmpEditorState.Playing);
        var b = Wait("sid-b");

        await _lock.TickAsync();
        Assert.False(b.IsCompleted);
        var state = _lock.Snapshot();
        Assert.True(state.QueueHeldByPlayMode);
        Assert.Equal(["codex"], Order(state));

        link.EditorState = FmpEditorState.Ready;
        await _lock.TickAsync();

        Assert.StartsWith("acquired", Text(await Done(b)));
        Assert.False(_lock.Snapshot().QueueHeldByPlayMode);
    }

    [Fact]
    public async Task Slice_during_play_mode_answers_queued_with_the_reason()
    {
        Link(FmpEditorState.Playing);
        _options.EditorLockWaitSlice = TimeSpan.FromMilliseconds(100);

        var queued = await Done(Wait("sid-b"));

        Assert.NotEqual(true, queued.IsError);
        Assert.Contains("play mode", Text(queued));
        Assert.Contains("queued: position 1", Text(queued));
    }

    [Fact]
    public async Task Paused_queue_grants_nobody_until_it_resumes()
    {
        Link();
        _lock.SetQueuePaused(true);
        Assert.Contains("paused", Text(_lock.Acquire("sid-a", "claude", "first")));
        var b = Wait("sid-b");
        await _lock.TickAsync();
        Assert.False(b.IsCompleted);
        Assert.True(_lock.Snapshot().QueuePaused);

        _lock.SetQueuePaused(false);

        Assert.StartsWith("acquired", Text(await Done(b)));
    }

    [Fact]
    public async Task Person_removes_a_waiting_session_from_the_queue()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");
        var b = Wait("sid-b");
        var c = Wait("sid-c");
        var ticket = _lock.Snapshot().Waiting[0].Ticket;

        Assert.Equal("codex", _lock.RemoveWaiter(ticket));
        Assert.Equal("nothing", _lock.RemoveWaiter(ticket));

        var removed = await Done(b);
        Assert.True(removed.IsError);
        Assert.Contains("removed", Text(removed));
        Assert.Equal(["gemini"], Order(_lock.Snapshot()));
        Assert.Contains("removed", Text(await Done(Wait("sid-b", ticket))));
        _lock.Release("sid-a");
        Assert.StartsWith("acquired", Text(await Done(c)));
    }

    [Fact]
    public void Waiting_session_can_leave_with_release()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");
        _ = Wait("sid-b");

        Assert.Contains("left the editor lock queue", Text(_lock.Release("sid-b")));
        Assert.Empty(_lock.Snapshot().Waiting);
    }

    [Fact]
    public async Task No_wait_answers_busy_and_never_queues()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");

        var busy = await Done(_lock.AcquireAsync("sid-b", null, "t", wait: false, ticket: null, CancellationToken.None));

        Assert.True(busy.IsError);
        Assert.Contains("busy: held by claude", Text(busy));
        Assert.Equal("editor_locked", busy.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(_lock.Snapshot().Waiting);
    }

    [Fact]
    public void Refusal_carries_the_holder_the_queue_and_a_retry_hint()
    {
        Link();
        _lock.Acquire("sid-a", "claude-catwalk", "catwalk score");
        _ = Wait("sid-c");

        var refusal = _lock.RefusalResult("sid-b", "script-execute")!;

        Assert.True(refusal.IsError);
        var error = refusal.StructuredContent!.Value.GetProperty("error");
        Assert.Equal("editor_locked", error.GetProperty("code").GetString());
        Assert.Equal(5000, error.GetProperty("retry_after_ms").GetInt32());
        Assert.Equal("claude-catwalk", error.GetProperty("holder").GetString());
        Assert.Equal(1, error.GetProperty("queue_length").GetInt32());
        Assert.Contains("Refused 'script-execute'", Text(refusal));
        Assert.Null(_lock.RefusalResult("sid-a", "script-execute"));
    }

    [Fact]
    public void Status_lists_the_queue_and_the_position_of_the_asker()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");
        _ = Wait("sid-b");
        _ = Wait("sid-c");

        var status = _lock.Status("sid-c");

        Assert.Contains("2 waiting", Text(status));
        Assert.Contains("You are number 2", Text(status));
        var json = status.StructuredContent!.Value;
        Assert.Equal(2, json.GetProperty("position").GetInt32());
        Assert.Equal(2, json.GetProperty("waiting").GetArrayLength());
        Assert.Equal(0, _lock.Status("sid-a").StructuredContent!.Value.GetProperty("position").GetInt32());
    }

    [Fact]
    public async Task Queue_is_pushed_to_the_editor()
    {
        var link = Link();
        _lock.Acquire("sid-a", "claude", "first");
        _ = Wait("sid-b");
        await _lock.PublishAsync();

        var waiter = Assert.Single(_hub.Sent[link.ConnectionId][^1].Waiting);

        Assert.Equal("codex", waiter.Owner);
        Assert.True(waiter.Attached);
        Assert.Equal(_time.GetUtcNow().ToUnixTimeMilliseconds(), waiter.WaitingSinceUnixMs);
    }

    #region Implicit lock

    private Task<CallToolResult?> Gate(string sessionId, string tool = "script-execute", CancellationToken ct = default)
        => _lock.GateAsync(sessionId, tool, ct);

    [Fact]
    public async Task Implicit_claim_is_taken_by_an_editor_changing_tool_when_the_editor_is_free()
    {
        Link();

        Assert.Null(await Done(Gate("sid-a")));

        var state = _lock.Snapshot();
        Assert.True(state.Held);
        Assert.Equal("script-execute (implicit)", state.Task);
        Assert.Null(await Done(Gate("sid-a")));
        Assert.Equal("sid-a", _lock.Snapshot().SessionId);
    }

    [Fact]
    public async Task Read_only_and_exempt_tools_never_wait_or_claim()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");

        Assert.Null(await Done(Gate("sid-b", "rql-query")));
        Assert.Null(await Done(Gate("sid-b", "tool-set-enabled-state")));
        Assert.Empty(_lock.Snapshot().Waiting);
        Assert.Equal("sid-a", _lock.Snapshot().SessionId);
    }

    [Fact]
    public async Task Implicit_claim_is_freed_after_a_short_idle_and_not_when_a_call_is_running()
    {
        Link();
        await Gate("sid-a");

        using (_lock.Track("sid-a", "script-execute"))
        {
            _time.Advance(_options.EditorLockImplicitIdle + TimeSpan.FromMinutes(1));
            Assert.Equal("sid-a", _lock.Snapshot().SessionId);
        }

        _time.Advance(_options.EditorLockImplicitIdle + TimeSpan.FromSeconds(1));
        Assert.Null(await Done(Gate("sid-b")));
        Assert.Equal("sid-b", _lock.Snapshot().SessionId);
    }

    [Fact]
    public async Task Non_holder_waits_in_the_queue_and_runs_when_the_holder_releases()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");

        var call = Gate("sid-b");
        await Task.Delay(50);
        Assert.False(call.IsCompleted);
        Assert.Equal(["codex"], Order(_lock.Snapshot()));

        _lock.Release("sid-a");

        Assert.Null(await Done(call));
        Assert.Equal("sid-b", _lock.Snapshot().SessionId);
        Assert.Empty(_lock.Snapshot().Waiting);
    }

    [Fact]
    public async Task Slice_that_runs_out_returns_the_queued_error_and_the_next_call_keeps_the_place()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");
        _options.EditorLockWaitSlice = TimeSpan.FromMilliseconds(100);

        var queued = (await Done(Gate("sid-b")))!;

        Assert.True(queued.IsError);
        Assert.Contains("queued: position 1", Text(queued));
        Assert.Contains("The tool did not run", Text(queued));
        Assert.Equal("lock_queued", queued.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());

        var again = (await Done(Gate("sid-b")))!;
        Assert.Contains("queued: position 1", Text(again));
        Assert.Equal(["codex"], Order(_lock.Snapshot()));
    }

    [Fact]
    public async Task Queued_sessions_get_the_editor_in_order_whether_they_come_through_tools_or_acquire()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");
        var b = Gate("sid-b");
        await Task.Delay(30);
        var c = Wait("sid-c");
        await Task.Delay(30);

        _lock.Release("sid-a");
        Assert.Null(await Done(b));
        Assert.False(c.IsCompleted);

        _lock.Release("sid-b");
        Assert.StartsWith("acquired", Text(await Done(c)));
    }

    [Fact]
    public async Task Human_in_play_mode_queues_the_tool_but_not_a_session_whose_implicit_claim_just_ended()
    {
        Link();
        await Gate("sid-a");
        _time.Advance(_options.EditorLockImplicitIdle + TimeSpan.FromSeconds(1));
        Link(FmpEditorState.Playing);

        // sid-a started that play session itself
        Assert.Null(await Done(Gate("sid-a")));

        // a stranger waits until play mode ends
        _options.EditorLockWaitSlice = TimeSpan.FromMilliseconds(100);
        _lock.Release("sid-a");
        var queued = (await Done(Gate("sid-b")))!;
        Assert.True(queued.IsError);
        Assert.Contains("play mode", Text(queued));
    }

    [Fact]
    public async Task Explicit_acquire_turns_an_implicit_claim_into_a_normal_one()
    {
        Link();
        await Gate("sid-a");

        _lock.Acquire("sid-a", "claude", "burst");
        _time.Advance(_options.EditorLockImplicitIdle + TimeSpan.FromSeconds(1));

        Assert.Equal("sid-a", _lock.Snapshot().SessionId);
        Assert.Equal("burst", _lock.Snapshot().Task);
    }

    [Fact]
    public async Task Switched_off_the_gate_refuses_at_once_and_never_claims()
    {
        Link();
        _options.ImplicitLock = false;

        Assert.Null(await Done(Gate("sid-a")));
        Assert.False(_lock.Snapshot().Held);

        _lock.Acquire("sid-a", "claude", "first");
        var refusal = (await Done(Gate("sid-b")))!;
        Assert.True(refusal.IsError);
        Assert.Contains("Refused 'script-execute'", Text(refusal));
        Assert.Empty(_lock.Snapshot().Waiting);
    }

    [Fact]
    public async Task Session_that_closes_while_its_tool_waits_leaves_the_queue()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");
        using var cts = new CancellationTokenSource();
        var call = Gate("sid-b", ct: cts.Token);
        await Task.Delay(30);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.Empty(_lock.Snapshot().Waiting);
    }

    #endregion

    #region Play mode of an agent

    private UnityLink Beat(UnityLink link, FmpEditorState state, bool agentPlay = true, int phase = 0)
    {
        link.EditorState = state;
        link.Heartbeat = new HeartbeatMessage { UnityInstanceId = "inst", EditorState = state, AgentPlay = agentPlay, Phase = phase };
        _lock.ObserveEditor(link);
        return link;
    }

    private List<string> Stops(UnityLink link) => _hub.Stops.TryGetValue(link.ConnectionId, out var list) ? list : [];

    private UnityLink AgentPlaying()
    {
        var link = Link(agentPlay: true);
        _lock.Acquire("sid-a", "claude", "play test");
        return Beat(link, FmpEditorState.Playing);
    }

    [Fact]
    public async Task Holder_in_its_own_play_session_keeps_the_lock_past_the_waiting_idle_limit()
    {
        var link = AgentPlaying();
        var b = Wait("sid-b");
        _ = Wait("sid-c");

        _time.Advance(TimeSpan.FromMinutes(3));
        await _lock.TickAsync();

        Assert.Equal("sid-a", _lock.Snapshot().SessionId);
        Assert.False(b.IsCompleted);
        Assert.Empty(Stops(link));
    }

    [Fact]
    public async Task Implicit_holder_that_entered_play_keeps_the_lock_past_the_implicit_idle()
    {
        var link = Link(agentPlay: true);
        await Gate("sid-a");
        Beat(link, FmpEditorState.Playing);
        _ = Wait("sid-b");

        _time.Advance(TimeSpan.FromMinutes(3));
        await _lock.TickAsync();

        Assert.Equal("sid-a", _lock.Snapshot().SessionId);
    }

    [Fact]
    public async Task Holder_idle_past_the_play_limit_is_freed_and_its_play_mode_stopped_once()
    {
        var link = AgentPlaying();
        _ = Wait("sid-b");

        _time.Advance(_options.EditorLockPlayIdleTimeout + TimeSpan.FromSeconds(1));
        await _lock.TickAsync();
        await _lock.TickAsync();

        Assert.NotEqual("sid-a", _lock.Snapshot().SessionId);
        Assert.Single(Stops(link));
    }

    [Fact]
    public async Task Holder_freed_while_tests_run_does_not_stop_play_mode()
    {
        var link = Link(agentPlay: true);
        _lock.Acquire("sid-a", "claude", "play mode tests");
        Beat(link, FmpEditorState.Playing, phase: FmpPhases.RunningTests);

        _time.Advance(_options.EditorLockPlayIdleTimeout + TimeSpan.FromSeconds(1));
        await _lock.TickAsync();

        Assert.False(_lock.Snapshot().Held);
        Assert.Empty(Stops(link));
    }

    [Fact]
    public async Task Owner_of_play_left_on_jumps_the_queue_with_acquire()
    {
        var link = AgentPlaying();
        _lock.Release("sid-a");
        var b = Wait("sid-b");
        _ = Wait("sid-c");
        await Task.Delay(30);
        Assert.False(b.IsCompleted);
        var state = _lock.Snapshot();
        Assert.Equal("claude", state.PlayLeftBy);
        Assert.False(state.QueueHeldByPlayMode);

        Assert.StartsWith("acquired", Text(await Done(Wait("sid-a"))));
        Assert.Equal(["codex", "gemini"], Order(_lock.Snapshot()));
        Assert.Empty(Stops(link));
    }

    [Fact]
    public async Task Owner_of_play_left_on_runs_an_editor_changing_tool_ahead_of_the_queue()
    {
        AgentPlaying();
        _lock.Release("sid-a");
        _ = Wait("sid-b");
        _ = Wait("sid-c");
        await Task.Delay(30);

        Assert.Null(await Done(Gate("sid-a")));
        Assert.Equal("sid-a", _lock.Snapshot().SessionId);
    }

    [Fact]
    public async Task Play_left_on_passes_to_the_head_of_the_queue_after_the_grace()
    {
        AgentPlaying();
        _lock.Release("sid-a");
        var b = Wait("sid-b");
        await Task.Delay(30);

        _time.Advance(_options.EditorLockAbandonedPlayGrace - TimeSpan.FromSeconds(1));
        await _lock.TickAsync();
        Assert.False(b.IsCompleted);

        _time.Advance(TimeSpan.FromSeconds(2));
        await _lock.TickAsync();
        var granted = Text(await Done(b));
        Assert.StartsWith("acquired", granted);
        Assert.Contains("left on by claude", granted);
    }

    [Fact]
    public async Task Play_left_on_during_a_test_run_keeps_the_queue_waiting()
    {
        var link = AgentPlaying();
        Beat(link, FmpEditorState.Playing, phase: FmpPhases.RunningTests);
        _lock.Release("sid-a");
        var b = Wait("sid-b");

        _time.Advance(_options.EditorLockAbandonedPlayGrace + TimeSpan.FromMinutes(1));
        await _lock.TickAsync();

        Assert.False(b.IsCompleted);
    }

    [Fact]
    public async Task Test_run_in_play_mode_holds_the_queue_and_says_so()
    {
        var link = Link(agentPlay: true);
        Beat(link, FmpEditorState.Playing, agentPlay: false, phase: FmpPhases.RunningTests);
        _options.EditorLockWaitSlice = TimeSpan.FromMilliseconds(100);

        var queued = Text(await Done(Wait("sid-b")));

        Assert.Contains("test run", queued);
        Assert.DoesNotContain("person", queued);
    }

    [Fact]
    public async Task Person_playing_holds_the_queue_even_with_a_new_plugin()
    {
        var link = Link(agentPlay: true);
        Beat(link, FmpEditorState.Playing, agentPlay: false);
        var b = Wait("sid-b");

        _time.Advance(TimeSpan.FromMinutes(5));
        await _lock.TickAsync();

        Assert.False(b.IsCompleted);
        Assert.True(_lock.Snapshot().QueueHeldByPlayMode);
        Assert.Null(_lock.Snapshot().PlayLeftBy);
        Assert.Empty(Stops(link));
    }

    [Fact]
    public async Task Person_playing_does_not_let_a_session_whose_implicit_claim_just_ended_in()
    {
        var link = Link(agentPlay: true);
        await Gate("sid-a");
        _time.Advance(_options.EditorLockImplicitIdle + TimeSpan.FromSeconds(1));
        Beat(link, FmpEditorState.Playing, agentPlay: false);
        _options.EditorLockWaitSlice = TimeSpan.FromMilliseconds(100);

        var queued = (await Done(Gate("sid-a")))!;

        Assert.True(queued.IsError);
        Assert.Contains("play mode", Text(queued));
    }

    [Fact]
    public void Force_release_of_a_holder_in_play_stops_its_play_mode()
    {
        var link = AgentPlaying();

        _lock.ForceRelease();

        Assert.Single(Stops(link));
    }

    [Fact]
    public async Task Closed_session_of_a_holder_in_play_stops_its_play_mode()
    {
        var link = AgentPlaying();

        await _clients.EndAsync("sid-a", _entries["sid-a"]);

        Assert.Single(Stops(link));
    }

    [Fact]
    public void Plugin_without_the_feature_gets_no_stop()
    {
        var link = Link();
        _lock.Acquire("sid-a", "claude", "play test");
        Beat(link, FmpEditorState.Playing);

        _lock.ForceRelease();

        Assert.Empty(Stops(link));
        Assert.Null(_lock.Snapshot().PlayLeftBy);
    }

    [Fact]
    public async Task Play_owner_survives_the_link_dropping_for_a_domain_reload()
    {
        var link = AgentPlaying();
        link.Connected = false;
        Beat(link, FmpEditorState.Reloading, agentPlay: false);
        link.Connected = true;
        Beat(link, FmpEditorState.Playing);
        _lock.Release("sid-a");
        _ = Wait("sid-b");
        await Task.Delay(30);

        Assert.StartsWith("acquired", Text(await Done(Wait("sid-a"))));
    }

    [Fact]
    public async Task Leaving_play_mode_clears_the_owner()
    {
        var link = AgentPlaying();
        _lock.Release("sid-a");
        Beat(link, FmpEditorState.Ready, agentPlay: false);

        Assert.Null(_lock.Snapshot().PlayLeftBy);
        Assert.StartsWith("acquired", Text(await Done(Wait("sid-b"))));
    }

    #endregion

    #region Long claims

    private async Task<CallToolResult> Call(string sessionId, string tool)
    {
        var settings = Options.Create(_options);
        var tools = new ServerTools(_registry, _clients, new EditorHealth(_registry, settings, _time), _lock, settings, NullLogger<ServerTools>.Instance);
        return await tools.CallUnityAsync(new CallToolRequestParams { Name = tool }, sessionId, new EchoBackend(), default);
    }

    private async Task Calls(string sessionId, string tool, TimeSpan every, TimeSpan total)
    {
        for (var passed = TimeSpan.Zero; passed < total; passed += every)
        {
            _time.Advance(every);
            await Call(sessionId, tool);
        }
    }

    [Fact]
    public async Task Holder_past_the_cap_is_told_on_every_tool_result()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "long burst");
        await Calls("sid-a", "script-execute", TimeSpan.FromMinutes(1), EditorLock.Cap - TimeSpan.FromMinutes(1));
        Assert.DoesNotContain(EditorLock.ReleaseName, Text(await Call("sid-a", "script-execute")));

        _time.Advance(TimeSpan.FromMinutes(2));
        var late = await Call("sid-a", "rql-query");

        Assert.StartsWith("ran rql-query", Text(late));
        Assert.Contains(EditorLock.ReleaseName, Text(late));
        Assert.Contains("long burst", Text(late));
        Assert.Equal("rql-query", late.StructuredContent!.Value.GetProperty("tool").GetString());
        Assert.Contains(EditorLock.ReleaseName, late.StructuredContent!.Value.GetProperty("lock_notice").GetString());
        Assert.DoesNotContain(EditorLock.ReleaseName, Text(await Call("sid-b", "rql-query")));
    }

    [Fact]
    public async Task Read_only_calls_do_not_keep_an_idle_claim()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "burst");

        await Calls("sid-a", "rql-query", TimeSpan.FromMinutes(1), _options.EditorLockIdleTimeout + TimeSpan.FromMinutes(1));

        Assert.Null(_lock.Refusal("sid-b", "script-execute"));
        Assert.Contains("already freed", Text(_lock.Release("sid-a")));
    }

    [Fact]
    public async Task Editor_changing_calls_keep_the_claim()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "burst");

        await Calls("sid-a", "script-execute", TimeSpan.FromMinutes(1), _options.EditorLockIdleTimeout + TimeSpan.FromMinutes(1));

        Assert.NotNull(_lock.Refusal("sid-b", "script-execute"));
    }

    [Fact]
    public async Task Read_only_calls_keep_the_claim_in_the_holders_own_play_session()
    {
        AgentPlaying();
        _ = Wait("sid-b");

        await Calls("sid-a", "rql-query", TimeSpan.FromMinutes(1), _options.EditorLockPlayIdleTimeout + TimeSpan.FromMinutes(1));
        await _lock.TickAsync();

        Assert.Equal("sid-a", _lock.Snapshot().SessionId);
    }

    private async Task KeepCallingWhileHeld(string sessionId, TimeSpan every, TimeSpan total)
    {
        for (var passed = TimeSpan.Zero; passed < total && _lock.Snapshot().SessionId == sessionId; passed += every)
        {
            _time.Advance(every);
            await _lock.TickAsync();
            if (_lock.Snapshot().SessionId == sessionId)
                await Call(sessionId, "script-execute");
        }
    }

    [Fact]
    public async Task Implicit_claim_gives_way_after_its_max_age_when_others_wait()
    {
        Link();
        await Call("sid-a", "script-execute");
        var b = Wait("sid-b");

        await KeepCallingWhileHeld("sid-a", TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(6));

        Assert.Equal("sid-b", _lock.Snapshot().SessionId);
        Assert.StartsWith("acquired", Text(await Done(b)));
    }

    [Fact]
    public async Task Implicit_claim_nobody_waits_for_is_not_cut_by_age()
    {
        Link();
        await Call("sid-a", "script-execute");

        await KeepCallingWhileHeld("sid-a", TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(6));

        Assert.Equal("sid-a", _lock.Snapshot().SessionId);
    }

    [Fact]
    public void Notice_goes_only_to_the_holder_of_an_explicit_claim_past_the_cap()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "long burst");
        _ = Wait("sid-b");
        using var call = _lock.Track("sid-a", "script-execute");

        _time.Advance(EditorLock.Cap);
        Assert.Null(_lock.HolderNotice("sid-a"));

        _time.Advance(TimeSpan.FromMinutes(1));
        var notice = _lock.HolderNotice("sid-a");
        Assert.Contains("16m 0s", notice);
        Assert.Contains("\"long burst\"", notice);
        Assert.Contains("1 session(s) are waiting", notice);
        Assert.Contains(EditorLock.ReleaseName, notice);
        Assert.Null(_lock.HolderNotice("sid-b"));
        Assert.Null(_lock.HolderNotice(null));

        _options.EditorLockCapNudge = false;
        Assert.Null(_lock.HolderNotice("sid-a"));
    }

    [Fact]
    public async Task Implicit_claim_past_the_cap_gets_no_notice()
    {
        Link();
        await Call("sid-a", "script-execute");

        await KeepCallingWhileHeld("sid-a", TimeSpan.FromSeconds(20), EditorLock.Cap + TimeSpan.FromMinutes(1));

        Assert.Equal("sid-a", _lock.Snapshot().SessionId);
        Assert.Null(_lock.HolderNotice("sid-a"));
        Assert.DoesNotContain(EditorLock.ReleaseName, Text(await Call("sid-a", "script-execute")));
    }

    [Fact]
    public async Task Acquiring_again_starts_a_new_cap()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first burst");
        await Calls("sid-a", "script-execute", TimeSpan.FromMinutes(1), EditorLock.Cap + TimeSpan.FromMinutes(1));
        Assert.NotNull(_lock.HolderNotice("sid-a"));

        _lock.Acquire("sid-a", "claude", "second burst");

        Assert.Null(_lock.HolderNotice("sid-a"));
    }

    [Fact]
    public async Task Option_restores_read_only_calls_keeping_the_claim()
    {
        Link();
        _options.EditorLockReadOnlyKeepsAlive = true;
        _lock.Acquire("sid-a", "claude", "burst");

        await Calls("sid-a", "rql-query", TimeSpan.FromMinutes(1), _options.EditorLockIdleTimeout + TimeSpan.FromMinutes(1));

        Assert.NotNull(_lock.Refusal("sid-b", "script-execute"));
    }

    [Theory]
    [InlineData(FmpEditorState.Compiling, 0, true)]
    [InlineData(FmpEditorState.Reloading, 0, true)]
    [InlineData(FmpEditorState.Ready, FmpPhases.RunningTests, true)]
    [InlineData(FmpEditorState.Ready, 0, false)]
    public async Task Read_only_calls_keep_the_claim_while_the_editor_compiles_reloads_or_runs_tests(FmpEditorState state, int phase, bool connected)
    {
        var link = Link();
        _lock.Acquire("sid-a", "claude", "compile and test");
        Beat(link, state, agentPlay: false, phase: phase);
        link.Connected = connected;

        await Calls("sid-a", "rql-query", TimeSpan.FromMinutes(1), _options.EditorLockIdleTimeout + TimeSpan.FromMinutes(1));

        Assert.NotNull(_lock.Refusal("sid-b", "script-execute"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Read_only_calls_keep_the_claim_while_assets_import_or_a_build_holds_the_main_thread(bool importing)
    {
        var link = Link();
        _lock.Acquire("sid-a", "claude", "player build");
        link.Heartbeat = new HeartbeatMessage
        {
            UnityInstanceId = "inst",
            Updating = importing,
            LastMainTickUnixMs = importing ? 0 : _time.GetUtcNow().ToUnixTimeMilliseconds(),
        };

        await Calls("sid-a", "rql-query", TimeSpan.FromMinutes(1), _options.EditorLockIdleTimeout + TimeSpan.FromMinutes(1));

        Assert.NotNull(_lock.Refusal("sid-b", "script-execute"));
    }

    [Fact]
    public async Task Read_only_calls_on_a_ticking_editor_do_not_keep_the_claim()
    {
        var link = Link();
        _lock.Acquire("sid-a", "claude", "burst");

        for (var minute = 0; minute <= _options.EditorLockIdleTimeout.TotalMinutes; minute++)
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            link.Heartbeat = new HeartbeatMessage { UnityInstanceId = "inst", LastMainTickUnixMs = _time.GetUtcNow().ToUnixTimeMilliseconds() };
            await Call("sid-a", "rql-query");
        }

        Assert.Null(_lock.Refusal("sid-b", "script-execute"));
    }

    [Fact]
    public async Task Running_read_only_call_keeps_the_claim_until_it_ends()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "burst");

        using (_lock.Track("sid-a", "rql-query"))
        {
            _time.Advance(_options.EditorLockIdleTimeout + TimeSpan.FromMinutes(1));
            await _lock.TickAsync();
            Assert.Equal("sid-a", _lock.Snapshot().SessionId);
        }

        await _lock.TickAsync();
        Assert.False(_lock.Snapshot().Held);
    }

    [Fact]
    public async Task Granted_session_whose_first_call_only_reads_keeps_its_turn()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");
        var b = Wait("sid-b");
        _lock.Release("sid-a");
        await Done(b);

        using (_lock.Track("sid-b", "rql-query"))
            _time.Advance(TimeSpan.FromSeconds(1));
        _time.Advance(_options.EditorLockFirstCallDeadline + TimeSpan.FromSeconds(1));
        await _lock.TickAsync();

        Assert.Equal("codex", _lock.Snapshot().Owner);
    }

    [Fact]
    public async Task Implicit_claim_in_its_own_play_session_is_not_cut_by_age()
    {
        var link = Link(agentPlay: true);
        await Call("sid-a", "script-execute");
        Beat(link, FmpEditorState.Playing);
        _ = Wait("sid-b");

        await KeepCallingWhileHeld("sid-a", TimeSpan.FromSeconds(20), _options.EditorLockImplicitMaxAge + TimeSpan.FromMinutes(1));

        Assert.Equal("sid-a", _lock.Snapshot().SessionId);
        Assert.Empty(Stops(link));
    }

    [Fact]
    public async Task Hard_cap_frees_an_explicit_claim_only_with_waiters_and_only_when_set()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "long burst");
        var b = Wait("sid-b");
        await Calls("sid-a", "script-execute", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(31));
        await _lock.TickAsync();
        Assert.Equal("sid-a", _lock.Snapshot().SessionId);

        _lock.Release("sid-b");
        _options.EditorLockHardCap = TimeSpan.FromMinutes(30);
        await _lock.TickAsync();
        Assert.Equal("sid-a", _lock.Snapshot().SessionId);

        var c = Wait("sid-c");
        await _lock.TickAsync();

        Assert.StartsWith("acquired", Text(await Done(c)));
        Assert.Contains("past the hard cap of 30m 0s", Text(_lock.Release("sid-a")));
        await Done(b);
    }

    private sealed class EchoBackend : IUnityBackend
    {
        public ValueTask<IReadOnlyList<Tool>> ListToolsAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<Tool>>([]);

        public ValueTask<CallToolResult> CallToolAsync(CallToolRequestParams request, CancellationToken ct)
            => ValueTask.FromResult(new CallToolResult
            {
                Content = [new TextContentBlock { Text = "ran " + request.Name }],
                StructuredContent = JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["tool"] = request.Name }),
            });
    }

    #endregion

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }

    private static LockCaller Sub(string sessionId, string agentId, string type = "general-purpose") => new(sessionId, agentId, type);

    private Task<CallToolResult> Wait(LockCaller caller, string? owner, params string[] after)
        => _lock.AcquireAsync(caller, owner, "task of " + (owner ?? caller.Key), true, null, default, after: after);

    private void Hook(string name, string session, string? toolUseId = null, string? tool = null, string? agentId = null, string? agentType = null)
        => _agents.OnHook(JsonSerializer.SerializeToElement(new Dictionary<string, string?>
        {
            ["hook_event_name"] = name, ["session_id"] = session, ["tool_use_id"] = toolUseId, ["tool_name"] = tool,
            ["agent_id"] = agentId, ["agent_type"] = agentType,
        }.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value)));

    // a tool call as Claude Code makes it: the hook first, then the call with the same tool use id
    private LockCaller Call(string mcpSession, string claudeSession, string toolUseId, string? agentId = null, string? agentType = null)
    {
        Hook("PreToolUse", claudeSession, toolUseId, "mcp__Feeder-MCP__script-execute", agentId, agentType);
        return _agents.Resolve(mcpSession, toolUseId);
    }

    [Fact]
    public void Tool_use_id_names_the_subagent_and_an_unknown_one_the_session()
    {
        var main = Call("sid-a", "claude-1", "use-1");
        var sub = Call("sid-a", "claude-1", "use-2", "agent-1", "Explore");

        Assert.Equal(new LockCaller("sid-a"), main);
        Assert.Equal(new LockCaller("sid-a", "agent-1", "Explore"), sub);
        Assert.NotEqual(main.Key, sub.Key);
        Assert.Equal(new LockCaller("sid-a"), _agents.Resolve("sid-a", "never-seen"));
        Assert.Equal(new LockCaller("sid-a"), _agents.Resolve("sid-a", null));
        Assert.True(_agents.Reports("sid-a"));
        Assert.False(_agents.Reports("sid-b"));
    }

    [Fact]
    public async Task Subagent_does_not_share_the_claim_of_its_session()
    {
        Link();
        _lock.Acquire("sid-a", "main", "compile");
        var sub = Sub("sid-a", "agent-1");

        Assert.Null(_lock.Refusal("sid-a", "script-execute"));
        Assert.NotNull(_lock.Refusal(sub, "script-execute"));
        Assert.False(_lock.Status(sub).StructuredContent!.Value.GetProperty("mine").GetBoolean());

        var waiting = Wait(sub, null);
        var state = _lock.Snapshot();
        Assert.False(waiting.IsCompleted);
        Assert.Equal(["general-purpose agent-1"], Order(state));
        Assert.True(state.Waiting[0].Subagent);
        Assert.NotEqual(state.SessionId, state.Waiting[0].SessionId);
        Assert.Equal(1, _lock.Status(sub).StructuredContent!.Value.GetProperty("position").GetInt32());

        _lock.Release("sid-a");
        Assert.StartsWith("acquired", Text(await Done(waiting)));
        Assert.True(_lock.Snapshot().Subagent);
        Assert.NotNull(_lock.Refusal("sid-a", "script-execute"));
        Assert.Null(_lock.Refusal(sub, "script-execute"));
    }

    [Fact]
    public async Task Two_subagents_of_one_session_take_turns()
    {
        Link();
        var first = Sub("sid-a", "agent-1");
        var second = Sub("sid-a", "agent-2");
        Assert.StartsWith("acquired", Text(await Done(Wait(first, "coder"))));

        var waiting = Wait(second, "playtest");
        Assert.False(waiting.IsCompleted);
        Assert.Equal(["playtest"], Order(_lock.Snapshot()));

        _lock.Release(first);
        Assert.StartsWith("acquired", Text(await Done(waiting)));
        Assert.Equal("playtest", _lock.Snapshot().Owner);
    }

    [Fact]
    public async Task Parent_that_waits_for_its_subagent_passes_the_claim_ahead_of_the_queue()
    {
        Link();
        _lock.Acquire(Call("sid-a", "claude-1", "use-1"), "main", "compile");
        var other = Wait("sid-b");
        Hook("PreToolUse", "claude-1", "spawn-1", "Agent");

        var sub = Call("sid-a", "claude-1", "use-2", "agent-1", "playtester");
        var granted = await Done(Wait(sub, null));

        Assert.StartsWith("acquired", Text(granted));
        Assert.Equal("playtester agent-1", _lock.Snapshot().Owner);
        Assert.False(other.IsCompleted);
        Assert.Contains("passed to your subagent playtester agent-1", _lock.HolderNotice("sid-a"));
        Assert.Null(_lock.HolderNotice("sid-a"));

        _lock.Release(sub);
        Assert.StartsWith("acquired", Text(await Done(other)));
    }

    [Fact]
    public async Task Parent_that_does_not_wait_keeps_its_claim()
    {
        Link();
        _lock.Acquire(Call("sid-a", "claude-1", "use-1"), "main", "compile");
        Hook("PreToolUse", "claude-1", "spawn-1", "Agent");
        Hook("PostToolUse", "claude-1", "spawn-1", "Agent");

        var waiting = Wait(Call("sid-a", "claude-1", "use-2", "agent-1", "playtester"), null);
        await _lock.TickAsync();

        Assert.False(waiting.IsCompleted);
        Assert.Equal("main", _lock.Snapshot().Owner);
    }

    [Fact]
    public async Task Subagent_that_ends_frees_its_claim_and_leaves_the_queue()
    {
        Link();
        var holder = Call("sid-a", "claude-1", "use-1", "agent-1", "coder");
        var queued = Call("sid-a", "claude-1", "use-2", "agent-2", "reviewer");
        await Done(Wait(holder, null));
        var gone = Wait(queued, null);
        var next = Wait("sid-b");

        Hook("SubagentStop", "claude-1", agentId: "agent-2", agentType: "reviewer");
        Assert.Equal(["codex"], Order(_lock.Snapshot()));
        Assert.True((await Done(gone)).IsError);

        Hook("SubagentStop", "claude-1", agentId: "agent-1", agentType: "coder");
        Assert.StartsWith("acquired", Text(await Done(next)));
    }

    [Fact]
    public async Task After_waits_for_the_named_agent_to_finish_and_holds_nobody_back()
    {
        Link();
        var coder = Sub("sid-a", "agent-1");
        var playtest = Sub("sid-a", "agent-2");
        _lock.Status(coder, "coder");

        var waiting = Wait(playtest, "playtest", "coder");
        var state = _lock.Snapshot();
        Assert.False(waiting.IsCompleted);
        Assert.False(state.Held);
        Assert.Equal("coder", state.Waiting[0].After);
        Assert.Contains("after coder", Text(_lock.Status("sid-b")));

        // the editor is free for everyone else meanwhile, and one claim of the coder is not the end of its work
        Assert.StartsWith("acquired", Text(await Done(Wait(coder, "coder"))));
        _lock.Release(coder);
        Assert.StartsWith("acquired", Text(_lock.Acquire("sid-b", "other", "unrelated")));
        _lock.Release("sid-b");
        Assert.False(waiting.IsCompleted);

        _lock.Release(coder, done: true);
        Assert.StartsWith("acquired", Text(await Done(waiting)));
        Assert.Equal("playtest", _lock.Snapshot().Owner);
    }

    [Fact]
    public async Task After_takes_a_subagent_type_and_ends_with_the_subagent()
    {
        Link();
        Hook("SubagentStart", "claude-1", agentId: "agent-1", agentType: "coder");

        var waiting = Wait("sid-b", "playtest", "coder");
        await _lock.TickAsync();
        Assert.False(waiting.IsCompleted);

        Hook("SubagentStop", "claude-1", agentId: "agent-1", agentType: "coder");
        await _lock.TickAsync();
        Assert.StartsWith("acquired", Text(await Done(waiting)));
    }

    [Fact]
    public async Task After_a_finished_agent_does_not_wait()
    {
        Link();
        var coder = Sub("sid-a", "agent-1");
        _lock.Acquire(coder, "coder", "compile");
        _lock.Release(coder, done: true);

        Assert.StartsWith("acquired", Text(await Done(Wait("sid-b", "playtest", "coder"))));
    }

    [Fact]
    public async Task After_a_name_nobody_uses_fails_when_the_grace_ends()
    {
        Link();
        _lock.Status("sid-a", "coder");
        var waiting = Wait("sid-b", "playtest", "ghost");

        _time.Advance(_options.EditorLockAfterGrace - TimeSpan.FromSeconds(1));
        await _lock.TickAsync();
        Assert.False(waiting.IsCompleted);

        _time.Advance(TimeSpan.FromSeconds(2));
        await _lock.TickAsync();
        var failed = await Done(waiting);
        Assert.True(failed.IsError);
        Assert.Contains("Nobody named 'ghost'", Text(failed));
        Assert.Contains("Known now: coder", Text(failed));
        Assert.Empty(_lock.Snapshot().Waiting);
    }

    [Fact]
    public async Task After_refuses_yourself_and_a_circle()
    {
        Link();
        _lock.Status("sid-a", "coder");
        _lock.Status("sid-b", "playtest");

        Assert.Contains("names yourself", Text(await Done(Wait("sid-a", "coder", "coder"))));

        var first = Wait("sid-a", "coder", "playtest");
        var circle = await Done(Wait("sid-b", "playtest", "coder"));
        Assert.True(circle.IsError);
        Assert.Contains("already waits for you", Text(circle));
        Assert.False(first.IsCompleted);
    }

    [Fact]
    public async Task Person_lets_a_waiter_skip_after()
    {
        Link();
        _lock.Status("sid-a", "coder");
        var waiting = Wait("sid-b", "playtest", "coder");
        var ticket = _lock.Snapshot().Waiting[0].Ticket;

        Assert.Equal("playtest", _lock.SkipAfter(ticket));
        Assert.StartsWith("acquired", Text(await Done(waiting)));
        Assert.Equal("nothing", _lock.SkipAfter("no-such-ticket"));
    }

    [Fact]
    public async Task Closed_session_counts_as_finished_for_after()
    {
        Link();
        _lock.Status("sid-a", "coder");
        var waiting = Wait("sid-b", "playtest", "coder");

        await _clients.EndAsync("sid-a", _entries["sid-a"]);
        Assert.StartsWith("acquired", Text(await Done(waiting)));
    }

    [Fact]
    public async Task Second_call_of_a_waiting_agent_waits_with_the_first()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");
        var first = Wait("sid-b");
        var second = Wait("sid-b");

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Assert.Single(_lock.Snapshot().Waiting);

        _lock.Release("sid-a");
        Assert.StartsWith("acquired", Text(await Done(first)));
        Assert.StartsWith("acquired", Text(await Done(second)));
    }

    [Fact]
    public async Task One_of_two_waiting_calls_cancelled_keeps_the_place()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");
        using var cancel = new CancellationTokenSource();
        var cancelled = Wait("sid-b", ct: cancel.Token);
        var kept = Wait("sid-b");

        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Done(cancelled));
        Assert.True(_lock.Snapshot().Waiting[0].Attached);

        _lock.Release("sid-a");
        Assert.StartsWith("acquired", Text(await Done(kept)));
    }

    [Fact]
    public async Task Agent_whose_place_lapsed_is_told_when_it_joins_again()
    {
        Link();
        _lock.Acquire("sid-a", "claude", "first");
        _options.EditorLockWaitSlice = TimeSpan.FromMilliseconds(100);
        var ticket = Ticket(await Done(Wait("sid-b")));

        _time.Advance(_options.EditorLockWaitSlice + _options.EditorLockTicketGrace + TimeSpan.FromSeconds(1));
        await _lock.TickAsync();
        Assert.Empty(_lock.Snapshot().Waiting);

        Assert.Contains("Your earlier place lapsed", Text(await Done(Wait("sid-b", ticket))));
        Assert.DoesNotContain("lapsed", Text(await Done(Wait("sid-b", ticket))));
    }

    [Fact]
    public async Task Client_that_reconnects_keeps_its_claim_and_its_place()
    {
        Link();
        Open("sid-a2", "claude-code");
        _lock.Acquire(Call("sid-a", "claude-1", "use-1"), "main", "compile");
        var sub = Wait(Call("sid-a", "claude-1", "use-2", "agent-1", "playtester"), null);

        // the same client, on a new MCP session
        var again = Call("sid-a2", "claude-1", "use-3");

        Assert.Null(_lock.Refusal(again, "script-execute"));
        Assert.True(_lock.Status(again).StructuredContent!.Value.GetProperty("mine").GetBoolean());
        Assert.StartsWith("released", Text(_lock.Release(again)));
        Assert.StartsWith("acquired", Text(await Done(sub)));
        Assert.Null(_lock.Refusal(Call("sid-a2", "claude-1", "use-4", "agent-1", "playtester"), "script-execute"));
    }

    private sealed class LockHub : IHubContext<FmpHub, IFmpUnityClient>
    {
        private readonly Targets _targets;

        public LockHub() => _targets = new Targets(this);

        public Dictionary<string, List<EditorLockState>> Sent { get; } = [];
        public Dictionary<string, List<string>> Stops { get; } = [];

        public IHubClients<IFmpUnityClient> Clients => _targets;
        public IGroupManager Groups => throw new NotSupportedException();

        private sealed class Targets(LockHub hub) : IHubClients<IFmpUnityClient>
        {
            public IFmpUnityClient All => new Recorder(hub, "*");
            public IFmpUnityClient Client(string connectionId) => new Recorder(hub, connectionId);
            public IFmpUnityClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IFmpUnityClient Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
            public IFmpUnityClient Group(string groupName) => throw new NotSupportedException();
            public IFmpUnityClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IFmpUnityClient Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
            public IFmpUnityClient User(string userId) => throw new NotSupportedException();
            public IFmpUnityClient Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
        }

        private sealed class Recorder(LockHub hub, string connectionId) : IFmpUnityClient
        {
            public Task InvokeTool(ToolInvoke invoke) => throw new NotSupportedException();
            public Task CancelTool(string operationId) => throw new NotSupportedException();
            public Task McpClientsChanged(McpClientInfo[] clients) => Task.CompletedTask;

            public Task EditorLockChanged(EditorLockState state)
            {
                if (!hub.Sent.TryGetValue(connectionId, out var list))
                    hub.Sent[connectionId] = list = [];
                list.Add(state);
                return Task.CompletedTask;
            }

            public Task StopAgentPlay(string reason)
            {
                if (!hub.Stops.TryGetValue(connectionId, out var list))
                    hub.Stops[connectionId] = list = [];
                list.Add(reason);
                return Task.CompletedTask;
            }
        }
    }
}
