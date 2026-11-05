using System.Text.Json;
using Feeder.Matrix.Backends;
using Feeder.Matrix.Tools;
using Feeder.Matrix.Unity;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Feeder.Matrix.UnitTests;

public sealed class MatrixResourcesTests
{
    private readonly MatrixResources _resources = new(new UnityLinkRegistry());
    private readonly FakeBackend _backend = new();

    [Fact]
    public void Lists_the_editor_resources_and_two_templates()
    {
        var uris = _resources.List().Resources.Select(r => r.Uri).ToArray();

        Assert.Equal(["matrix://editor/state", "matrix://editor/selection", "matrix://editor/scenes", "matrix://console/errors"], uris);
        Assert.Equal(["matrix://rql/{query}", "matrix://console/{type}/{count}"], _resources.Templates().ResourceTemplates.Select(t => t.UriTemplate).ToArray());
    }

    [Fact]
    public async Task State_is_read_without_a_unity_call()
    {
        var result = await _resources.ReadAsync("matrix://editor/state", _backend, default);

        var text = ((TextResourceContents)result.Contents[0]).Text;
        using var json = JsonDocument.Parse(text);
        Assert.False(json.RootElement.GetProperty("linked").GetBoolean());
        Assert.Empty(_backend.Queries);
    }

    [Theory]
    [InlineData("matrix://editor/selection", "selection | select name, $path")]
    [InlineData("matrix://editor/scenes", "scenes")]
    [InlineData("matrix://rql/objects%20%7C%20count", "objects | count")]
    [InlineData("matrix://console/Warning/5", "logs | where type == \"Warning\" | take 5")]
    [InlineData("matrix://console/error", "logs | where type == \"Error\" | take 30")]
    public async Task Resource_forwards_one_read_only_query_to_unity(string uri, string query)
    {
        var result = await _resources.ReadAsync(uri, _backend, default);

        Assert.Equal(query, Assert.Single(_backend.Queries));
        Assert.Equal("rows", ((TextResourceContents)result.Contents[0]).Text);
    }

    [Theory]
    [InlineData("matrix://nope")]
    [InlineData("file:///etc/passwd")]
    [InlineData("matrix://console/Error/0")]
    [InlineData("matrix://console/Error/9999")]
    [InlineData("matrix://console/Error\" | take 1 | x/5")]
    public async Task Unknown_resources_are_refused_with_invalid_params(string uri)
    {
        var e = await Assert.ThrowsAsync<McpProtocolException>(async () => await _resources.ReadAsync(uri, _backend, default));

        Assert.Equal(McpErrorCode.InvalidParams, e.ErrorCode);
        Assert.Empty(_backend.Queries);
    }

    [Fact]
    public async Task A_failing_query_becomes_an_internal_error_with_the_reason()
    {
        _backend.Fail = true;

        var e = await Assert.ThrowsAsync<McpProtocolException>(async () => await _resources.ReadAsync("matrix://editor/scenes", _backend, default));

        Assert.Equal(McpErrorCode.InternalError, e.ErrorCode);
        Assert.Contains("bad source", e.Message);
    }

    private sealed class FakeBackend : IUnityBackend
    {
        public List<string> Queries { get; } = [];
        public bool Fail { get; set; }

        public ValueTask<IReadOnlyList<Tool>> ListToolsAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<Tool>>([]);

        public ValueTask<CallToolResult> CallToolAsync(CallToolRequestParams request, CancellationToken ct)
        {
            Assert.Equal(MatrixResources.QueryTool, request.Name);
            Queries.Add(request.Arguments!["query"].GetString()!);
            return ValueTask.FromResult(new CallToolResult
            {
                IsError = Fail,
                Content = [new TextContentBlock { Text = Fail ? "bad source" : "rows" }],
            });
        }
    }
}
