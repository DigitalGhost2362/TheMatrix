using Feeder.Matrix.Compile;
using Feeder.Matrix.Protocol;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Feeder.Matrix.Unity;

/// <summary>Client (Unity adapter) callback methods invoked by the matrix.</summary>
public interface IFmpUnityClient
{
    Task InvokeTool(ToolInvoke invoke);
    Task CancelTool(string operationId);
    Task McpClientsChanged(McpClientInfo[] clients);
}

/// <summary>FMP/1 hub — the Unity side of the matrix. WebSocket + MessagePack only.</summary>
public sealed class FmpHub(
    UnityLinkRegistry registry,
    McpClientTracker mcpClients,
    ScriptCompiler compiler,
    IOptions<MatrixOptions> options,
    ILogger<FmpHub> logger) : Hub<IFmpUnityClient>
{
    public UnityRegisterResponse Register(UnityRegisterRequest request)
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

        return new UnityRegisterResponse
        {
            SessionId = link.SessionId,
            ChosenProtocolVersion = FmpConstants.ProtocolVersion,
            RegistryUpToDate = link.Registry?.RegistryHash == request.ToolRegistryHash,
            AwaitedOperations = registry.AwaitedOperationIds,
            Capabilities = [FmpCapabilities.Compile],
        };
    }

    public Task<CompileResponse> Compile(CompileRequest request)
        => registry.IsRegistered(Context.ConnectionId)
            ? compiler.CompileAsync(request, Context.ConnectionAborted)
            : Task.FromResult(new CompileResponse { Error = "Compile is only served to a registered Unity link; call Register first." });

    public void Unregister(string projectId, string unityInstanceId)
    {
        registry.Unregister(projectId, unityInstanceId);
        _ = mcpClients.NotifyToolListChangedAsync();
        logger.LogInformation("Unity unregistered: project {ProjectId}, instance {InstanceId}", projectId, unityInstanceId);
    }

    public void PublishRegistry(RegistrySnapshot snapshot)
    {
        var link = registry.GetLink(snapshot.ProjectId);
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
            }
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        registry.MarkDisconnected(Context.ConnectionId);
        logger.LogInformation(exception, "Unity link disconnected ({ConnectionId})", Context.ConnectionId);
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
