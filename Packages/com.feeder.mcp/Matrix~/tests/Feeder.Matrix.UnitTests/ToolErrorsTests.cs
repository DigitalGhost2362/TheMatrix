using System.Text;
using System.Text.Json;
using Feeder.Matrix.Backends;
using Feeder.Matrix.Health;
using Feeder.Matrix.Protocol;
using Feeder.Matrix.Tools;
using Feeder.Matrix.Unity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace Feeder.Matrix.UnitTests;

public class ToolErrorsTests
{
    private static JsonElement Error(CallToolResult result) => result.StructuredContent!.Value.GetProperty("error");

    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content![0]).Text;

    [Theory]
    [InlineData(FmpErrorCodes.ToolFailed, "tool_failed", null)]
    [InlineData(FmpErrorCodes.Interrupted, "interrupted", ToolErrors.ReloadRetryMs)]
    [InlineData(FmpErrorCodes.QueueFull, "queue_full", ToolErrors.QueueFullRetryMs)]
    [InlineData(FmpErrorCodes.DeadlineExceeded, "timeout", ToolErrors.TimeoutRetryMs)]
    [InlineData(FmpErrorCodes.UnityUnavailable, "unity_unavailable", ToolErrors.TimeoutRetryMs)]
    [InlineData(FmpErrorCodes.ToolNotFound, "tool_not_found", null)]
    [InlineData(FmpErrorCodes.Cancelled, "tool_failed", null)]
    [InlineData(FmpErrorCodes.Internal, "tool_failed", null)]
    public void Fmp_code_maps_to_structured_code_and_retry(string fmp, string code, int? retry)
    {
        var result = UnityLinkBackend.ToCallToolResult("t", new ToolResult { OperationId = "o", Error = new FmpError { Code = fmp, Message = "boom" } });

        Assert.True(result.IsError);
        Assert.Equal(code, Error(result).GetProperty("code").GetString());
        Assert.Equal(retry, Error(result).TryGetProperty("retry_after_ms", out var ms) ? ms.GetInt32() : null);
        Assert.Contains("boom", Text(result));
    }

    [Fact]
    public void Fmp_error_with_unity_prefix_uses_that_code()
    {
        var result = ToolErrors.FromFmp(FmpErrorCodes.ToolFailed, "[tests_running retry_after_ms=5000] another run is active");

        Assert.Equal("tests_running", Error(result).GetProperty("code").GetString());
        Assert.Equal(5000, Error(result).GetProperty("retry_after_ms").GetInt32());
    }

    [Fact]
    public void Unity_error_result_gains_structured_error_and_keeps_content()
    {
        var json = Encoding.UTF8.GetBytes("""{"isError":true,"content":[{"type":"text","text":"[tests_running retry_after_ms=5000] A test run is active."}]}""");
        var result = UnityLinkBackend.ToCallToolResult("tests-run", new ToolResult { OperationId = "o", Success = true, ContentJsonUtf8 = json });

        Assert.Equal("[tests_running retry_after_ms=5000] A test run is active.", Text(result));
        Assert.Equal("tests_running", Error(result).GetProperty("code").GetString());
        Assert.Equal(5000, Error(result).GetProperty("retry_after_ms").GetInt32());
        Assert.Equal("A test run is active.", Error(result).GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("""{"content":[{"type":"text","text":"[tests_running] fine"}]}""")]
    [InlineData("""{"isError":true,"content":[{"type":"text","text":"plain failure"}]}""")]
    public void Unity_result_without_error_prefix_is_untouched(string json)
        => Assert.Null(UnityLinkBackend.ToCallToolResult("t", new ToolResult { OperationId = "o", Success = true, ContentJsonUtf8 = Encoding.UTF8.GetBytes(json) }).StructuredContent);

    [Theory]
    [InlineData("[code] message", true, "code", "message", null)]
    [InlineData("[tests_running retry_after_ms=250]   spaced", true, "tests_running", "spaced", 250)]
    [InlineData("[a1_b] x", true, "a1_b", "x", null)]
    [InlineData("no prefix", false, "tool_failed", "no prefix", null)]
    [InlineData("[Upper] x", false, "tool_failed", "[Upper] x", null)]
    [InlineData("[code retry_after_ms=abc] x", false, "tool_failed", "[code retry_after_ms=abc] x", null)]
    [InlineData("  [code] leading space", false, "tool_failed", "  [code] leading space", null)]
    [InlineData("", false, "tool_failed", "", null)]
    [InlineData(null, false, "tool_failed", "", null)]
    public void TryParseUnityError_edge_cases(string? text, bool ok, string code, string message, int? retry)
    {
        Assert.Equal(ok, ToolErrors.TryParseUnityError(text, out var c, out var m, out var r));
        Assert.Equal(code, c);
        Assert.Equal(message, m);
        Assert.Equal(retry, r);
    }

    private static UnityLinkBackend Backend(UnityLinkRegistry registry)
    {
        var options = Options.Create(new MatrixOptions());
        var health = new EditorHealth(registry, options, TimeProvider.System);
        return new UnityLinkBackend(registry, null!, health, options, NullLogger<UnityLinkBackend>.Instance);
    }

    private static UnityLink Register(UnityLinkRegistry registry, FmpEditorState state)
        => registry.Register(new UnityRegisterRequest
        {
            ProtocolVersion = "1", ProjectId = "p", UnityInstanceId = "i", ProjectPathHash = "h", UnityVersion = "6",
            PluginVersion = "1", SupportedProtocolVersions = ["1"], ToolRegistryHash = "x", AuthToken = "t",
            EditorState = state,
        }, "c");

    [Fact]
    public async Task No_editor_connected_returns_unity_unavailable_without_retry()
    {
        var result = await Backend(new UnityLinkRegistry()).CallToolAsync(new CallToolRequestParams { Name = "scene-open" }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("unity_unavailable", Error(result).GetProperty("code").GetString());
        Assert.False(Error(result).TryGetProperty("retry_after_ms", out _));
        Assert.Contains("Not linked", Text(result));
    }

    [Fact]
    public async Task Editor_gone_while_reloading_returns_compiling_or_reloading_with_retry()
    {
        var registry = new UnityLinkRegistry();
        var link = Register(registry, FmpEditorState.Reloading);
        link.Connected = false;
        link.LastSeen = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);

        var result = await Backend(registry).CallToolAsync(new CallToolRequestParams { Name = "scene-open" }, CancellationToken.None);

        Assert.Equal("compiling_or_reloading", Error(result).GetProperty("code").GetString());
        Assert.Equal(ToolErrors.ReloadRetryMs, Error(result).GetProperty("retry_after_ms").GetInt32());
    }

    [Fact]
    public async Task Cancellation_still_throws()
    {
        var registry = new UnityLinkRegistry();
        Register(registry, FmpEditorState.Ready).Connected = false;
        using var cts = new CancellationTokenSource(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Backend(registry).CallToolAsync(new CallToolRequestParams { Name = "scene-open" }, cts.Token).AsTask());
    }
}
