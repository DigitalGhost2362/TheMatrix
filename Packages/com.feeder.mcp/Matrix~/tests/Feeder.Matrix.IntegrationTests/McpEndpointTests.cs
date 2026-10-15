using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Feeder.Matrix.IntegrationTests;

public sealed class MatrixFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("FeederMatrix:EnableMockBackend", "true");
    }
}

public class McpEndpointTests : IClassFixture<MatrixFactory>
{
    private readonly MatrixFactory _factory;

    public McpEndpointTests(MatrixFactory factory) => _factory = factory;

    private static async Task<(JsonDocument doc, string? sessionId)> PostAsync(
        HttpClient client, string body, string? sessionId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (sessionId is not null)
            request.Headers.Add("Mcp-Session-Id", sessionId);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var newSessionId = response.Headers.TryGetValues("Mcp-Session-Id", out var v) ? v.FirstOrDefault() : sessionId;
        var text = await response.Content.ReadAsStringAsync();
        var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? text.Split('\n').Last(l => l.StartsWith("data:"))["data:".Length..].Trim()
            : text;
        return (JsonDocument.Parse(json), newSessionId);
    }

    private async Task<(HttpClient client, string sessionId)> InitializeAsync()
    {
        var client = _factory.CreateClient();
        var (doc, sessionId) = await PostAsync(client,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"tests","version":"0"}}}""");
        using (doc)
        {
            Assert.Equal("feeder-mcp-server", doc.RootElement.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
        }
        return (client, sessionId!);
    }

    [Fact]
    public async Task Initialize_negotiates_protocol_and_capabilities()
    {
        var client = _factory.CreateClient();
        var (doc, sessionId) = await PostAsync(client,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"tests","version":"0"}}}""");
        using var d = doc;

        Assert.NotNull(sessionId);
        var result = doc.RootElement.GetProperty("result");
        Assert.Equal("2025-06-18", result.GetProperty("protocolVersion").GetString());
        Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
        Assert.True(result.GetProperty("capabilities").TryGetProperty("prompts", out _));
        Assert.True(result.GetProperty("capabilities").TryGetProperty("resources", out _));
    }

    [Fact]
    public async Task ToolsList_returns_mock_ping_tool()
    {
        var (client, sessionId) = await InitializeAsync();
        var (doc, _) = await PostAsync(client, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""", sessionId);
        using var _d = doc;

        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        Assert.Contains("ping", tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task ToolsCall_ping_echoes_message()
    {
        var (client, sessionId) = await InitializeAsync();
        var (doc, _) = await PostAsync(client,
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"ping","arguments":{"message":"hello"}}}""",
            sessionId);
        using var _d = doc;

        var content = doc.RootElement.GetProperty("result").GetProperty("content");
        Assert.Equal("hello", content[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task ToolsCall_unknown_tool_returns_jsonrpc_error()
    {
        var (client, sessionId) = await InitializeAsync();
        var (doc, _) = await PostAsync(client,
            """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"no-such-tool","arguments":{}}}""",
            sessionId);
        using var _d = doc;

        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("no-such-tool", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PromptsList_and_ResourcesList_return_empty()
    {
        var (client, sessionId) = await InitializeAsync();

        var (prompts, _) = await PostAsync(client, """{"jsonrpc":"2.0","id":5,"method":"prompts/list"}""", sessionId);
        using (prompts)
            Assert.Empty(prompts.RootElement.GetProperty("result").GetProperty("prompts").EnumerateArray());

        var (resources, _) = await PostAsync(client, """{"jsonrpc":"2.0","id":6,"method":"resources/list"}""", sessionId);
        using (resources)
            Assert.Empty(resources.RootElement.GetProperty("result").GetProperty("resources").EnumerateArray());
    }

    [Fact]
    public async Task Ping_method_answers()
    {
        var (client, sessionId) = await InitializeAsync();
        var (doc, _) = await PostAsync(client, """{"jsonrpc":"2.0","id":7,"method":"ping"}""", sessionId);
        using var _d = doc;
        Assert.True(doc.RootElement.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task NonLoopback_host_header_is_rejected()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/healthz");
        request.Headers.Host = "evil.example.com";
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Healthz_reports_ok_without_unity()
    {
        var client = _factory.CreateClient();
        var json = await client.GetStringAsync("/healthz");
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.Empty(doc.RootElement.GetProperty("unityLinks").EnumerateArray());
    }

    [Fact]
    public async Task Healthz_keys_mcp_clients_by_session_header()
    {
        var (client, sessionId) = await InitializeAsync();
        var json = await client.GetStringAsync("/healthz");
        using var doc = JsonDocument.Parse(json);
        var session = doc.RootElement.GetProperty("mcpClients").EnumerateArray()
            .Single(c => c.GetProperty("sessionId").GetString() == sessionId);
        Assert.True(session.GetProperty("isActive").GetBoolean());
        Assert.True(session.TryGetProperty("lastActivity", out _));
    }
    [Fact]
    public async Task ToolsList_has_matrix_tools_and_list_changed_capability()
    {
        var (client, sessionId) = await InitializeAsync();
        var (doc, _) = await PostAsync(client, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""", sessionId);
        using var _d = doc;
        var names = doc.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()).ToArray();
        Assert.Contains("editor-health", names);
        Assert.Contains("tool-call", names);

        var (init, _) = await PostAsync(_factory.CreateClient(),
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"tests","version":"0"}}}""");
        using (init)
            Assert.True(init.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("tools").GetProperty("listChanged").GetBoolean());
    }

    [Fact]
    public async Task EditorHealth_answers_without_unity()
    {
        var (client, sessionId) = await InitializeAsync();
        var (doc, _) = await PostAsync(client,
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"editor-health","arguments":{}}}""", sessionId);
        using var _d = doc;
        Assert.StartsWith("Not linked", doc.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("""{"name":"tool-call"}""", "matrix tool")]
    [InlineData("""{"name":" EDITOR-HEALTH "}""", "matrix tool")]
    [InlineData("""{"name":"mcp__Feeder-MCP__tool-call"}""", "matrix tool")]
    [InlineData("""{"name":"ping","arguments":[1]}""", "JSON object")]
    [InlineData("""{"name":42}""", "as a string")]
    [InlineData("""{}""", "as a string")]
    public async Task ToolCall_refuses_bad_requests(string arguments, string expected)
    {
        var (client, sessionId) = await InitializeAsync();
        var (doc, _) = await PostAsync(client,
            $$$"""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"tool-call","arguments":{{{arguments}}}}}""", sessionId);
        using var _d = doc;
        var result = doc.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains(expected, result.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task ToolCall_serves_only_tools_the_session_has_not_listed()
    {
        var (client, sessionId) = await InitializeAsync();
        var (gap, _) = await PostAsync(client,
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"tool-call","arguments":{"name":"ping","arguments":{"message":"hi"}}}}""", sessionId);
        using (gap)
        {
            var content = gap.RootElement.GetProperty("result").GetProperty("content");
            Assert.Equal("via tool-call -> ping", content[0].GetProperty("text").GetString());
            Assert.Equal("hi", content[1].GetProperty("text").GetString());
        }

        (await PostAsync(client, """{"jsonrpc":"2.0","id":4,"method":"tools/list"}""", sessionId)).doc.Dispose();
        var (listed, _) = await PostAsync(client,
            """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"tool-call","arguments":{"name":"PING"}}}""", sessionId);
        using (listed)
            Assert.Contains("already in your tool list", listed.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString());
    }
}
