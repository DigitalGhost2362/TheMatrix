using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Feeder.Matrix.Benchmarks;

/// <summary>
/// Minimal JSON-RPC client for MCP Streamable HTTP, used for latency baselining.
/// Deliberately dependency-free so measured overhead is dominated by the server under test.
/// </summary>
public sealed class McpHttpBenchClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private string? _sessionId;
    private long _nextId = 1;

    public string? ServerInfoJson { get; private set; }
    public string? NegotiatedProtocolVersion { get; private set; }

    public McpHttpBenchClient(string url)
    {
        _endpoint = new Uri(url);
        _http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
            ConnectTimeout = TimeSpan.FromSeconds(5),
        })
        {
            Timeout = TimeSpan.FromSeconds(120),
        };
    }

    public async Task<TimeSpan> InitializeAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var (json, response) = await PostAsync(
            """{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"feeder-matrix-bench","version":"0.1.0"}}}""",
            ct);
        sw.Stop();

        if (response.Headers.TryGetValues("Mcp-Session-Id", out var values))
            _sessionId = values.FirstOrDefault();

        using var doc = JsonDocument.Parse(json);
        var result = doc.RootElement.GetProperty("result");
        ServerInfoJson = result.GetProperty("serverInfo").GetRawText();
        NegotiatedProtocolVersion = result.GetProperty("protocolVersion").GetString();

        await NotifyAsync("notifications/initialized", ct);
        return sw.Elapsed;
    }

    public async Task<(TimeSpan elapsed, string resultJson)> RequestAsync(string method, string? paramsJson, CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var body = paramsJson is null
            ? $$"""{"jsonrpc":"2.0","id":{{id}},"method":"{{method}}"}"""
            : $$"""{"jsonrpc":"2.0","id":{{id}},"method":"{{method}}","params":{{paramsJson}}}""";

        var sw = Stopwatch.StartNew();
        var (json, _) = await PostAsync(body, ct);
        sw.Stop();

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"JSON-RPC error for {method}: {error.GetRawText()}");
        var result = doc.RootElement.TryGetProperty("result", out var r) ? r.GetRawText() : "{}";
        return (sw.Elapsed, result);
    }

    private async Task NotifyAsync(string method, CancellationToken ct)
    {
        using var request = BuildRequest($$"""{"jsonrpc":"2.0","method":"{{method}}"}""");
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<(string json, HttpResponseMessage response)> PostAsync(string body, CancellationToken ct)
    {
        using var request = BuildRequest(body);
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(ct);
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var json = mediaType == "text/event-stream" ? ExtractLastSseData(text) : text;
        return (json, response);
    }

    private HttpRequestMessage BuildRequest(string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (_sessionId is not null)
            request.Headers.Add("Mcp-Session-Id", _sessionId);
        return request;
    }

    /// <summary>The response for a single request arrives as one or more SSE events; the JSON-RPC response is the last `data:` payload.</summary>
    private static string ExtractLastSseData(string sse)
    {
        string? last = null;
        foreach (var line in sse.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("data: ", StringComparison.Ordinal))
                last = trimmed["data: ".Length..];
            else if (trimmed.StartsWith("data:", StringComparison.Ordinal))
                last = trimmed["data:".Length..];
        }
        return last ?? throw new InvalidOperationException("No data field in SSE response.");
    }

    public void Dispose() => _http.Dispose();
}
