using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Feeder.Matrix.IntegrationTests;

public sealed class EditorLockFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("FeederMatrix:EnableMockBackend", "true");
        builder.UseSetting("FeederMatrix:ImplicitLock", "false");
    }
}

public sealed class ImplicitLockFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("FeederMatrix:EnableMockBackend", "true");
        builder.UseSetting("FeederMatrix:EditorLockWaitSlice", "00:00:01");
    }
}

public class EditorLockEndpointTests(EditorLockFactory factory) : IClassFixture<EditorLockFactory>
{
    [Fact]
    public async Task Claim_of_one_session_refuses_the_other_until_released()
    {
        var a = await InitializeAsync("claude-code");
        var b = await InitializeAsync("codex");

        var names = (await RpcAsync(a, "tools/list", "{}")).GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()).ToArray();
        Assert.Contains("editor-lock-acquire", names);
        Assert.Contains("editor-lock-release", names);
        Assert.Contains("editor-lock-status", names);

        var acquired = await CallAsync(a, "editor-lock-acquire", """{"owner":"claude-catwalk","task":"catwalk score"}""");
        Assert.StartsWith("acquired", Text(acquired));

        var refused = await CallAsync(b, "ping", "{}");
        Assert.True(refused.GetProperty("isError").GetBoolean());
        Assert.Contains("held by claude-catwalk", Text(refused));

        var proxied = await CallAsync(b, "tool-call", """{"name":"ping"}""");
        Assert.Contains("held by claude-catwalk", string.Join("\n", proxied.GetProperty("content").EnumerateArray().Select(c => c.GetProperty("text").GetString())));

        Assert.Equal("pong", Text(await CallAsync(a, "ping", "{}")));
        Assert.StartsWith("held by claude-catwalk (claude-code", Text(await CallAsync(b, "editor-lock-status", "{}")));

        Assert.StartsWith("released", Text(await CallAsync(a, "editor-lock-release", "{}")));
        Assert.Equal("pong", Text(await CallAsync(b, "ping", "{}")));
    }

    [Fact]
    public async Task Hook_tells_a_subagent_from_the_main_agent_of_its_session()
    {
        var a = await InitializeAsync("claude-code");
        await HookAsync(a, """{"hook_event_name":"PreToolUse","session_id":"claude-1","tool_use_id":"use-main","tool_name":"mcp__Feeder-MCP__editor-lock-acquire"}""");
        Assert.StartsWith("acquired", Text(await CallAsync(a, "editor-lock-acquire", """{"owner":"main","task":"compile"}""", "use-main")));

        await HookAsync(a, """{"hook_event_name":"PreToolUse","session_id":"claude-1","agent_id":"agent-1","agent_type":"playtester","tool_use_id":"use-sub","tool_name":"mcp__Feeder-MCP__ping"}""");
        var refused = await CallAsync(a, "ping", "{}", "use-sub");
        Assert.True(refused.GetProperty("isError").GetBoolean());
        Assert.Contains("held by main", Text(refused));

        // a call no hook announced belongs to the session, as without the hook
        Assert.Equal("pong", Text(await CallAsync(a, "ping", "{}")));
        Assert.StartsWith("released", Text(await CallAsync(a, "editor-lock-release", "{}")));
    }

    private static async Task HookAsync(Session session, string body)
    {
        var response = await session.Client.PostAsync("/hooks/claude", new StringContent(body, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    private Task<JsonElement> CallAsync(Session session, string name, string arguments, string toolUseId)
        => RpcAsync(session, "tools/call", $"{{\"name\":\"{name}\",\"arguments\":{arguments},\"_meta\":{{\"claudecode/toolUseId\":\"{toolUseId}\"}}}}");

    private static string Text(JsonElement result) => result.GetProperty("content")[0].GetProperty("text").GetString()!;

    private sealed record Session(HttpClient Client, string Id);

    private int _id;

    private async Task<Session> InitializeAsync(string clientName)
    {
        var client = factory.CreateClient();
        var (_, sessionId) = await PostAsync(client,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"CLIENT","version":"1"}}}""".Replace("CLIENT", clientName), null);
        return new Session(client, sessionId!);
    }

    private Task<JsonElement> CallAsync(Session session, string name, string arguments)
        => RpcAsync(session, "tools/call", $"{{\"name\":\"{name}\",\"arguments\":{arguments}}}");

    private async Task<JsonElement> RpcAsync(Session session, string method, string parameters)
    {
        var (json, _) = await PostAsync(session.Client,
            $"{{\"jsonrpc\":\"2.0\",\"id\":{Interlocked.Increment(ref _id) + 1},\"method\":\"{method}\",\"params\":{parameters}}}", session.Id);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("result").Clone();
    }

    private static async Task<(string json, string? sessionId)> PostAsync(HttpClient client, string body, string? sessionId)
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
        var newSessionId = response.Headers.TryGetValues("Mcp-Session-Id", out var values) ? values.FirstOrDefault() : sessionId;
        var text = await response.Content.ReadAsStringAsync();
        var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? text.Split('\n').Last(l => l.StartsWith("data:"))["data:".Length..].Trim()
            : text;
        return (json, newSessionId);
    }
}

public class ImplicitLockEndpointTests(ImplicitLockFactory factory) : IClassFixture<ImplicitLockFactory>
{
    [Fact]
    public async Task Editor_changing_call_of_a_session_without_a_lock_claims_it_and_makes_the_others_queue()
    {
        var a = await InitializeAsync("claude-code");
        var b = await InitializeAsync("codex");

        // free editor: the call runs and leaves an implicit claim behind
        Assert.Equal("pong", Text(await CallAsync(a, "ping", "{}")));
        Assert.Contains("ping (implicit)", Text(await CallAsync(b, "editor-lock-status", "{}")));

        // the other session waits one slice, then gets the structured queued answer, for the tool and for tool-call
        var queued = await CallAsync(b, "ping", "{}");
        Assert.True(queued.GetProperty("isError").GetBoolean());
        Assert.Contains("queued: position 1", Text(queued));
        Assert.Contains("The tool did not run", Text(queued));
        var proxied = await CallAsync(b, "tool-call", """{"name":"ping"}""");
        Assert.Contains("queued: position 1", string.Join("\n", proxied.GetProperty("content").EnumerateArray().Select(c => c.GetProperty("text").GetString())));

        // the holder keeps working; an explicit release hands the editor over
        Assert.Equal("pong", Text(await CallAsync(a, "ping", "{}")));
        Assert.StartsWith("released", Text(await CallAsync(a, "editor-lock-release", "{}")));
        Assert.Equal("pong", Text(await CallAsync(b, "ping", "{}")));
    }

    private static string Text(JsonElement result) => result.GetProperty("content")[0].GetProperty("text").GetString()!;

    private sealed record Session(HttpClient Client, string Id);

    private int _id;

    private async Task<Session> InitializeAsync(string clientName)
    {
        var client = factory.CreateClient();
        var (_, sessionId) = await PostAsync(client,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"CLIENT","version":"1"}}}""".Replace("CLIENT", clientName), null);
        return new Session(client, sessionId!);
    }

    private async Task<JsonElement> CallAsync(Session session, string name, string arguments)
    {
        var (json, _) = await PostAsync(session.Client,
            $"{{\"jsonrpc\":\"2.0\",\"id\":{Interlocked.Increment(ref _id) + 1},\"method\":\"tools/call\",\"params\":{{\"name\":\"{name}\",\"arguments\":{arguments}}}}}", session.Id);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("result").Clone();
    }

    private static async Task<(string json, string? sessionId)> PostAsync(HttpClient client, string body, string? sessionId)
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
        var newSessionId = response.Headers.TryGetValues("Mcp-Session-Id", out var values) ? values.FirstOrDefault() : sessionId;
        var text = await response.Content.ReadAsStringAsync();
        var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? text.Split('\n').Last(l => l.StartsWith("data:"))["data:".Length..].Trim()
            : text;
        return (json, newSessionId);
    }
}
