using ModelContextProtocol.Protocol;

namespace Feeder.Matrix.Backends;

/// <summary>Serves MCP tool traffic for one logical Unity project.</summary>
public interface IUnityBackend
{
    ValueTask<IReadOnlyList<Tool>> ListToolsAsync(CancellationToken ct);
    ValueTask<CallToolResult> CallToolAsync(CallToolRequestParams request, CancellationToken ct);
}
