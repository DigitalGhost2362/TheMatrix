using System.Collections.Concurrent;
using Feeder.Matrix.Protocol;

namespace Feeder.Matrix.Unity;

public sealed class UnityLink
{
    public required string ConnectionId { get; set; }
    public required string ProjectId { get; init; }
    public required string UnityInstanceId { get; init; }
    public required string SessionId { get; set; }
    public FmpEditorState EditorState { get; set; }
    public RegistrySnapshot? Registry { get; set; }
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
    public bool Connected { get; set; } = true;
    public string? ProjectRoot { get; set; }
    public string? McpEntryName { get; set; }
    public string? PluginVersion { get; set; }

    /// <summary>Optional editor features the plugin handles (<see cref="UnityRegisterRequest.Features"/>).</summary>
    public string[] Features { get; set; } = [];

    /// <summary>Last background heartbeat; null for plugins that never send one.</summary>
    public HeartbeatMessage? Heartbeat { get; set; }
    public DateTimeOffset HeartbeatAt { get; set; }

    /// <summary>Set once the link stayed down past the reconnect grace and clients were told its tools are gone.</summary>
    public bool ListedAsGone { get; set; }

    /// <summary>True for a built player that runs the Runtime tools; false for the Unity editor.</summary>
    public bool IsPlayer { get; init; }
}

/// <summary>An MCP tool call waiting for Unity. Its deadline moves when Unity defers or asks for more time.</summary>
public sealed class PendingOperation(string operationId, string toolName, string projectId, DateTimeOffset started, DateTimeOffset deadline)
{
    public string OperationId { get; } = operationId;
    public string ToolName { get; } = toolName;
    public string ProjectId { get; } = projectId;
    public DateTimeOffset Started { get; } = started;
    public TaskCompletionSource<ToolResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string? SentOnConnectionId { get; set; }

    private long _deadlineTicks = deadline.UtcTicks;
    public DateTimeOffset Deadline => new(Interlocked.Read(ref _deadlineTicks), TimeSpan.Zero);

    /// <summary>Moves the deadline later only; never earlier.</summary>
    public void ExtendTo(DateTimeOffset deadline)
    {
        long next = deadline.UtcTicks, current;
        do
        {
            current = Interlocked.Read(ref _deadlineTicks);
            if (next <= current)
                return;
        } while (Interlocked.CompareExchange(ref _deadlineTicks, next, current) != current);
    }

    public volatile bool Deferred;
}

/// <summary>
/// Tracks linked Unity editor instances and in-flight tool operations. Operations are keyed by
/// operationId — independent of SignalR connection ids — so results arriving on a new connection
/// (after a domain reload) still complete the original waiter (protocol spec §5).
/// </summary>
public sealed class UnityLinkRegistry
{
    private readonly ConcurrentDictionary<string, UnityLink> _linksByProject = new();
    private readonly ConcurrentDictionary<string, PendingOperation> _pendingOps = new();
    // Players never take the editor's slot: several can be linked to one project, keyed by instance.
    private readonly ConcurrentDictionary<string, UnityLink> _players = new();

    public UnityLink Register(UnityRegisterRequest request, string connectionId)
    {
        var link = new UnityLink
        {
            ConnectionId = connectionId,
            ProjectId = request.ProjectId,
            UnityInstanceId = request.UnityInstanceId,
            SessionId = Guid.NewGuid().ToString("N"),
            EditorState = request.EditorState,
            ProjectRoot = request.ProjectRoot,
            McpEntryName = request.McpEntryName,
            PluginVersion = request.PluginVersion,
            Features = request.Features ?? [],
        };
        // Newest registration wins for a project (ADR-0003); resume keeps the instance id.
        _linksByProject.AddOrUpdate(request.ProjectId, link, (_, existing) =>
        {
            if (existing.UnityInstanceId == request.UnityInstanceId)
            {
                existing.ConnectionId = connectionId;
                existing.SessionId = link.SessionId;
                existing.EditorState = request.EditorState;
                existing.Connected = true;
                existing.LastSeen = DateTimeOffset.UtcNow;
                existing.ProjectRoot = request.ProjectRoot;
                existing.McpEntryName = request.McpEntryName;
                existing.PluginVersion = request.PluginVersion;
                existing.Features = request.Features ?? [];
                // the beats before a reload describe the old domain; the new one starts its own
                existing.Heartbeat = null;
                return existing;
            }
            return link;
        });
        return _linksByProject[request.ProjectId];
    }

    /// <summary>Links a built player. It lives beside the editor link of its project and never replaces it.</summary>
    public UnityLink RegisterPlayer(UnityRegisterRequest request, string connectionId)
    {
        var link = new UnityLink
        {
            ConnectionId = connectionId,
            ProjectId = request.ProjectId,
            UnityInstanceId = request.UnityInstanceId,
            SessionId = Guid.NewGuid().ToString("N"),
            EditorState = request.EditorState,
            PluginVersion = request.PluginVersion,
            Features = request.Features ?? [],
            IsPlayer = true,
        };
        return _players.AddOrUpdate(request.UnityInstanceId, link, (_, existing) =>
        {
            existing.ConnectionId = connectionId;
            existing.Connected = true;
            existing.LastSeen = DateTimeOffset.UtcNow;
            return existing;
        });
    }

    /// <summary>The newest connected player, preferring one of the given project.</summary>
    public UnityLink? GetPlayerLink(string? projectId = null)
        => _players.Values.Where(p => p.Connected)
            .OrderByDescending(p => p.ProjectId == projectId)
            .ThenByDescending(p => p.LastSeen)
            .FirstOrDefault();

    public IReadOnlyCollection<UnityLink> Players => _players.Values.ToArray();

    public UnityLink? GetPlayerByConnection(string connectionId)
        => _players.Values.FirstOrDefault(p => p.Connected && p.ConnectionId == connectionId);

    /// <summary>True when a connected player offers a Runtime tool of this name.</summary>
    public bool IsRuntimeTool(string? toolName)
        => !string.IsNullOrEmpty(toolName) && _players.Values.Any(p => p.Connected && p.Registry?.Tools.Any(t =>
            t.Target == FmpToolTargets.Runtime && string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase)) == true);

    public void MarkDisconnected(string connectionId)
    {
        // a player has no domain reload to wait out: its link is gone with its connection
        foreach (var player in _players.Values.Where(p => p.ConnectionId == connectionId))
            _players.TryRemove(player.UnityInstanceId, out _);
        foreach (var link in _linksByProject.Values)
        {
            if (link.ConnectionId == connectionId)
            {
                link.Connected = false;
                link.LastSeen = DateTimeOffset.UtcNow;
            }
        }
    }

    public void Unregister(string projectId, string unityInstanceId)
    {
        _players.TryRemove(unityInstanceId, out _);
        if (_linksByProject.TryGetValue(projectId, out var link) && link.UnityInstanceId == unityInstanceId)
            _linksByProject.TryRemove(projectId, out _);
    }

    /// <summary>Default link: single-project setups route to the only registered editor.</summary>
    public UnityLink? GetDefaultLink()
        => _linksByProject.Values.FirstOrDefault(l => l.Connected);

    /// <summary>The connected link, else the most recently seen one (it may be reloading).</summary>
    public UnityLink? GetLatestLink()
        => GetDefaultLink() ?? _linksByProject.Values.OrderByDescending(l => l.LastSeen).FirstOrDefault();

    public UnityLink? GetLink(string projectId)
        => _linksByProject.TryGetValue(projectId, out var link) ? link : null;

    public IReadOnlyCollection<UnityLink> Links => _linksByProject.Values.ToArray();

    public bool IsRegistered(string connectionId)
        => _linksByProject.Values.Any(l => l.Connected && l.ConnectionId == connectionId);

    public PendingOperation CreateOperation(string operationId, string toolName = "", string projectId = "", TimeSpan? timeout = null)
    {
        var now = DateTimeOffset.UtcNow;
        var operation = new PendingOperation(operationId, toolName, projectId, now, now + (timeout ?? TimeSpan.FromSeconds(60)));
        if (!_pendingOps.TryAdd(operationId, operation))
            throw new InvalidOperationException($"Duplicate operationId '{operationId}'.");
        return operation;
    }

    public PendingOperation? GetOperation(string operationId)
        => _pendingOps.TryGetValue(operationId, out var operation) ? operation : null;

    public bool CompleteOperation(ToolResult result)
        => _pendingOps.TryRemove(result.OperationId, out var operation) && operation.Completion.TrySetResult(result);

    public void AbandonOperation(string operationId)
        => _pendingOps.TryRemove(operationId, out _);

    public IReadOnlyCollection<PendingOperation> Operations => _pendingOps.Values.ToArray();

    public string[] AwaitedOperationIds => _pendingOps.Keys.ToArray();
}
