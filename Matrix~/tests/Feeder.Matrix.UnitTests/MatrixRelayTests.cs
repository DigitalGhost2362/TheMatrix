using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Feeder.StdioShim;

namespace Feeder.Matrix.UnitTests;

public sealed class MatrixRelayTests
{
    const string Initialize = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}";
    const string SlowCall = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"slow\"}}";
    const string Ping = "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"ping\"}";
    const string ListTools = "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/list\"}";
    static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Forwards_a_request_while_another_is_in_flight()
    {
        var matrix = new FakeMatrix();
        var client = new ClientOutput();
        using var relay = new MatrixRelay("http://127.0.0.1:1/mcp", client.WriteAsync, _ => { }, handler: matrix);

        var input = new StringReader(string.Join("\n", Initialize, SlowCall, Ping));
        var run = relay.RunAsync(input, CancellationToken.None);

        Assert.True(await client.WaitForIdAsync(3, Wait), "ping was held back by the slow call");
        Assert.False(client.HasId(2));
        Assert.False(run.IsCompleted);

        matrix.ReleaseSlowCall();
        await run.WaitAsync(Wait);
        Assert.True(client.HasId(2));
    }

    [Fact]
    public async Task Reinitializes_when_the_matrix_forgets_the_session()
    {
        var matrix = new FakeMatrix();
        var client = new ClientOutput();
        using var relay = new MatrixRelay("http://127.0.0.1:1/mcp", client.WriteAsync, _ => { }, handler: matrix);

        await relay.ForwardAsync(Initialize, CancellationToken.None);
        var firstSession = matrix.Session;
        matrix.Restart();

        await relay.ForwardAsync(ListTools, CancellationToken.None).WaitAsync(Wait);

        Assert.True(client.HasId(4));
        Assert.DoesNotContain(client.Lines, line => line.Contains("\"error\""));
        Assert.Equal(1, client.Lines.Count(line => Id(line) == 1));
        Assert.NotEqual(firstSession, matrix.Session);
        Assert.Equal(1, matrix.InitializedNotifications);
        Assert.Contains(matrix.SessionsSeenFor("tools/list"), session => session == matrix.Session);
    }

    static int? Id(string line)
    {
        using var doc = JsonDocument.Parse(line);
        return doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt32() : null;
    }

    sealed class ClientOutput
    {
        readonly ConcurrentQueue<string> _lines = new();

        public IReadOnlyCollection<string> Lines => _lines.ToArray();

        public Task WriteAsync(string line)
        {
            _lines.Enqueue(line);
            return Task.CompletedTask;
        }

        public bool HasId(int id) => _lines.Any(line => Id(line) == id);

        public async Task<bool> WaitForIdAsync(int id, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (HasId(id))
                    return true;
                await Task.Delay(10);
            }
            return false;
        }
    }

    /// <summary>A stateful MCP endpoint: unknown sessions get 404, like the real matrix after a restart.</summary>
    sealed class FakeMatrix : HttpMessageHandler
    {
        readonly TaskCompletionSource _slowCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly ConcurrentQueue<(string Method, string? Session)> _posts = new();
        int _sessionCounter;
        int _initialized;
        volatile string? _session;

        public string? Session => _session;
        public int InitializedNotifications => Volatile.Read(ref _initialized);

        public void ReleaseSlowCall() => _slowCall.TrySetResult();

        public void Restart() => _session = null;

        public IEnumerable<string?> SessionsSeenFor(string method)
            => _posts.Where(post => post.Method == method).Select(post => post.Session);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var session = request.Headers.TryGetValues("Mcp-Session-Id", out var values) ? values.First() : null;
            if (request.Method == HttpMethod.Get)
            {
                if (session != _session)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var method = doc.RootElement.GetProperty("method").GetString()!;
            var id = doc.RootElement.TryGetProperty("id", out var idElement) ? idElement.GetRawText() : null;
            _posts.Enqueue((method, session));

            if (method == "initialize")
            {
                _session = "session-" + Interlocked.Increment(ref _sessionCounter);
                var response = Json(id!, "{\"protocolVersion\":\"2025-06-18\"}");
                response.Headers.Add("Mcp-Session-Id", _session);
                return response;
            }
            if (session is null || session != _session)
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (method == "notifications/initialized")
            {
                Interlocked.Increment(ref _initialized);
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }
            if (method == "tools/call")
                await _slowCall.Task.WaitAsync(cancellationToken);
            return Json(id!, "{}");
        }

        static HttpResponseMessage Json(string id, string result) => new(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":" + result + "}", Encoding.UTF8, "application/json"),
        };
    }
}
