using System.Text.Json;
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
    private readonly MatrixOptions _options = new();
    private readonly EditorLock _lock;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "matrix-lock-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, McpClientTracker.Entry> _entries = [];

    public EditorLockTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Temp"));
        _clients = new McpClientTracker(_hub, _time, NullLogger<McpClientTracker>.Instance);
        _lock = new EditorLock(_registry, _clients, _hub, _time, Options.Create(_options), NullLogger<EditorLock>.Instance);
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

    private UnityLink Link(FmpEditorState state = FmpEditorState.Ready, bool showsLock = true)
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
            Features = showsLock ? [FmpCapabilities.EditorLock] : [],
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

        using (_lock.Track("sid-a"))
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

    private static Task<CallToolResult> Done(Task<CallToolResult> call) => call.WaitAsync(TimeSpan.FromSeconds(5));

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
        using (_lock.Track("sid-b"))
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
        using (_lock.Track("sid-b"))
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

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }

    private sealed class LockHub : IHubContext<FmpHub, IFmpUnityClient>
    {
        private readonly Targets _targets;

        public LockHub() => _targets = new Targets(this);

        public Dictionary<string, List<EditorLockState>> Sent { get; } = [];

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
        }
    }
}
