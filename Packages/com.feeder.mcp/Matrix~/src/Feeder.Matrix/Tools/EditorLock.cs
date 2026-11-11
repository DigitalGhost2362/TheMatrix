using System.Text.Encodings.Web;
using System.Text.Json;
using Feeder.Matrix.Health;
using Feeder.Matrix.Protocol;
using Feeder.Matrix.Unity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace Feeder.Matrix.Tools;

/// <summary>
/// One claim on the Unity editor at a time, held by an agent: an MCP session, or one subagent inside it when the
/// client reports its subagents (<see cref="AgentDirectory"/>).
/// While someone holds it, every other agent gets its editor-changing tools refused; read-only tools always run.
/// The claim ends on release, when the holder's MCP session closes, or when an agent stays idle past
/// <see cref="MatrixOptions.EditorLockIdleTimeout"/>. It is mirrored to <c>Temp/agent-editor-lock/lock.json</c> so
/// sessions that still claim with the lock-file scripts see it, and a live lock file written by such a session
/// keeps the claim from being taken.
/// </summary>
public sealed class EditorLock
{
    public const string AcquireName = "editor-lock-acquire";
    public const string ReleaseName = "editor-lock-release";
    public const string StatusName = "editor-lock-status";

    public const string KindAgent = "agent";
    public const string KindOutside = "outside";

    /// <summary>Longest a claim should last; past it the editor and other sessions are told, but nothing is freed.</summary>
    public static readonly TimeSpan Cap = TimeSpan.FromMinutes(15);

    // what the lock-file scripts treat as stale
    static readonly TimeSpan FileTtl = TimeSpan.FromMinutes(20);
    static readonly TimeSpan MirrorBeat = TimeSpan.FromMinutes(1);
    static readonly TimeSpan NoticeLogBeat = TimeSpan.FromMinutes(1);
    const int MaxTextLength = 120;

    // not marked read-only, yet they change nothing another session's play test depends on
    static readonly HashSet<string> Exempt = new(StringComparer.OrdinalIgnoreCase)
    {
        "tool-set-enabled-state",
        "unity-skill-generate",
    };

    static readonly JsonSerializerOptions FileJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    readonly UnityLinkRegistry _registry;
    readonly McpClientTracker _clients;
    readonly AgentDirectory _agents;
    readonly IHubContext<FmpHub, IFmpUnityClient> _hub;
    readonly TimeProvider _time;
    readonly IOptions<MatrixOptions> _options;
    readonly ILogger<EditorLock> _logger;

    readonly object _gate = new();
    Holder? _holder;
    FileLock? _outside;
    (string Key, string Reason, bool Implicit, string Owner, DateTimeOffset At)? _lastDropped;
    // the agent whose tool started the current play mode; kept across the domain reload of entering play
    (string Key, string Owner)? _playOwner;
    // when the play owner stopped holding the lock
    DateTimeOffset _playLeftAt;
    (string ConnectionId, string Reason)? _stopPlay;
    EditorLockState _published = new();
    readonly List<Waiter> _queue = [];
    readonly Dictionary<string, DateTimeOffset> _removed = [];
    // agents whose place in the queue lapsed between two calls
    readonly Dictionary<string, DateTimeOffset> _lapsed = [];
    // main agents whose claim passed to a subagent they waited for, and to whom; told once
    readonly Dictionary<string, string> _handed = [];
    // the names agents go by, for 'after'
    readonly List<Named> _named = [];
    bool _paused;

    public EditorLock(
        UnityLinkRegistry registry,
        McpClientTracker clients,
        AgentDirectory agents,
        IHubContext<FmpHub, IFmpUnityClient> hub,
        TimeProvider time,
        IOptions<MatrixOptions> options,
        ILogger<EditorLock> logger)
    {
        _registry = registry;
        _clients = clients;
        _agents = agents;
        _hub = hub;
        _time = time;
        _options = options;
        _logger = logger;
        clients.SessionEnded += OnSessionEnded;
        agents.AgentEnded += OnAgentEnded;
        agents.SessionReplaced += OnSessionReplaced;
    }

    sealed class Named
    {
        public required string Label { get; init; }
        public required string Key { get; set; }
        public required string SessionId { get; set; }
        public DateTimeOffset? Done { get; set; }
    }

    sealed class Holder
    {
        public required LockCaller Caller { get; set; }
        public string Key => Caller.Key!;
        public string SessionId => Caller.SessionId!;
        public required string Owner { get; set; }
        public required string Task { get; set; }
        public string ClientName { get; init; } = string.Empty;
        public string ClientVersion { get; init; } = string.Empty;
        public string? Root { get; init; }
        public DateTimeOffset Acquired { get; set; }
        public DateTimeOffset LastActivity { get; set; }
        public DateTimeOffset MirrorWritten { get; set; }
        public DateTimeOffset NoticeLogged { get; set; }
        public int InFlight { get; set; }
        public DateTimeOffset GrantedAt { get; init; }
        public bool AwaitingFirstCall { get; set; }
        public bool Implicit { get; set; }
    }

    sealed class Waiter
    {
        public required string Ticket { get; init; }
        public required LockCaller Caller { get; set; }
        public string Key => Caller.Key!;
        public string SessionId => Caller.SessionId!;
        public required string Owner { get; set; }
        public required string Task { get; set; }
        public string ClientName { get; init; } = string.Empty;
        // names of the agents that must finish first
        public string[] After { get; set; } = [];
        public string? Failed { get; set; }
        public DateTimeOffset Enqueued { get; init; }
        public DateTimeOffset LastCall { get; set; }
        public TaskCompletionSource? Pending { get; set; }
        // calls of this agent that wait on Pending now
        public int Calls { get; set; }
        // its earlier place lapsed and it joined again; said once
        public bool Rejoined { get; set; }
        public bool Granted { get; set; }
        public bool Removed { get; set; }
        public bool Implicit { get; set; }
        public bool PlayOk { get; init; }
    }

    sealed record FileLock(string Owner, string Task, string Acquired, long Heartbeat, bool Matrix, string Session, DateTimeOffset Created);

    public static IReadOnlyList<string> ToolNames { get; } = [AcquireName, ReleaseName, StatusName];

    public static bool IsLockTool(string? name) => name is AcquireName or ReleaseName or StatusName;

    public static IEnumerable<Tool> Tools()
    {
        yield return new Tool
        {
            Name = AcquireName,
            Title = "Editor / Lock / Acquire",
            Description = "Claims the shared Unity editor for one burst of editor-changing work (a compile, one play session, an " +
                          "edit-mode batch). While you hold it, other sessions' editor-changing tools are refused; read-only tools " +
                          "still run for everyone. When it is taken the call waits in a first-come queue (about 50 s per call) and " +
                          "answers 'acquired' the moment it is your turn; on 'queued' call it again at once with the ticket to keep " +
                          $"your place. Release with {ReleaseName} as soon as the burst ends, because others may be waiting; a claim " +
                          "is freed on its own when your session closes or after a long idle stretch. Calling it again while you hold " +
                          "it starts a new claim. A subagent claims for itself: it does not share its parent's claim. With 'after' " +
                          "you join the queue now and get the editor only once the named agents have finished (a play test after the " +
                          "agent that edits and compiles).",
            InputSchema = Schema("""
                {"type":"object","properties":{
                  "task":{"type":"string","description":"What the claim is for, in a few words. Shown in the Unity editor."},
                  "owner":{"type":"string","description":"A label people recognize you by, e.g. 'claude-catwalk-score'. Defaults to your client name."},
                  "wait":{"type":"boolean","description":"Wait in the queue when the editor is taken. Default true; false answers 'busy' at once."},
                  "ticket":{"type":"string","description":"The ticket of an earlier 'queued' answer: keeps your place in the queue."},
                  "after":{"type":"array","items":{"type":"string"},"description":"Owner labels (or subagent types) of agents that must finish before you get the editor."}},
                 "required":["task"],"additionalProperties":false}
                """),
        };
        yield return new Tool
        {
            Name = ReleaseName,
            Title = "Editor / Lock / Release",
            Description = "Gives the shared Unity editor back after your burst: exit play mode and restore what you changed first. " +
                          "Only releases your own claim.",
            InputSchema = Schema("""
                {"type":"object","properties":{
                  "done":{"type":"boolean","description":"True when all your editor work is finished: agents that wait for you with 'after' may go. A subagent that ends is done on its own."}},
                 "additionalProperties":false}
                """),
            Annotations = new ToolAnnotations { IdempotentHint = true },
        };
        yield return new Tool
        {
            Name = StatusName,
            Title = "Editor / Lock / Status",
            Description = "Says who holds the shared Unity editor (an agent or a lock file), " +
                          "for what, since when, whether it is you, and who waits in the queue (with your position).",
            InputSchema = Schema("""
                {"type":"object","properties":{
                  "owner":{"type":"string","description":"Makes your label known before your first claim, so others can wait for you with 'after'. Say it when you start work that ends in a compile."}},
                 "additionalProperties":false}
                """),
            Annotations = new ToolAnnotations { ReadOnlyHint = true, IdempotentHint = true },
        };
    }

    #region MCP tools

    public Task<CallToolResult> CallAsync(CallToolRequestParams request, LockCaller caller, CancellationToken ct) => request.Name switch
    {
        AcquireName => AcquireAsync(caller, Text(request.Arguments, "owner"), Text(request.Arguments, "task"),
            Flag(request.Arguments, "wait") ?? true, Text(request.Arguments, "ticket"), ct, after: Texts(request.Arguments, "after")),
        ReleaseName => Task.FromResult(Release(caller, Flag(request.Arguments, "done") ?? false)),
        _ => Task.FromResult(Status(caller, Text(request.Arguments, "owner"))),
    };

    /// <summary>Claims the editor now or answers busy; never queues.</summary>
    public CallToolResult Acquire(LockCaller caller, string? owner, string? task)
    {
        var result = Enter(caller, owner, task, wait: false, ticket: null, out _, out _);
        _ = PublishAsync();
        return result!;
    }

    /// <summary>Claims the editor, or waits in the queue for at most one slice and answers queued.</summary>
    public async Task<CallToolResult> AcquireAsync(LockCaller caller, string? owner, string? task, bool wait, string? ticket, CancellationToken ct, string? implicitTool = null, string[]? after = null)
    {
        var result = Enter(caller, owner, task, wait, ticket, out var waiter, out var signal, implicitTool, after);
        _ = PublishAsync();
        if (result is not null)
            return result;
        try
        {
            await signal!.WaitAsync(_options.Value.EditorLockWaitSlice, _time, ct);
        }
        catch (TimeoutException)
        {
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                if (--waiter!.Calls <= 0)
                    Drop(waiter);
            }
            _ = PublishAsync();
            throw;
        }
        lock (_gate)
            result = Settle(waiter!, signal!, _time.GetUtcNow());
        _ = PublishAsync();
        return result;
    }

    // null: the caller is queued and must wait on signal
    CallToolResult? Enter(LockCaller caller, string? owner, string? task, bool wait, string? ticket, out Waiter? waiter, out Task? signal, string? implicitTool = null, string[]? after = null)
    {
        waiter = null;
        signal = null;
        if (caller.Key is not { } key)
            return Error("This connection has no MCP session id, so it cannot hold the editor lock.");
        task = Clip(task);
        if (task.Length == 0)
            return Error($"{AcquireName} needs 'task': what the claim is for, in a few words.");

        lock (_gate)
        {
            var now = _time.GetUtcNow();
            Maintain(now);
            var link = _registry.GetLatestLink();
            var client = _clients.ClientOf(caller.SessionId);
            owner = Clip(owner);
            if (owner.Length > 0)
                Name(caller, owner);
            else
                owner = DefaultOwner(caller, client);

            if (_holder is { } held && held.Key == key)
            {
                _queue.RemoveAll(w => w.Key == key);
                if (implicitTool is not null)
                {
                    held.LastActivity = now;
                    held.AwaitingFirstCall = false;
                    return Acquired($"acquired (you already hold it). {EditorLine(link)}", held.Task, held.Owner, link, TimeSpan.Zero);
                }
                held.Implicit = false;
                held.Owner = owner;
                held.Task = task;
                held.Acquired = now;
                held.LastActivity = now;
                held.AwaitingFirstCall = false;
                WriteMirror(held, now);
                return Acquired($"acquired (a new claim; you already held it) for \"{task}\". {EditorLine(link)} {Reminder}", task, owner, link, TimeSpan.Zero);
            }
            if (ticket is not null && _removed.Remove(ticket))
                return Removed();

            var waitsFor = Labels(after);
            if (AfterProblem(key, waitsFor) is { } problem)
                return Error(problem);

            waiter = _queue.Find(w => w.Key == key);
            if (waiter is null)
            {
                var playOk = implicitTool is not null && _lastDropped is { Implicit: true } dropped && dropped.Key == key;
                // only the agent that left play mode on can clean it up, so it goes ahead of the queue
                var playOwner = PlayLeftOn(link)?.Key == key;
                // sessions that wait for another agent to finish hold nobody back
                if (Unfinished(waitsFor).Count == 0 && (!_queue.Exists(w => Unfinished(w.After).Count == 0) || playOwner) && Blocker(link, now, key, playOk) is null)
                {
                    _lapsed.Remove(key);
                    Grant(caller, owner, task, link, now, fromQueue: false, implicitTool is not null);
                    return Acquired($"acquired for \"{task}\". {EditorLine(link)} {Reminder}", task, owner, link, TimeSpan.Zero);
                }
                if (!wait)
                    return Busy(AfterNote(waitsFor) ?? Blocker(link, now));
                waiter = new Waiter
                {
                    Implicit = implicitTool is not null,
                    PlayOk = playOk,
                    Ticket = Guid.NewGuid().ToString("N")[..12],
                    Caller = caller,
                    Owner = owner,
                    Task = task,
                    ClientName = client?.Name ?? string.Empty,
                    After = waitsFor,
                    Rejoined = _lapsed.Remove(key),
                    Enqueued = now,
                };
                _queue.Add(waiter);
            }
            else if (implicitTool is null)
            {
                waiter.Owner = owner;
                waiter.Task = task;
                waiter.Implicit = false;
                if (after is not null)
                    waiter.After = waitsFor;
            }
            waiter.LastCall = now;
            if (!wait)
                return Queued(waiter, now, link);

            // a second call of the same agent waits with the first instead of ending it
            waiter.Pending ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            waiter.Calls++;
            signal = waiter.Pending.Task;
            Pump(now);
            return waiter.Granted ? Settle(waiter, signal, now) : null;
        }
    }

    // caller holds _gate; the answer of a waiting call that was granted, removed, dropped or timed out
    CallToolResult Settle(Waiter waiter, Task signal, DateTimeOffset now)
    {
        if (--waiter.Calls <= 0 && waiter.Pending?.Task == signal)
            waiter.Pending = null;
        var link = _registry.GetLatestLink();
        if (waiter.Granted)
        {
            if (_holder is { } held && held.Key == waiter.Key)
                return Acquired($"acquired for \"{held.Task}\" after waiting {Age(now - waiter.Enqueued)}. {EditorLine(link)} {Reminder} " +
                                $"Make a tool call within {Age(_options.Value.EditorLockFirstCallDeadline)} or the turn passes on.",
                    held.Task, held.Owner, link, now - waiter.Enqueued);
            return Error($"You were granted the lock but it was freed again before your call returned. Call {AcquireName} again.");
        }
        if (waiter.Removed)
            return Removed();
        if (waiter.Failed is { } why)
            return Error(why);
        if (_queue.Contains(waiter))
            return Queued(waiter, now, link);
        return Error($"Your place in the editor lock queue is gone (its session closed or the ticket expired). Call {AcquireName} again to join the end.");
    }

    static CallToolResult Removed()
        => ToolErrors.Create(ToolErrors.EditorLocked,
            "removed: a person took you out of the editor lock queue in the Unity editor. Do not queue again unless the user asks; " +
            $"call {AcquireName} without the ticket to join the end of the queue.");

    CallToolResult Queued(Waiter waiter, DateTimeOffset now, UnityLink? link)
    {
        var position = _queue.IndexOf(waiter) + 1;
        var blocker = AfterNote(waiter.After) ?? Blocker(link, now) ?? "The editor is free and your turn comes next.";
        var rejoined = waiter.Rejoined
            ? $"Your earlier place lapsed (no call within {Age(_options.Value.EditorLockTicketGrace)} of a queued answer), so you joined the end. "
            : string.Empty;
        waiter.Rejoined = false;
        var result = ToolErrors.Create(ToolErrors.LockQueued,
            $"queued: position {position} ({position - 1} ahead). {rejoined}{char.ToUpperInvariant(blocker[0])}{blocker[1..]} " +
            (waiter.Implicit
                ? "The tool did not run. Call the same tool again at once to keep your place."
                : $"Call {AcquireName} again at once with ticket \"{waiter.Ticket}\" to keep your place."),
            retryAfterMs: 0,
            new Dictionary<string, object?>
            {
                ["position"] = position,
                ["ahead"] = position - 1,
                ["holder"] = _holder?.Owner ?? _outside?.Owner,
                ["ticket"] = waiter.Ticket,
                ["waited_ms"] = (long)(now - waiter.Enqueued).TotalMilliseconds,
            });
        result.IsError = false;
        return result;
    }

    CallToolResult Acquired(string text, string task, string owner, UnityLink? link, TimeSpan waited)
        => new()
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["acquired"] = true,
                ["task"] = task,
                ["owner"] = owner,
                ["waited_ms"] = (long)waited.TotalMilliseconds,
                ["editor"] = EditorLine(link),
            }),
        };

    CallToolResult Busy(string? blocker)
        => ToolErrors.Create(ToolErrors.EditorLocked,
            $"busy: {blocker ?? $"{_queue.Count} session(s) are waiting ahead of you."} Call {AcquireName} with wait (the default) to queue, or hand off (write a test case and stop).",
            retryAfterMs: 5000,
            new Dictionary<string, object?> { ["holder"] = _holder?.Owner ?? _outside?.Owner, ["queue_length"] = _queue.Count });

    // caller holds _gate
    void Grant(LockCaller caller, string owner, string task, UnityLink? link, DateTimeOffset now, bool fromQueue, bool isImplicit = false)
    {
        var client = _clients.ClientOf(caller.SessionId);
        _holder = new Holder
        {
            Caller = caller,
            Owner = owner,
            Task = task,
            ClientName = client?.Name ?? string.Empty,
            ClientVersion = client?.Version ?? string.Empty,
            Root = link?.ProjectRoot,
            Acquired = now,
            LastActivity = now,
            GrantedAt = now,
            AwaitingFirstCall = fromQueue,
            Implicit = isImplicit,
        };
        _outside = null;
        if (_lastDropped?.Key == caller.Key)
            _lastDropped = null;
        WriteMirror(_holder, now);
        _logger.LogInformation("Editor lock acquired by {Owner} (session {SessionId}) for {Task}", owner, caller.Key, task);
    }

    // caller holds _gate; why nobody can be granted the lock now, or null
    string? Blocker(UnityLink? link, DateTimeOffset now, string? key = null, bool playOk = false)
    {
        if (_holder is { } other)
            return Describe(other, now);
        if (link?.ProjectRoot is { } root && ReadLiveOutsideLock(root, now) is { } outside)
        {
            _outside = outside;
            return DescribeOutside(outside, now);
        }
        if (_paused)
            return "a person paused the editor lock queue in the Unity editor; nobody is granted the lock until they resume it.";
        if (link is not { Connected: true, EditorState: FmpEditorState.Playing or FmpEditorState.Paused })
            return null;
        if (PlayLeftOn(link) is { } left)
        {
            if (left.Key == key)
                return null;
            if (TestsRunning(link))
                return $"the editor is in play mode {left.Owner} left on while tests run; the queue waits until the test run ends.";
            var back = _playLeftAt + _options.Value.EditorLockAbandonedPlayGrace - now;
            return back > TimeSpan.Zero
                ? $"the editor is in play mode {left.Owner} (an agent) started and left on; it has {Age(back)} to come back and exit it, then the next in line gets the editor."
                : null;
        }
        if (TestsRunning(link))
            return "the editor is in play mode for a test run; the queue waits until the run ends.";
        if (link.Features.Contains(FmpCapabilities.AgentPlay))
            return "the editor is in play mode a person started (no agent tool started it), so a person is playing; do not stop it. The queue waits until play mode ends.";
        if (!playOk)
            return "the editor is in play mode and nobody holds the lock, so a person is playing (or a session left play mode on); do not stop it. The queue waits until play mode ends.";
        return null;
    }

    // play mode an agent's tool started, on a plugin that reports it
    static bool AgentPlaying(UnityLink? link)
        => link is { Connected: true, EditorState: FmpEditorState.Playing or FmpEditorState.Paused, Heartbeat.AgentPlay: true }
           && link.Features.Contains(FmpCapabilities.AgentPlay);

    static bool TestsRunning(UnityLink? link) => ((link?.Heartbeat?.Phase ?? 0) & FmpPhases.RunningTests) != 0;

    // caller holds _gate; the agent that started play mode when nobody holds the lock now
    (string Key, string Owner)? PlayLeftOn(UnityLink? link) => _holder is null && AgentPlaying(link) ? _playOwner : null;

    // caller holds _gate; hands a free lock to the first queued agent that has a call waiting and waits for nobody
    bool Pump(DateTimeOffset now)
    {
        if (_queue.Count == 0)
            return false;
        var next = PassToSubagent();
        var link = _registry.GetLatestLink();
        var owner = PlayLeftOn(link)?.Key;
        next ??= (owner is null ? null : _queue.Find(w => w.Pending is not null && w.Key == owner))
                 ?? _queue.Find(w => w.Pending is not null && Unfinished(w.After).Count == 0);
        if (next is null || Blocker(link, now, next.Key, next.PlayOk) is not null)
            return false;
        _queue.Remove(next);
        next.Granted = true;
        Grant(next.Caller, next.Owner, next.Task, link, now, fromQueue: true, next.Implicit);
        next.Pending!.TrySetResult();
        return true;
    }

    // caller holds _gate; a main agent that waits for a subagent it spawned cannot use the editor, and the subagent
    // would wait for its own parent: the claim passes to the subagent, ahead of the queue
    Waiter? PassToSubagent()
    {
        if (_paused || _holder is not { Caller.IsSubagent: false, InFlight: 0 } parent || !_agents.MainAgentWaits(parent.SessionId))
            return null;
        var child = _queue.Find(w => w.Pending is not null && w.Caller.IsSubagent && w.SessionId == parent.SessionId && Unfinished(w.After).Count == 0);
        if (child is null)
            return null;
        _handed[parent.Key] = child.Owner;
        if (_playOwner?.Key == parent.Key)
            _playOwner = (child.Key, child.Owner);
        Free($"passed to its subagent {child.Owner}", pump: false);
        return child;
    }

    // caller holds _gate; drops queued sessions that closed or stayed away past their ticket
    bool Sweep(DateTimeOffset now)
    {
        var changed = false;
        var away = _options.Value.EditorLockWaitSlice + _options.Value.EditorLockTicketGrace;
        foreach (var waiter in _queue.ToArray())
        {
            if (!_clients.IsOpen(waiter.SessionId))
            {
                Drop(waiter);
                changed = true;
            }
            else if (waiter.Pending is null && now - waiter.LastCall > away)
            {
                _lapsed[waiter.Key] = now;
                Drop(waiter);
                changed = true;
            }
            else if (now - waiter.Enqueued > _options.Value.EditorLockAfterGrace && Unfinished(waiter.After).Where(label => !IsKnown(label)).ToArray() is { Length: > 0 } unknown)
            {
                waiter.Failed = $"Nobody named {string.Join(", ", unknown.Select(u => $"'{u}'"))} showed up within {Age(_options.Value.EditorLockAfterGrace)}, so 'after' cannot be met. " +
                                $"{KnownNames()} Call {AcquireName} again with a known name, or without 'after'.";
                Drop(waiter);
                changed = true;
            }
        }
        foreach (var ticket in _removed.Where(r => now - r.Value > TimeSpan.FromMinutes(10)).Select(r => r.Key).ToArray())
            _removed.Remove(ticket);
        foreach (var key in _lapsed.Where(l => now - l.Value > TimeSpan.FromMinutes(10)).Select(l => l.Key).ToArray())
            _lapsed.Remove(key);
        _named.RemoveAll(n => n.Done is { } done && now - done > NameKept);
        return changed;
    }

    // caller holds _gate; true when something changed
    bool Maintain(DateTimeOffset now)
    {
        var swept = Sweep(now);
        var freed = ExpireIfIdle(now);
        var granted = Pump(now);
        return freed || swept || granted;
    }

    // caller holds _gate
    void Drop(Waiter waiter)
    {
        _queue.Remove(waiter);
        waiter.Pending?.TrySetResult();
    }

    public CallToolResult Release(LockCaller caller, bool done = false)
    {
        CallToolResult result;
        var key = caller.Key;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            Maintain(now);
            if (done && key is not null)
                Finish(n => n.Key == key, now);
            if (_holder is { } held && held.Key == key)
            {
                Free($"released by {held.Owner}");
                result = Ok($"released after {Age(now - held.Acquired)}." + (PlayLeftOn(_registry.GetLatestLink())?.Key == key
                    ? $" Play mode you started is still on: acquire again within {Age(_options.Value.EditorLockAbandonedPlayGrace)} (you go first) and exit it, or the next in line gets the editor with it."
                    : string.Empty));
            }
            else if (_queue.Find(w => w.Key == key) is { } waiting)
            {
                Drop(waiting);
                result = Ok("you left the editor lock queue.");
            }
            else if (key is not null && _handed.Remove(key, out var child))
            {
                result = Ok($"nothing to release: your claim passed to your subagent {child} while you waited for it. Acquire again before more editor work.");
            }
            else if (_lastDropped is { } dropped && dropped.Key == key)
            {
                _lastDropped = null;
                result = Ok($"nothing to release: your claim was already freed ({dropped.Reason}). Acquire again before more editor work.");
            }
            else if (_holder is { } other)
            {
                result = Ok($"nothing to release: you do not hold the lock. {Describe(other, now)}");
            }
            else
            {
                result = Ok("nothing to release: nobody holds the lock.");
            }
            if (done)
                Pump(now);
        }
        _ = PublishAsync();
        return result;
    }

    public CallToolResult Status(LockCaller caller, string? owner = null)
    {
        var key = caller.Key;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            Maintain(now);
            if (key is not null && Clip(owner) is { Length: > 0 } label)
                Name(caller, label);
            var link = _registry.GetLatestLink();
            var position = _queue.FindIndex(w => w.Key == key) + 1;
            string text;
            if (_holder is { } held)
            {
                var mine = held.Key == key;
                if (mine)
                    held.AwaitingFirstCall = false;
                text = (mine ? $"you hold it: \"{held.Task}\" since {Clock(held.Acquired)} ({Age(now - held.Acquired)}). " : Describe(held, now) + " ") + EditorLine(link);
            }
            else if (link?.ProjectRoot is { } root && ReadLiveOutsideLock(root, now) is { } outside)
                text = DescribeOutside(outside, now) + " " + EditorLine(link);
            else if (_lastDropped is { } dropped && dropped.Key == key)
                text = $"free. Your last claim was freed ({dropped.Reason}). " + EditorLine(link);
            else
                text = "free. " + EditorLine(link);

            if (_queue.Count > 0)
            {
                text += $" Queue ({(_paused ? "paused by a person, " : string.Empty)}{_queue.Count} waiting): " +
                        string.Join("; ", _queue.Select((w, i) => $"{i + 1}. {w.Owner} for \"{w.Task}\" ({Age(now - w.Enqueued)}{(Unfinished(w.After) is { Count: > 0 } waits ? ", after " + string.Join(", ", waits) : string.Empty)})"));
                if (position > 0)
                    text += $". You are number {position}, ticket \"{_queue[position - 1].Ticket}\".";
            }
            var result = Ok(text);
            result.StructuredContent = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["held"] = _holder is not null || _outside is not null,
                ["mine"] = _holder is { } h && h.Key == key,
                ["holder"] = _holder?.Owner ?? _outside?.Owner,
                ["queue_paused"] = _paused,
                ["position"] = position,
                ["waiting"] = _queue.Select((w, i) => new Dictionary<string, object?>
                {
                    ["position"] = i + 1,
                    ["owner"] = w.Owner,
                    ["task"] = w.Task,
                    ["waited_ms"] = (long)(now - w.Enqueued).TotalMilliseconds,
                    ["after"] = Unfinished(w.After),
                }).ToArray(),
            });
            return result;
        }
    }

    #endregion

    #region Gate

    /// <summary>Why this session may not run the tool now, or null when it may.</summary>
    public string? Refusal(LockCaller caller, string toolName)
    {
        if (!_options.Value.EnforceEditorLock || Exempt.Contains(toolName) || IsReadOnly(toolName))
            return null;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (Maintain(now))
                _ = PublishAsync();
            if (_holder is not { } held || held.Key == caller.Key)
                return null;
            return $"Refused '{toolName}': {Describe(held, now)} Until it is released only read-only tools run for other " +
                   $"sessions. Call {AcquireName} (it waits in line for its turn), or hand off (write a test case and stop).";
        }
    }

    /// <summary>The refusal as a tool result, or null when the session may run the tool.</summary>
    public CallToolResult? RefusalResult(LockCaller caller, string toolName)
    {
        if (Refusal(caller, toolName) is not { } text)
            return null;
        lock (_gate)
            return ToolErrors.Create(ToolErrors.EditorLocked, text, retryAfterMs: 5000,
                new Dictionary<string, object?> { ["holder"] = _holder?.Owner, ["queue_length"] = _queue.Count });
    }

    /// <summary>
    /// The gate every Unity tool call passes. With the implicit lock on, an editor-changing tool from a session that holds
    /// no lock claims the editor on the spot when it is free, or waits in the queue like <see cref="AcquireName"/> and runs
    /// when granted; when the slice ends the tool does not run and the answer is the structured <c>lock_queued</c> error.
    /// Null means the tool may run. Read-only and exempt tools never wait.
    /// </summary>
    public async Task<CallToolResult?> GateAsync(LockCaller caller, string toolName, CancellationToken ct)
    {
        if (!_options.Value.ImplicitLock || caller.Key is null)
            return RefusalResult(caller, toolName);
        if (!_options.Value.EnforceEditorLock || Exempt.Contains(toolName) || IsReadOnly(toolName))
            return null;
        var result = await AcquireAsync(caller, null, $"{toolName} (implicit)", wait: true, ticket: null, ct, toolName);
        if (result.StructuredContent is { ValueKind: JsonValueKind.Object } body && body.TryGetProperty("acquired", out var granted) && granted.ValueKind == JsonValueKind.True)
            return null;
        result.IsError = true;
        return result;
    }

    /// <summary>
    /// Counts a Unity call of the holder, so a long call never reads as idle. A read-only call renews the claim only while
    /// the editor works for the holder (<see cref="EditorWorks"/>): reading an idle editor is not using it.
    /// </summary>
    public IDisposable Track(LockCaller caller, string toolName)
    {
        var reads = !_options.Value.EditorLockReadOnlyKeepsAlive && IsReadOnly(toolName);
        lock (_gate)
        {
            if (_holder is not { } held || held.Key != caller.Key)
                return NoCall.Instance;
            var now = _time.GetUtcNow();
            var renews = !reads || EditorWorks(held, _registry.GetLatestLink(), now);
            held.InFlight++;
            // any call shows the granted session is there, a read-only one too
            held.AwaitingFirstCall = false;
            if (renews)
                held.LastActivity = now;
            return new TrackedCall(this, held, renews);
        }
    }

    /// <summary>
    /// What a caller is told with a Unity tool result, or null: once, that its claim passed to a subagent it waited for;
    /// with every result, that its explicit claim is past <see cref="Cap"/>.
    /// </summary>
    public string? HolderNotice(LockCaller caller)
    {
        lock (_gate)
        {
            if (caller.Key is { } key && _handed.Remove(key, out var child))
                return $"Editor lock: your claim passed to your subagent {child} while you waited for it. {AcquireName} again before more editor work.";
            if (!_options.Value.EditorLockCapNudge)
                return null;
            var now = _time.GetUtcNow();
            if (_holder is not { Implicit: false } held || held.Key != caller.Key || now - held.Acquired <= Cap)
                return null;
            if (now - held.NoticeLogged >= NoticeLogBeat)
            {
                held.NoticeLogged = now;
                _logger.LogInformation("Editor lock of {Owner} (session {SessionId}) is past the cap at {Age} with {Waiting} waiting; its tool results say so",
                    held.Owner, held.SessionId, Age(now - held.Acquired), _queue.Count);
            }
            return $"Editor lock: you have held the editor for {Age(now - held.Acquired)} for \"{held.Task}\", past the 15-minute cap" +
                   (_queue.Count > 0 ? $", and {_queue.Count} session(s) are waiting" : string.Empty) +
                   $". Finish this step (exit play mode you started), call {ReleaseName} now and acquire again for the next burst.";
        }
    }

    // runtime tools act on a player build, never on the Editor
    bool IsReadOnly(string toolName)
        => _registry.IsRuntimeTool(toolName) || _registry.GetLatestLink()?.Registry?.Tools
            .FirstOrDefault(tool => string.Equals(tool.Name, toolName, StringComparison.OrdinalIgnoreCase))?.ReadOnly == true;

    sealed class TrackedCall(EditorLock owner, Holder holder, bool renews) : IDisposable
    {
        int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 1)
                return;
            lock (owner._gate)
            {
                holder.InFlight = Math.Max(0, holder.InFlight - 1);
                if (renews)
                    holder.LastActivity = owner._time.GetUtcNow();
            }
        }
    }

    sealed class NoCall : IDisposable
    {
        public static readonly NoCall Instance = new();
        public void Dispose() { }
    }

    #endregion

    #region Editor side

    /// <summary>Frees whatever holds the editor, including a lock file left by another session. Returns what was freed.</summary>
    public string ForceRelease()
    {
        string freed;
        lock (_gate)
        {
            if (_holder is { } held)
            {
                freed = $"the claim of {held.Owner}";
                Free("freed from the Unity editor by a person", stopPlay: true);
            }
            else if (_registry.GetLatestLink()?.ProjectRoot is { } root && ReadFile(root) is { } file)
            {
                freed = file.Owner.Length == 0 ? "the lock file" : $"the lock file of {file.Owner}";
                DeleteDirectory(root);
                _outside = null;
                _logger.LogWarning("Editor lock file of {Owner} removed from the Unity editor", file.Owner);
                Pump(_time.GetUtcNow());
            }
            else
            {
                freed = "nothing";
            }
        }
        _ = PublishAsync();
        return freed;
    }

    void OnSessionEnded(string sessionId)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            foreach (var waiter in _queue.Where(w => w.SessionId == sessionId).ToArray())
                Drop(waiter);
            Finish(n => n.SessionId == sessionId, now);
            _handed.Remove(sessionId);
            if (_holder is { } held && held.SessionId == sessionId)
                Free("its MCP session closed", stopPlay: true);
            else
                Pump(now);
        }
        _ = PublishAsync();
    }

    // a subagent that ended without releasing would keep its claim until the idle timeout
    void OnAgentEnded(string sessionId, string agentId)
    {
        var key = new LockCaller(sessionId, agentId).Key;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            foreach (var waiter in _queue.Where(w => w.Key == key).ToArray())
                Drop(waiter);
            Finish(n => n.Key == key, now);
            if (_holder is { } held && held.Key == key)
                Free("its subagent ended", stopPlay: true);
            else
                Pump(now);
        }
        _ = PublishAsync();
    }

    // a client that opened a new MCP session (a reconnect) is the same agents: its claims move with it instead of
    // blocking it behind its own old session until that one times out
    void OnSessionReplaced(string old, string current)
    {
        string Moved(string key) => key == old ? current : key.StartsWith(old + "#", StringComparison.Ordinal) ? current + key[old.Length..] : key;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            foreach (var waiter in _queue.Where(w => w.SessionId == old).ToArray())
            {
                if (_queue.Exists(w => w.Key == Moved(waiter.Key)))
                    Drop(waiter);
                else
                    waiter.Caller = waiter.Caller with { SessionId = current };
            }
            foreach (var named in _named.Where(n => n.SessionId == old))
            {
                named.Key = Moved(named.Key);
                named.SessionId = current;
            }
            if (_playOwner is { } play)
                _playOwner = (Moved(play.Key), play.Owner);
            if (_lastDropped is { } dropped)
                _lastDropped = dropped with { Key = Moved(dropped.Key) };
            if (_holder is { } held && held.SessionId == old)
            {
                DeleteMirror(held);
                held.Caller = held.Caller with { SessionId = current };
                WriteMirror(held, now);
                _logger.LogInformation("Editor lock of {Owner} moved from session {Old} to {Current}: the client reconnected", held.Owner, old, current);
            }
        }
        _ = PublishAsync();
    }

    /// <summary>Lets one queued agent stop waiting for the agents it named with 'after' (a person's choice in the editor). Returns who.</summary>
    public string SkipAfter(string ticket)
    {
        string skipped;
        lock (_gate)
        {
            if (_queue.Find(w => w.Ticket == ticket) is { } waiter)
            {
                waiter.After = [];
                skipped = waiter.Owner;
                _logger.LogInformation("{Owner} no longer waits for other agents: let through from the Unity editor", waiter.Owner);
                Pump(_time.GetUtcNow());
            }
            else
            {
                skipped = "nothing";
            }
        }
        _ = PublishAsync();
        return skipped;
    }

    /// <summary>Removes one session from the queue (a person's choice in the editor). Returns who was removed.</summary>
    public string RemoveWaiter(string ticket)
    {
        string removed;
        lock (_gate)
        {
            if (_queue.Find(w => w.Ticket == ticket) is { } waiter)
            {
                waiter.Removed = true;
                _removed[ticket] = _time.GetUtcNow();
                Drop(waiter);
                removed = waiter.Owner;
                _logger.LogInformation("{Owner} removed from the editor lock queue from the Unity editor", waiter.Owner);
            }
            else
            {
                removed = "nothing";
            }
        }
        _ = PublishAsync();
        return removed;
    }

    /// <summary>Pauses or resumes the queue: while paused nobody is granted the lock. Returns the new state.</summary>
    public bool SetQueuePaused(bool paused)
    {
        lock (_gate)
        {
            _paused = paused;
            Maintain(_time.GetUtcNow());
        }
        _ = PublishAsync();
        return paused;
    }

    #endregion

    #region State

    /// <summary>Notes who started play mode, from a heartbeat already applied to the link.</summary>
    public void ObserveEditor(UnityLink link)
    {
        // a link that drops in the domain reload of entering play keeps its owner
        if (!link.Connected || !link.Features.Contains(FmpCapabilities.AgentPlay))
            return;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (link.EditorState is FmpEditorState.Ready or FmpEditorState.Compiling)
                _playOwner = null;
            else if (_playOwner is null && AgentPlaying(link))
            {
                if (_holder is { } held)
                    _playOwner = (held.Key, held.Owner);
                else
                {
                    // the claim of the agent that started it may have ended while the editor reloaded
                    _playOwner = _lastDropped is { } dropped && now - dropped.At < PlayHandoff ? (dropped.Key, dropped.Owner) : (string.Empty, "an agent");
                    _playLeftAt = now;
                }
            }
            Maintain(now);
        }
        _ = PublishAsync();
    }

    static readonly TimeSpan PlayHandoff = TimeSpan.FromSeconds(60);

    /// <summary>Periodic pass: frees idle claims, notices lock files, keeps the mirror fresh and tells the editor.</summary>
    public Task TickAsync()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            Maintain(now);
            if (_holder is { } held)
            {
                if (now - held.MirrorWritten >= MirrorBeat)
                    WriteMirror(held, now);
            }
            else
            {
                var root = _registry.GetLatestLink()?.ProjectRoot;
                _outside = root is null ? null : ReadLiveOutsideLock(root, now);
            }
        }
        return PublishAsync();
    }

    public EditorLockState Snapshot()
    {
        lock (_gate)
        {
            var link = _registry.GetLatestLink();
            return Holding() with
            {
                Waiting = _queue.Select(w => new EditorLockWaiter
                {
                    Ticket = w.Ticket,
                    Owner = w.Owner,
                    Task = w.Task,
                    ClientName = w.ClientName,
                    // one value per agent: the editor keys its display by it
                    SessionId = w.Key,
                    WaitingSinceUnixMs = w.Enqueued.ToUnixTimeMilliseconds(),
                    Attached = w.Pending is not null,
                    After = string.Join(", ", Unfinished(w.After)),
                    AgentType = w.Caller.AgentType,
                    Subagent = w.Caller.IsSubagent,
                }).ToArray(),
                QueuePaused = _paused,
                QueueHeldByPlayMode = _queue.Count > 0 && _holder is null && PlayLeftOn(link) is null
                    && link is { Connected: true, EditorState: FmpEditorState.Playing or FmpEditorState.Paused },
                PlayLeftBy = PlayLeftOn(link)?.Owner,
            };
        }
    }

    // caller holds _gate
    EditorLockState Holding()
    {
        if (_holder is { } held)
        {
            var idle = IdleLimit(held);
            return new EditorLockState
            {
                Held = true,
                Kind = KindAgent,
                Owner = held.Owner,
                Task = held.Task,
                ClientName = held.ClientName,
                ClientVersion = held.ClientVersion,
                SessionId = held.Key,
                AgentType = held.Caller.AgentType,
                Subagent = held.Caller.IsSubagent,
                Hooked = _agents.Reports(held.SessionId),
                AcquiredUnixMs = held.Acquired.ToUnixTimeMilliseconds(),
                LastActivityUnixMs = held.LastActivity.ToUnixTimeMilliseconds(),
                ExpiresUnixMs = held.InFlight > 0 ? 0
                    : held.AwaitingFirstCall ? (held.GrantedAt + _options.Value.EditorLockFirstCallDeadline).ToUnixTimeMilliseconds()
                    : (held.LastActivity + idle).ToUnixTimeMilliseconds(),
                CallsInFlight = held.InFlight,
            };
        }
        if (_outside is { } outside)
        {
            return new EditorLockState
            {
                Held = true,
                Kind = KindOutside,
                Owner = outside.Owner,
                Task = outside.Task,
                AcquiredUnixMs = ParseAcquired(outside.Acquired)?.ToUnixTimeMilliseconds() ?? outside.Created.ToUnixTimeMilliseconds(),
                LastActivityUnixMs = outside.Heartbeat > 0 ? outside.Heartbeat * 1000 : outside.Created.ToUnixTimeMilliseconds(),
                ExpiresUnixMs = outside.Heartbeat > 0 ? (outside.Heartbeat + (long)FileTtl.TotalSeconds) * 1000 : 0,
            };
        }
        return new EditorLockState();
    }

    /// <summary>Sends the current state to every linked editor that shows it, when it changed (or to one link, always).</summary>
    public async Task PublishAsync(UnityLink? only = null)
    {
        // one at a time: two passes that ran side by side could deliver the older state last, and the editor kept it
        await _publishing.WaitAsync();
        try
        {
            await PublishNowAsync(only);
        }
        finally
        {
            _publishing.Release();
        }
    }

    readonly SemaphoreSlim _publishing = new(1, 1);

    async Task PublishNowAsync(UnityLink? only)
    {
        (string ConnectionId, string Reason)? stop;
        lock (_gate)
        {
            stop = _stopPlay;
            _stopPlay = null;
        }
        if (stop is { } send)
        {
            try
            {
                await _hub.Clients.Client(send.ConnectionId).StopAgentPlay(send.Reason);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to tell the editor to stop play mode");
            }
        }
        var state = Snapshot();
        if (only is null)
        {
            lock (_gate)
            {
                if (Same(state, _published))
                    return;
                _published = state;
            }
        }
        foreach (var link in only is null ? _registry.Links : [only])
        {
            if (!link.Connected || !link.Features.Contains(FmpCapabilities.EditorLock))
                continue;
            try
            {
                await _hub.Clients.Client(link.ConnectionId).EditorLockChanged(state);
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "Failed to send the editor lock to {ProjectId}", link.ProjectId);
            }
        }
    }

    static bool Same(EditorLockState a, EditorLockState b)
        => (a with { Waiting = [] }) == (b with { Waiting = [] }) && a.Waiting.SequenceEqual(b.Waiting);

    // shorter while other sessions wait
    TimeSpan IdleLimit(Holder held)
    {
        // a play session waits on the game, not on tool calls
        if (InOwnPlay(held, _registry.GetLatestLink()))
            return _options.Value.EditorLockPlayIdleTimeout;
        var limit = _queue.Count > 0 ? _options.Value.EditorLockWaitingIdleTimeout : _options.Value.EditorLockIdleTimeout;
        return held.Implicit && _options.Value.EditorLockImplicitIdle < limit ? _options.Value.EditorLockImplicitIdle : limit;
    }

    // caller holds _gate; the play session the holder started itself
    bool InOwnPlay(Holder held, UnityLink? link) => _playOwner?.Key == held.Key && AgentPlaying(link);

    // caller holds _gate; a compile, a reload, an import, a held main thread (a player build), a test run or the holder's
    // own play session: reading the editor then is part of the burst
    bool EditorWorks(Holder held, UnityLink? link, DateTimeOffset now)
        => link is { Connected: false } or { EditorState: FmpEditorState.Compiling or FmpEditorState.Reloading } or { Heartbeat.Updating: true }
           || (link?.Heartbeat is { LastMainTickUnixMs: > 0 } beat && now - DateTimeOffset.FromUnixTimeMilliseconds(beat.LastMainTickUnixMs) > EditorHealth.StallAfter)
           || TestsRunning(link) || InOwnPlay(held, link);

    // caller holds _gate; true when it freed a claim
    bool ExpireIfIdle(DateTimeOffset now)
    {
        if (_holder is not { } held)
            return false;
        if (held.AwaitingFirstCall && held.InFlight == 0 && now - held.GrantedAt > _options.Value.EditorLockFirstCallDeadline)
        {
            Free($"no tool call within {Age(_options.Value.EditorLockFirstCallDeadline)} of being granted its turn");
            return true;
        }
        if (_queue.Count > 0 && held.InFlight == 0)
        {
            var age = now - held.Acquired;
            var maxAge = _options.Value.EditorLockImplicitMaxAge;
            if (held.Implicit && age > maxAge && !InOwnPlay(held, _registry.GetLatestLink()))
            {
                Free($"implicit claim older than {Age(maxAge)} while others wait");
                return true;
            }
            if (!held.Implicit && _options.Value.EditorLockHardCap is { } hardCap && age > hardCap)
            {
                Free($"past the hard cap of {Age(hardCap)} while others wait", stopPlay: true);
                return true;
            }
        }
        if (held.InFlight == 0 && now - held.LastActivity > IdleLimit(held))
        {
            Free($"no call for {Age(now - held.LastActivity)}", stopPlay: true);
            return true;
        }
        if (!_clients.IsOpen(held.SessionId))
        {
            Free("its MCP session closed", stopPlay: true);
            return true;
        }
        return false;
    }

    // caller holds _gate
    // stopPlay: the holder is gone for good, so play mode it started is stopped instead of left to block the queue
    void Free(string reason, bool stopPlay = false, bool pump = true)
    {
        if (_holder is not { } held)
            return;
        var now = _time.GetUtcNow();
        _holder = null;
        _lastDropped = (held.Key, reason, held.Implicit, held.Owner, now);
        DeleteMirror(held);
        _logger.LogInformation("Editor lock of {Owner} ended: {Reason}", held.Owner, reason);
        var link = _registry.GetLatestLink();
        if (_playOwner?.Key == held.Key && AgentPlaying(link))
        {
            _playLeftAt = now;
            if (stopPlay && !TestsRunning(link))
            {
                _stopPlay = (link!.ConnectionId, $"the editor lock of {held.Owner} ended ({reason})");
                _logger.LogInformation("Stopping play mode {Owner} left on", held.Owner);
            }
        }
        if (pump)
            Pump(now);
    }

    #endregion

    #region Names

    static readonly TimeSpan NameKept = TimeSpan.FromMinutes(30);

    // caller holds _gate; an agent that says its label can be waited for with 'after'
    void Name(LockCaller caller, string label)
    {
        if (_named.Exists(n => n.Done is null && n.Key == caller.Key && Same(n.Label, label)))
            return;
        _named.Add(new Named { Label = label, Key = caller.Key!, SessionId = caller.SessionId! });
    }

    // caller holds _gate; these agents are done: whoever waits for them may go
    void Finish(Predicate<Named> which, DateTimeOffset now)
    {
        foreach (var named in _named.Where(n => n.Done is null && which(n)))
            named.Done = now;
    }

    // caller holds _gate; the labels of 'after' whose agents have not finished
    List<string> Unfinished(string[] after)
    {
        var open = new List<string>();
        foreach (var label in after)
        {
            var named = _named.FindAll(n => Same(n.Label, label));
            if (named.Count > 0 ? named.Exists(n => n.Done is null) : _agents.TypeState(label) != AgentDirectory.Known.Ended)
                open.Add(label);
        }
        return open;
    }

    // caller holds _gate
    bool IsKnown(string label) => _named.Exists(n => Same(n.Label, label)) || _agents.TypeState(label) != AgentDirectory.Known.Unknown;

    // caller holds _gate
    string KnownNames()
    {
        var names = _named.Where(n => n.Done is null).Select(n => n.Label).Concat(_agents.LiveTypes()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return names.Length == 0 ? "No agent has said its name yet." : $"Known now: {string.Join(", ", names)}.";
    }

    // caller holds _gate; why this 'after' can never be met, or null
    string? AfterProblem(string key, string[] after)
    {
        foreach (var label in after)
        {
            var named = _named.FindAll(n => n.Done is null && Same(n.Label, label));
            if (named.Exists(n => n.Key == key))
                return $"'after' names yourself ({label}): you would wait for your own work to finish.";
            if (named.Find(n => WaitsFor(n.Key, key, [])) is { } loop)
                return $"'after' would never be met: {label} already waits for you to finish. One of you must go first.";
        }
        return null;
    }

    // caller holds _gate; true when the queued agent 'from' waits, directly or through others, for the agent 'target'
    bool WaitsFor(string from, string target, HashSet<string> seen)
    {
        if (!seen.Add(from) || _queue.Find(w => w.Key == from) is not { } waiter)
            return false;
        foreach (var named in _named.Where(n => n.Done is null && waiter.After.Any(label => Same(n.Label, label))))
            if (named.Key == target || WaitsFor(named.Key, target, seen))
                return true;
        return false;
    }

    // caller holds _gate
    string? AfterNote(string[] after) => Unfinished(after) is { Count: > 0 } open
        ? $"you wait for {string.Join(", ", open)} to finish ('after'); nobody is held back by you meanwhile."
        : null;

    static string[] Labels(string[]? after)
        => (after ?? []).Select(Clip).Where(label => label.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    string DefaultOwner(LockCaller caller, Implementation? client)
        => caller.IsSubagent ? $"{(caller.AgentType.Length > 0 ? caller.AgentType : "subagent")} {Short(caller.AgentId)}"
            : string.IsNullOrEmpty(client?.Name) ? "agent" : client!.Name;

    #endregion

    #region Lock file

    static string LockDirectory(string root) => Path.Combine(root, "Temp", "agent-editor-lock");

    // the lock file of a session outside the matrix, when it is live; a stale one, or a mirror the matrix no longer backs, is removed
    FileLock? ReadLiveOutsideLock(string root, DateTimeOffset now)
    {
        var file = ReadFile(root);
        if (file is null)
            return null;
        if (file.Matrix)
        {
            DeleteDirectory(root);
            return null;
        }
        var live = file.Heartbeat > 0
            ? now.ToUnixTimeSeconds() - file.Heartbeat <= (long)FileTtl.TotalSeconds
            : now - file.Created <= FileTtl;
        if (live)
            return file;
        // delete only if nobody beat in between
        if (ReadFile(root)?.Heartbeat == file.Heartbeat)
        {
            DeleteDirectory(root);
            _logger.LogInformation("Stale editor lock file of {Owner} removed", file.Owner);
        }
        return null;
    }

    FileLock? ReadFile(string root)
    {
        var directory = LockDirectory(root);
        try
        {
            if (!Directory.Exists(directory))
                return null;
            var created = new DateTimeOffset(Directory.GetCreationTimeUtc(directory), TimeSpan.Zero);
            var path = Path.Combine(directory, "lock.json");
            if (!File.Exists(path))
                return new FileLock(string.Empty, string.Empty, string.Empty, 0, false, string.Empty, created);
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var json = document.RootElement;
                return new FileLock(
                    Read(json, "owner"), Read(json, "task"), Read(json, "acquired"),
                    long.TryParse(Read(json, "heartbeat"), out var beat) ? beat : 0,
                    json.TryGetProperty("matrix", out var matrix) && matrix.ValueKind == JsonValueKind.True,
                    Read(json, "session"), created);
            }
            catch (JsonException)
            {
                // half-written: live while the directory is young
                return new FileLock(string.Empty, string.Empty, string.Empty, 0, false, string.Empty, created);
            }
        }
        catch (IOException e)
        {
            _logger.LogDebug(e, "Could not read the editor lock file under {Root}", root);
            return null;
        }
        catch (UnauthorizedAccessException e)
        {
            _logger.LogDebug(e, "Could not read the editor lock file under {Root}", root);
            return null;
        }
    }

    void WriteMirror(Holder holder, DateTimeOffset now)
    {
        holder.MirrorWritten = now;
        if (holder.Root is not { } root || !Directory.Exists(Path.Combine(root, "Temp")))
            return;
        try
        {
            var directory = LockDirectory(root);
            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["owner"] = holder.Owner,
                ["task"] = holder.Task,
                ["acquired"] = holder.Acquired.ToOffset(_time.LocalTimeZone.GetUtcOffset(holder.Acquired)).ToString("yyyy-MM-ddTHH:mm:sszzz"),
                ["heartbeat"] = now.ToUnixTimeSeconds(),
                ["matrix"] = true,
                ["session"] = holder.Key,
                ["client"] = holder.ClientName,
            }, FileJson);
            File.WriteAllText(Path.Combine(directory, "lock.json"), json + "\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(e, "Could not write the editor lock file under {Root}", root);
        }
    }

    void DeleteMirror(Holder holder)
    {
        if (holder.Root is { } root && ReadFile(root) is { Matrix: true } file && file.Session == holder.Key)
            DeleteDirectory(root);
    }

    void DeleteDirectory(string root)
    {
        try
        {
            var directory = LockDirectory(root);
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(e, "Could not remove the editor lock file under {Root}", root);
        }
    }

    static string Read(JsonElement json, string name)
    {
        if (!json.TryGetProperty(name, out var value))
            return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty,
        };
    }

    static DateTimeOffset? ParseAcquired(string text)
        => DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var value) ? value : null;

    #endregion

    #region Text

    const string Reminder = "Release it as soon as this burst ends (cap 15 minutes per claim); read-only tools never need it.";

    string Describe(Holder held, DateTimeOffset now)
    {
        var age = now - held.Acquired;
        var over = age > Cap ? ", past the 15-minute cap" : string.Empty;
        var client = $"{held.ClientName} {held.ClientVersion}".Trim();
        var busy = held.InFlight > 0 ? $"{held.InFlight} call(s) running" : $"last call {Age(now - held.LastActivity)} ago";
        var subagent = held.Caller.IsSubagent ? $", subagent {Short(held.Caller.AgentId)}" : string.Empty;
        return $"held by {held.Owner} ({(client.Length > 0 ? client + ", " : string.Empty)}session {Short(held.SessionId)}{subagent}) for " +
               $"\"{held.Task}\" since {Clock(held.Acquired)} ({Age(age)}{over}; {busy}).";
    }

    string DescribeOutside(FileLock file, DateTimeOffset now)
    {
        var owner = file.Owner.Length == 0 ? "a session" : file.Owner;
        var task = file.Task.Length == 0 ? string.Empty : $" for \"{file.Task}\"";
        var beat = file.Heartbeat > 0 ? $", last heartbeat {Age(now - DateTimeOffset.FromUnixTimeSeconds(file.Heartbeat))} ago" : string.Empty;
        return $"held by {owner}{task} through the lock file Temp/agent-editor-lock (it does not claim through the matrix){beat}.";
    }

    // caller holds _gate
    string EditorLine(UnityLink? link) => AgentPlaying(link) && _playOwner is { } left && left.Key != _holder?.Key
        ? $"Editor: in play mode left on by {left.Owner} (an agent); exit play mode first, before your own work."
        : link switch
    {
        null => "No Unity editor is linked right now.",
        { Connected: false } => "Editor: reloading or reconnecting.",
        _ => link.EditorState switch
        {
            FmpEditorState.Compiling => "Editor: compiling; wait for it before playing.",
            FmpEditorState.Reloading => "Editor: reloading.",
            FmpEditorState.Playing => "Editor: in play mode.",
            FmpEditorState.Paused => "Editor: in play mode, paused.",
            _ => "Editor: ready.",
        },
    };

    string Clock(DateTimeOffset time) => time.ToOffset(_time.LocalTimeZone.GetUtcOffset(time)).ToString("HH:mm");

    static string Age(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;
        return span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes}m {span.Seconds}s" : $"{span.Seconds}s";
    }

    static string Short(string sessionId) => sessionId.Length > 8 ? sessionId[..8] : sessionId;

    static string Clip(string? text)
    {
        text = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length > MaxTextLength ? text[..MaxTextLength] : text;
    }

    static bool? Flag(IDictionary<string, JsonElement>? args, string name)
        => args is not null && args.TryGetValue(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    static string? Text(IDictionary<string, JsonElement>? args, string name)
        => args is not null && args.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static string[]? Texts(IDictionary<string, JsonElement>? args, string name)
        => args is not null && args.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray()
            : null;

    static JsonElement Schema(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    static CallToolResult Ok(string text) => new() { Content = [new TextContentBlock { Text = text }] };

    static CallToolResult Error(string text) => new() { IsError = true, Content = [new TextContentBlock { Text = text }] };

    #endregion
}
