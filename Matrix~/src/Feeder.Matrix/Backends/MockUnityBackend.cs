using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Feeder.Matrix.Backends;

/// <summary>
/// In-process backend used when no Unity editor is linked: matrix-only development,
/// integration tests, and the Phase 3 mock-ping soak. Mirrors the Unity plugin's `ping` contract.
/// </summary>
public sealed class MockUnityBackend : IUnityBackend
{
    private static readonly IReadOnlyList<Tool> Tools =
    [
        new Tool
        {
            Name = "ping",
            Description = "Echoes the input message back, or 'pong' when omitted.",
            InputSchema = JsonSerializer.Deserialize<JsonElement>(
                """{"type":"object","properties":{"message":{"type":"string"}},"additionalProperties":false}"""),
        },
    ];

    public ValueTask<IReadOnlyList<Tool>> ListToolsAsync(CancellationToken ct) => ValueTask.FromResult(Tools);

    public ValueTask<CallToolResult> CallToolAsync(CallToolRequestParams request, CancellationToken ct)
    {
        if (request.Name != "ping")
            throw new McpProtocolException($"Unknown tool: '{request.Name}'", McpErrorCode.InvalidParams);

        var message = "pong";
        if (request.Arguments is { } args &&
            args.TryGetValue("message", out var m) &&
            m.ValueKind == JsonValueKind.String)
        {
            message = m.GetString()!;
        }

        return ValueTask.FromResult(new CallToolResult
        {
            Content = [new TextContentBlock { Text = message }],
        });
    }
}
