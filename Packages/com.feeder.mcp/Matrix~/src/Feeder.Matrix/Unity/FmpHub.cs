using Feeder.Matrix.Compile;
using Feeder.Matrix.Protocol;
using Feeder.Matrix.Tools;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Feeder.Matrix.Unity;

/// <summary>Client (Unity adapter) callback methods invoked by the matrix.</summary>
public interface IFmpUnityClient
{
    Task InvokeTool(ToolInvoke invoke);
    Task CancelTool(string operationId);
    Task McpClientsChanged(McpClientInfo[] clients);
    Task EditorLockChanged(EditorLockState state);
    // only sent to links listing FmpCapabilities.AgentPlay; the editor exits play mode only if an agent started it
    Task StopAgentPlay(string reason);
}

/// <summary>FMP/1 hub — the Unity side of the matrix. WebSocket + MessagePack only.</summary>
public sealed class FmpHub(
    UnityLinkRegistry registry,
    McpClientTracker mcpClients,
    ScriptCompiler compiler,
    EditorLock editorLock,
    IOptions<MatrixOptions> options,
    ILogger<FmpHub> logger) : Hub<IFmpUnityClient>
{
    private UnityRegisterResponse? Reject(UnityRegisterRequest request)
    {
        if (request.ProtocolVersion != FmpConstants.ProtocolVersion &&
            !request.SupportedProtocolVersions.Contains(FmpConstants.ProtocolVersion))
        {
            return new UnityRegisterResponse
            {
                SessionId = string.Empty,
                ChosenProtocolVersion = FmpConstants.ProtocolVersion,
                Error = new FmpError
                {
                    Code = FmpErrorCodes.VersionUnsupported,
                    Message = $"Matrix speaks FMP/{FmpConstants.ProtocolVersion}; Unity offered [{string.Join(", ", request.SupportedProtocolVersions)}].",
                },
            };
        }

        if (options.Value.RequireAuth &&
            !MatrixAuthentication.FixedTimeEquals(request.AuthToken, options.Value.Token))
        {
            return new UnityRegisterResponse
            {
                SessionId = string.Empty,
                ChosenProtocolVersion = FmpConstants.ProtocolVersion,
                Error = new FmpError
                {
                    Code = FmpErrorCodes.AuthFailed,
                    Message = "Unity matrix authentication failed.",
                },
            };
        }

        return null;
    }

    public UnityRegisterResponse Register(UnityRegisterRequest request)
    {
        if (Reject(request) is { } rejected)
            return rejected;

        var link = registry.Register(request, Context.ConnectionId);
        logger.LogInformation(
            "Unity registered: project {ProjectId}, instance {InstanceId}, editor {EditorState}, plugin {PluginVersion}, unity {UnityVersion}",
            request.ProjectId, request.UnityInstanceId, request.EditorState, request.PluginVersion, request.UnityVersion);

        FailInterruptedOperations(link, request);
        if (link.ListedAsGone)
        {
            link.ListedAsGone = false;
            _ = mcpClients.NotifyToolListChangedAsync();
        }

        // Seed the freshly-linked editor with the current MCP client set so its "AI agent"
        // indicator is correct immediately after a domain reload, not only on the next change.
        _ = Clients.Caller.McpClientsChanged(mcpClients.Snapshot());
        _ = editorLock.PublishAsync(link);

        return new UnityRegisterResponse
        {
            SessionId = link.SessionId,
            ChosenProtocolVersion = FmpConstants.ProtocolVersion,
            RegistryUpToDate = link.Registry?.RegistryHash == request.ToolRegistryHash,
            AwaitedOperations = registry.AwaitedOperationIds,
            Capabilities = [FmpCapabilities.Compile, FmpCapabilities.EditorLock, FmpCapabilities.EditorLockQueue, FmpCapabilities.EditorLockAgents, FmpCapabilities.PlayerLink],
        };
    }

    /// <summary>Links a built player beside the editor. An older matrix has no such method, so a player never replaces its editor there.</summary>
    public UnityRegisterResponse RegisterPlayer(UnityRegisterRequest request)
    {
        if (Reject(request) is { } rejected)
            return rejected;

        var link = registry.RegisterPlayer(request, Context.ConnectionId);
        logger.LogInformation("Player linked: project {ProjectId}, instance {InstanceId}, plugin {PluginVersion}, unity {UnityVersion}",
            request.ProjectId, request.UnityInstanceId, request.PluginVersion, request.UnityVersion);
        return new UnityRegisterResponse
        {
            SessionId = link.SessionId,
            ChosenProtocolVersion = FmpConstants.ProtocolVersion,
            Capabilities = [FmpCapabilities.PlayerLink],
        };
    }

    public Task<CompileResponse> Compile(CompileRequest request)
        => registry.IsRegistered(Context.ConnectionId)
            ? compiler.CompileAsync(request, Context.ConnectionAborted)
            : Task.FromResult(new CompileResponse { Error = "Compile is only served to a registered Unity link; call Register first." });

    /// <summary>Frees the editor lock, whoever holds it. Returns what was freed.</summary>
    public string ReleaseEditorLock()
    {
        if (!registry.IsRegistered(Context.ConnectionId))
            return "nothing";
        var freed = editorLock.ForceRelease();
        logger.LogInformation("Editor lock released from the Unity editor: {Freed}", freed);
        return freed;
    }

    /// <summary>Takes one session out of the editor lock queue. Returns who was removed.</summary>
    public string RemoveFromEditorLockQueue(string ticket)
        => registry.IsRegistered(Context.ConnectionId) ? editorLock.RemoveWaiter(ticket) : "nothing";

    /// <summary>Lets one queued agent stop waiting for the agents it named with 'after'. Returns who.</summary>
    public string SkipEditorLockAfter(string ticket)
        => registry.IsRegistered(Context.ConnectionId) ? editorLock.SkipAfter(ticket) : "nothing";

    /// <summary>Pauses or resumes the queue. Returns the state it is in now.</summary>
    public bool SetEditorLockQueuePaused(bool paused)
        => registry.IsRegistered(Context.ConnectionId) ? editorLock.SetQueuePaused(paused) : false;

    public void Unregister(string projectId, string unityInstanceId)
    {
        registry.Unregister(projectId, unityInstanceId);
        _ = mcpClients.NotifyToolListChangedAsync();
        logger.LogInformation("Unity unregistered: project {ProjectId}, instance {InstanceId}", projectId, unityInstanceId);
    }

    public void PublishRegistry(RegistrySnapshot snapshot)
    {
        var link = registry.GetPlayerByConnection(Context.ConnectionId) ?? registry.GetLink(snapshot.ProjectId);
        if (link is null)
        {
            logger.LogWarning("Registry snapshot for unknown project {ProjectId} dropped", snapshot.ProjectId);
            return;
        }
        var changed = link.Registry?.RegistryHash != snapshot.RegistryHash;
        link.Registry = snapshot;
        if (changed)
            _ = mcpClients.NotifyToolListChangedAsync();
        logger.LogInformation("Registry updated for {ProjectId}: {ToolCount} tools, hash {Hash}",
            snapshot.ProjectId, snapshot.Tools.Length, snapshot.RegistryHash);
    }

    public void CompleteTool(ToolResult result)
    {
        if (!registry.CompleteOperation(result))
            logger.LogWarning("Tool result for unknown/expired operation {OperationId} dropped", result.OperationId);
    }

    public void ReportProgress(ToolProgress progress)
    {
        logger.LogDebug("Progress {OperationId}: {Progress} {Message} deferred={Deferred} extend={ExtendMs}",
            progress.OperationId, progress.Progress, progress.Message, progress.Deferred, progress.ExtendDeadlineMs);
        var operation = registry.GetOperation(progress.OperationId);
        if (operation is null)
            return;

        var limit = operation.Started + options.Value.MaxToolTimeout;
        if (progress.Deferred)
        {
            operation.Deferred = true;
            operation.ExtendTo(Min(operation.Started + options.Value.DeferredToolTimeout, limit));
        }
        if (progress.ExtendDeadlineMs > 0)
            operation.ExtendTo(Min(DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(progress.ExtendDeadlineMs), limit));
    }

    public void Heartbeat(HeartbeatMessage heartbeat)
    {
        foreach (var link in registry.Links)
        {
            if (link.UnityInstanceId == heartbeat.UnityInstanceId)
            {
                var now = DateTimeOffset.UtcNow;
                link.EditorState = heartbeat.EditorState;
                link.LastSeen = now;
                link.Heartbeat = heartbeat;
                link.HeartbeatAt = now;
                editorLock.ObserveEditor(link);
            }
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        var wasPlayer = registry.GetPlayerByConnection(Context.ConnectionId) is not null;
        registry.MarkDisconnected(Context.ConnectionId);
        logger.LogInformation(exception, "Unity link disconnected ({ConnectionId})", Context.ConnectionId);
        if (wasPlayer)
            _ = mcpClients.NotifyToolListChangedAsync();
        foreach (var link in registry.Links.Where(l => l.ConnectionId == Context.ConnectionId))
            _ = AnnounceIfGoneAsync(link, link.LastSeen);
        return base.OnDisconnectedAsync(exception);
    }

    // a link that stays down past the grace drops its Unity tools from tools/list; tell the clients once
    private async Task AnnounceIfGoneAsync(UnityLink link, DateTimeOffset seen)
    {
        await Task.Delay(options.Value.ReconnectGrace + TimeSpan.FromSeconds(1));
        if (link.Connected || link.LastSeen != seen || link.ListedAsGone)
            return;
        link.ListedAsGone = true;
        await mcpClients.NotifyToolListChangedAsync();
    }

    // Operations the editor lost in a domain reload never complete, so answer them now instead of at
    // their deadline. Only plugins that report deferral (they send ProjectRoot) are judged this way.
    private void FailInterruptedOperations(UnityLink link, UnityRegisterRequest request)
    {
        if (string.IsNullOrEmpty(request.ProjectRoot))
            return;
        foreach (var operation in registry.Operations)
        {
            if (operation.ProjectId != link.ProjectId || operation.Deferred ||
                operation.SentOnConnectionId is null || operation.SentOnConnectionId == Context.ConnectionId ||
                request.PendingOperations.Contains(operation.OperationId))
                continue;
            registry.CompleteOperation(new ToolResult
            {
                OperationId = operation.OperationId,
                Success = false,
                Error = new FmpError
                {
                    Code = FmpErrorCodes.Interrupted,
                    Message = $"Tool '{operation.ToolName}' was cut off by a Unity domain reload before it finished. " +
                              "Its code may have run in part: check the editor state before running it again.",
                },
            });
            logger.LogInformation("Operation {OperationId} ({Tool}) interrupted by a domain reload", operation.OperationId, operation.ToolName);
        }
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
