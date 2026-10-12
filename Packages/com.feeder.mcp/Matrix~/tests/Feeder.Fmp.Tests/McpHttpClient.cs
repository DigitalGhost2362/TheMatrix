using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Feeder.Fmp.Tests
{
    // the AI-client side: MCP over plain HTTP POST, answers may arrive as server-sent events
    internal sealed class McpHttpClient : IAsyncDisposable
    {
        readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        readonly string _url;
        string _session;
        int _id;

        public McpHttpClient(string url)
        {
            _url = url;
        }

        public async Task InitializeAsync()
        {
            using (await RpcAsync("initialize", "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"fmp-tests\",\"version\":\"1\"}}").ConfigureAwait(false))
            {
            }
            await PostAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", CancellationToken.None).ConfigureAwait(false);
        }

        public Task<JsonDocument> ListToolsAsync() => RpcAsync("tools/list", "{}");

        public Task<JsonDocument> CallToolAsync(string name, string argumentsJson, CancellationToken ct = default)
            => RpcAsync("tools/call", "{\"name\":" + JsonSerializer.Serialize(name) + ",\"arguments\":" + argumentsJson + "}", ct);

        public static string[] ToolNames(JsonDocument list)
            => list.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).ToArray();

        public static string FirstText(JsonDocument call)
            => call.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();

        public async ValueTask DisposeAsync()
        {
            if (_session != null)
            {
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Delete, _url))
                    {
                        request.Headers.Add("Mcp-Session-Id", _session);
                        using (await _http.SendAsync(request).ConfigureAwait(false))
                        {
                        }
                    }
                }
                catch (Exception)
                {
                }
            }
            _http.Dispose();
        }

        async Task<JsonDocument> RpcAsync(string method, string paramsJson, CancellationToken ct = default)
        {
            int id = Interlocked.Increment(ref _id);
            string body = "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"" + method + "\",\"params\":" + paramsJson + "}";
            return JsonDocument.Parse(await PostAsync(body, ct).ConfigureAwait(false));
        }

        async Task<string> PostAsync(string body, CancellationToken ct)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, _url) { Content = new StringContent(body, Encoding.UTF8, "application/json") })
            {
                request.Headers.Accept.ParseAdd("application/json");
                request.Headers.Accept.ParseAdd("text/event-stream");
                if (_session != null)
                    request.Headers.Add("Mcp-Session-Id", _session);
                using (HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false))
                {
                    if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessions))
                        _session = sessions.First();
                    response.EnsureSuccessStatusCode();
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (response.Content.Headers.ContentType?.MediaType != "text/event-stream")
                        return text;
                    string data = text.Split('\n').LastOrDefault(line => line.StartsWith("data:", StringComparison.Ordinal));
                    return data == null ? string.Empty : data.Substring("data:".Length).Trim();
                }
            }
        }
    }
}
