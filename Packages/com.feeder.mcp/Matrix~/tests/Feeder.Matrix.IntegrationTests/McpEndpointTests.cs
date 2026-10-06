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
}
