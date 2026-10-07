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
        link.Registry = snapshot;
        logger.LogInformation("Registry updated for {ProjectId}: {ToolCount} tools, hash {Hash}",
            snapshot.ProjectId, snapshot.Tools.Length, snapshot.RegistryHash);
    }

    public void CompleteTool(ToolResult result)
    {
        if (!registry.CompleteOperation(result))
            logger.LogWarning("Tool result for unknown/expired operation {OperationId} dropped", result.OperationId);
    }

    public void ReportProgress(ToolProgress progress)
        => logger.LogDebug("Progress {OperationId}: {Progress} {Message}", progress.OperationId, progress.Progress, progress.Message);

    public void Heartbeat(HeartbeatMessage heartbeat)
    {
        foreach (var link in registry.Links)
        {
            if (link.UnityInstanceId == heartbeat.UnityInstanceId)
            {
                link.EditorState = heartbeat.EditorState;
                link.LastSeen = DateTimeOffset.UtcNow;
            }
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        registry.MarkDisconnected(Context.ConnectionId);
        logger.LogInformation(exception, "Unity link disconnected ({ConnectionId})", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
