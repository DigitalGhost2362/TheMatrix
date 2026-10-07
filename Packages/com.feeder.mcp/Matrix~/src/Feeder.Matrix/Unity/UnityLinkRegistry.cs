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
}

/// <summary>
/// Tracks linked Unity editor instances and in-flight tool operations. Operations are keyed by
/// operationId — independent of SignalR connection ids — so results arriving on a new connection
/// (after a domain reload) still complete the original waiter (protocol spec §5).
/// </summary>
public sealed class UnityLinkRegistry
{
    private readonly ConcurrentDictionary<string, UnityLink> _linksByProject = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ToolResult>> _pendingOps = new();

    public UnityLink Register(UnityRegisterRequest request, string connectionId)
    {
        var link = new UnityLink
        {
            ConnectionId = connectionId,
            ProjectId = request.ProjectId,
            UnityInstanceId = request.UnityInstanceId,
            SessionId = Guid.NewGuid().ToString("N"),
            EditorState = request.EditorState,
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
                return existing;
            }
            return link;
        });
        return _linksByProject[request.ProjectId];
    }

    public void MarkDisconnected(string connectionId)
    {
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
        if (_linksByProject.TryGetValue(projectId, out var link) && link.UnityInstanceId == unityInstanceId)
            _linksByProject.TryRemove(projectId, out _);
    }

    /// <summary>Default link: single-project setups route to the only registered editor.</summary>
    public UnityLink? GetDefaultLink()
        => _linksByProject.Values.FirstOrDefault(l => l.Connected);

    public UnityLink? GetLink(string projectId)
        => _linksByProject.TryGetValue(projectId, out var link) ? link : null;

    public IReadOnlyCollection<UnityLink> Links => _linksByProject.Values.ToArray();

    public bool IsRegistered(string connectionId)
        => _linksByProject.Values.Any(l => l.Connected && l.ConnectionId == connectionId);

    public TaskCompletionSource<ToolResult> CreateOperation(string operationId)
    {
        var tcs = new TaskCompletionSource<ToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingOps.TryAdd(operationId, tcs))
            throw new InvalidOperationException($"Duplicate operationId '{operationId}'.");
        return tcs;
    }

    public bool CompleteOperation(ToolResult result)
        => _pendingOps.TryRemove(result.OperationId, out var tcs) && tcs.TrySetResult(result);

    public void AbandonOperation(string operationId)
        => _pendingOps.TryRemove(operationId, out _);

    public string[] AwaitedOperationIds => _pendingOps.Keys.ToArray();
}
