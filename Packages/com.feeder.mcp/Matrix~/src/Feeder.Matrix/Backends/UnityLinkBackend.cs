using System.Text.Json;
using Feeder.Matrix.Health;
using Feeder.Matrix.Protocol;
using Feeder.Matrix.Unity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Feeder.Matrix.Backends;

/// <summary>Routes MCP tool traffic to the linked Unity editor over the FMP hub.</summary>
public sealed class UnityLinkBackend(
    UnityLinkRegistry registry,
    IHubContext<FmpHub, IFmpUnityClient> hub,
    EditorHealth health,
    IOptions<MatrixOptions> options,
    ILogger<UnityLinkBackend> logger) : IUnityBackend
{
    private static readonly TimeSpan Slice = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LinkPoll = TimeSpan.FromMilliseconds(250);

    /// <summary>Unity's tools while a link is up or briefly down (a reload keeps its tools listed); empty otherwise.</summary>
    public ValueTask<IReadOnlyList<Tool>> ListToolsAsync(CancellationToken ct)
    {
        var link = registry.GetLatestLink();
        if (link?.Registry is not { } snapshot || IsGone(link))
            return ValueTask.FromResult<IReadOnlyList<Tool>>([]);

        // Registry-hash-keyed conversion cache lands in Phase 6; snapshot conversion is already reflection-free.
        IReadOnlyList<Tool> tools = snapshot.Tools.Select(t => new Tool
        {
            Name = t.Name,
            Title = t.Title,
            Description = t.Description,
            InputSchema = JsonSerializer.Deserialize<JsonElement>(t.InputSchemaJsonUtf8),
        }).ToArray();
        return ValueTask.FromResult(tools);
    }

    public async ValueTask<CallToolResult> CallToolAsync(CallToolRequestParams request, CancellationToken ct)
    {
        var settings = options.Value;
        var link = await WaitForLinkAsync(request.Name, ct);
        var connectionId = link.ConnectionId;
        var operationId = Guid.NewGuid().ToString("N");

        var argsJson = request.Arguments is null
            ? "{}"u8.ToArray()
            : JsonSerializer.SerializeToUtf8Bytes(request.Arguments);

        var operation = registry.CreateOperation(operationId, request.Name, link.ProjectId, settings.DefaultToolTimeout);
        operation.SentOnConnectionId = connectionId;
        try
        {
            await hub.Clients.Client(connectionId).InvokeTool(new ToolInvoke
            {
                OperationId = operationId,
                ToolName = request.Name,
                ArgumentsJsonUtf8 = argsJson,
                DeadlineUnixMs = operation.Deadline.ToUnixTimeMilliseconds(),
            });

            var hardStop = operation.Started + settings.MaxToolTimeout;
            var down = TimeSpan.Zero;
            var lastCheck = DateTimeOffset.UtcNow;
            while (true)
            {
                try
                {
                    var result = await operation.Completion.Task.WaitAsync(Slice, ct);
                    return ToCallToolResult(request.Name, result);
                }
                catch (TimeoutException)
                {
                }
                catch (OperationCanceledException)
                {
                    await TryCancelAsync(registry.GetLink(link.ProjectId) ?? link, operationId);
                    throw;
                }

                var now = DateTimeOffset.UtcNow;
                var elapsed = now - lastCheck;
                lastCheck = now;
                var current = registry.GetLink(link.ProjectId);
                if (current is null)
                    throw new McpProtocolException($"Tool '{request.Name}' lost its Unity editor: the editor unlinked from the matrix. {health.Summary()}", McpErrorCode.InternalError);

                if (!current.Connected)
                {
                    // time spent reloading does not count against the call, up to the reconnect grace
                    down += elapsed;
                    operation.ExtendTo(operation.Deadline + elapsed);
                    if (down > settings.ReconnectGrace || EditorHealth.ProcessExited(current))
                        throw new McpProtocolException(
                            $"Tool '{request.Name}' lost its Unity editor: it did not come back within {EditorHealth.Seconds(settings.ReconnectGrace)}. {health.Summary()}",
                            McpErrorCode.InternalError);
                    continue;
                }

                if (now > operation.Deadline || now > hardStop)
                {
                    await TryCancelAsync(current, operationId);
                    throw new McpProtocolException(
                        $"Tool '{request.Name}' timed out after {EditorHealth.Seconds(now - operation.Started)}. Editor health: {health.Summary()}",
                        McpErrorCode.InternalError);
                }
            }
        }
        finally
        {
            registry.AbandonOperation(operationId);
        }
    }

    // A link that dropped (domain reload, editor restart) gets the reconnect grace to come back
    // before the call fails; a project that never linked fails at once.
    private async Task<UnityLink> WaitForLinkAsync(string toolName, CancellationToken ct)
    {
        while (true)
        {
            if (registry.GetDefaultLink() is { } link)
                return link;

            var latest = registry.GetLatestLink();
            if (latest is null || IsGone(latest))
                throw new McpProtocolException($"No Unity editor is connected to the matrix, so '{toolName}' cannot run. {health.Summary()}", McpErrorCode.InternalError);

            await Task.Delay(LinkPoll, ct);
        }
    }

    private bool IsGone(UnityLink link)
        => !link.Connected &&
           (DateTimeOffset.UtcNow - link.LastSeen > options.Value.ReconnectGrace || EditorHealth.ProcessExited(link));

    private async Task TryCancelAsync(UnityLink link, string operationId)
    {
        try
        {
            await hub.Clients.Client(link.ConnectionId).CancelTool(operationId);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to send cancel for operation {OperationId}", operationId);
        }
    }

    private static CallToolResult ToCallToolResult(string toolName, ToolResult result)
    {
        if (!result.Success)
        {
            var error = result.Error;
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = error is null ? $"Tool '{toolName}' failed." : $"[{error.Code}] {error.Message}" }],
            };
        }

        if (result.ContentJsonUtf8 is { Length: > 0 } json)
        {
            // Unity sends a full CallToolResult-shaped JSON payload (content blocks + structuredContent).
            var parsed = JsonSerializer.Deserialize<CallToolResult>(json, McpJsonUtilities.DefaultOptions);
            if (parsed is not null)
                return parsed;
        }

        return new CallToolResult { Content = [new TextContentBlock { Text = string.Empty }] };
    }
}
