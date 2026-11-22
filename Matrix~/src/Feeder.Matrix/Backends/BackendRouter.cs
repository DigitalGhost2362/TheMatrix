using Feeder.Matrix.Unity;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace Feeder.Matrix.Backends;

/// <summary>
/// Picks the live Unity link when one is registered; otherwise falls back to the mock backend
/// when enabled (tests / matrix-only development), else fails fast with a clear MCP error.
/// </summary>
public sealed class BackendRouter(
    UnityLinkRegistry registry,
    UnityLinkBackend unityBackend,
    MockUnityBackend mockBackend,
    IOptions<MatrixOptions> options) : IUnityBackend
{
    // the Unity backend also answers when no editor is linked: an empty tool list, and calls that
    // wait out a reload or fail with the editor's health
    private IUnityBackend Resolve()
        => registry.GetLatestLink() is null && options.Value.EnableMockBackend ? mockBackend : unityBackend;

    public ValueTask<IReadOnlyList<Tool>> ListToolsAsync(CancellationToken ct) => Resolve().ListToolsAsync(ct);

    public ValueTask<CallToolResult> CallToolAsync(CallToolRequestParams request, CancellationToken ct)
        => Resolve().CallToolAsync(request, ct);
}
