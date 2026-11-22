using System.Net;
using System.Text;
using System.Text.Json;

namespace Feeder.StdioShim;

/// <summary>
/// Streamable-HTTP side of the shim. POSTs each client message to the matrix, relays the JSON-RPC
/// response back, tracks the Mcp-Session-Id, and (after initialize) holds one GET SSE stream open
/// for server-initiated messages. When the matrix restarts and forgets the session, the cached
/// initialize handshake is replayed so the client keeps working without a restart.
/// </summary>
public sealed class MatrixRelay : IDisposable
{
    private const string InitializedNotification = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}";
    private static readonly TimeSpan MaxListenBackoff = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly Func<string, Task> _writeToClient;
    private readonly Action<string> _logError;
    private readonly string? _bearerToken;
    private readonly Func<bool>? _startMatrix;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private volatile string? _sessionId;
    private string? _initializeLine;
    private Task? _listenTask;
    private readonly CancellationTokenSource _cts = new();

    public MatrixRelay(string url, Func<string, Task> writeToClient, Action<string> logError, string? bearerToken = null,
        Func<bool>? startMatrix = null, HttpMessageHandler? handler = null)
    {
        _endpoint = new Uri(url);
        _writeToClient = writeToClient;
        _logError = logError;
        _bearerToken = bearerToken;
        _startMatrix = startMatrix;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<bool> WaitForMatrixAsync(TimeSpan timeout)
    {
        var health = new Uri(_endpoint, "/healthz");
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var response = await _http.GetAsync(health, cts.Token);
                if (response.IsSuccessStatusCode)
                    return true;
            }
            catch
            {
                // matrix not up yet
            }
            await Task.Delay(100);
        }
        return false;
    }

    /// <summary>
    /// Relays every line of <paramref name="input"/> until it ends. Messages are forwarded concurrently so a
    /// long tool call never holds back a ping, a cancellation or a parallel call; only initialize is awaited,
    /// because every later message needs the session id its response carries.
    /// </summary>
    public async Task RunAsync(TextReader input, CancellationToken ct)
    {
        var running = new List<Task>();
        while (await input.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var forward = ForwardOrReplyAsync(line, ct);
            if (Inspect(line).IsInitialize)
            {
                await forward;
                continue;
            }
            running.RemoveAll(task => task.IsCompleted);
            running.Add(forward);
        }
        await Task.WhenAll(running);
    }

    private async Task ForwardOrReplyAsync(string line, CancellationToken ct)
    {
        try
        {
            await ForwardAsync(line, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            _logError($"forward failed: {e.Message}");
            if (Inspect(line).Id is { } id)
            {
                var message = JsonSerializer.Serialize("Feeder matrix unreachable: " + e.Message);
                await _writeToClient(
                    "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"error\":{\"code\":-32603,\"message\":" + message + "}}");
            }
        }
    }

    public async Task ForwardAsync(string jsonRpcLine, CancellationToken ct)
    {
        var isInitialize = Inspect(jsonRpcLine).IsInitialize;
        if (isInitialize)
            _initializeLine = jsonRpcLine;

        using var response = await SendWithRecoveryAsync(jsonRpcLine, isInitialize, ct);
        response.EnsureSuccessStatusCode();

        // only the handshake assigns the session; a late answer from a forgotten session must not bring it back
        if (isInitialize && response.Headers.TryGetValues("Mcp-Session-Id", out var values))
            _sessionId = values.FirstOrDefault() ?? _sessionId;

        if (response.StatusCode == HttpStatusCode.Accepted)
            return; // notification — no body

        await RelayBodyAsync(response, _writeToClient, ct);

        if (isInitialize && _sessionId is not null && _listenTask is null)
            _listenTask = ListenForServerMessagesAsync(_cts.Token);
    }

    private async Task<HttpResponseMessage> SendWithRecoveryAsync(string line, bool isInitialize, CancellationToken ct)
    {
        var session = isInitialize ? null : _sessionId;
        HttpResponseMessage response;
        try
        {
            response = await PostAsync(line, session, ct);
        }
        catch (HttpRequestException e) when (e.HttpRequestError == HttpRequestError.ConnectionError)
        {
            // refused before anything was sent, so replaying the message cannot run it twice
            if (!await RestoreSessionAsync(session, matrixDown: true, ct))
                throw;
            return await PostAsync(line, isInitialize ? null : _sessionId, ct);
        }

        if (response.StatusCode != HttpStatusCode.NotFound || session is null)
            return response;

        response.Dispose();
        if (!await RestoreSessionAsync(session, matrixDown: false, ct))
            throw new HttpRequestException("the matrix no longer knows this session and it could not be re-initialized",
                null, HttpStatusCode.NotFound);
        return await PostAsync(line, _sessionId, ct);
    }

    /// <summary>
    /// Replaces a session the matrix no longer knows (it restarted) by replaying the client's initialize
    /// handshake. Single-flight: callers that hit the same stale session wait and reuse the new one.
    /// </summary>
    private async Task<bool> RestoreSessionAsync(string? staleSession, bool matrixDown, CancellationToken ct)
    {
        await _sessionGate.WaitAsync(ct);
        try
        {
            if (_sessionId != staleSession)
                return _sessionId is not null;
            if (matrixDown && !await EnsureMatrixAsync())
                return false;
            if (staleSession is null)
                return true;
            if (_initializeLine is not { } initialize)
                return false;

            string? session = null;
            using (var response = await PostAsync(initialize, null, ct))
            {
                response.EnsureSuccessStatusCode();
                if (response.Headers.TryGetValues("Mcp-Session-Id", out var values))
                    session = values.FirstOrDefault();
                // the client already holds an initialize answer; a second one with the same id would confuse it
                await RelayBodyAsync(response, _ => Task.CompletedTask, ct);
            }
            if (session is null)
                return false;

            using (var initialized = await PostAsync(InitializedNotification, session, ct))
                initialized.EnsureSuccessStatusCode();
            _sessionId = session;
            _logError("matrix session expired (matrix restarted?); re-initialized");
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            _logError($"could not re-initialize the matrix session: {e.Message}");
            return false;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private async Task<bool> EnsureMatrixAsync()
    {
        if (await WaitForMatrixAsync(TimeSpan.FromSeconds(10)))
            return true;
        return _startMatrix?.Invoke() == true && await WaitForMatrixAsync(TimeSpan.FromSeconds(10));
    }

    private async Task<HttpResponseMessage> PostAsync(string line, string? session, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(line, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        AddAuthorization(request);
        if (session is not null)
            request.Headers.Add("Mcp-Session-Id", session);
        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private static async Task RelayBodyAsync(HttpResponseMessage response, Func<string, Task> write, CancellationToken ct)
    {
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (line.StartsWith("data:", StringComparison.Ordinal))
                    await write(line[5..].TrimStart());
            }
        }
        else
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(body))
                await write(body);
        }
    }

    /// <summary>Standing GET stream: server-initiated requests/notifications (sampling, list_changed, …).</summary>
    private async Task ListenForServerMessagesAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            var session = _sessionId;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
                request.Headers.Accept.ParseAdd("text/event-stream");
                request.Headers.Add("Mcp-Session-Id", session);
                AddAuthorization(request);

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.StatusCode == HttpStatusCode.NotFound && await RestoreSessionAsync(session, matrixDown: false, ct))
                {
                    backoff = TimeSpan.FromSeconds(1);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    await Task.Delay(backoff, ct);
                    backoff = Grow(backoff);
                    continue;
                }

                backoff = TimeSpan.FromSeconds(1);
                await RelayBodyAsync(response, _writeToClient, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                _logError($"server-message stream dropped: {e.Message}; retrying in {backoff.TotalSeconds:0}s");
                try { await Task.Delay(backoff, ct); } catch (OperationCanceledException) { return; }
                backoff = Grow(backoff);
            }
        }
    }

    private static TimeSpan Grow(TimeSpan backoff)
        => backoff * 2 < MaxListenBackoff ? backoff * 2 : MaxListenBackoff;

    private static (bool IsInitialize, string? Id) Inspect(string jsonRpcLine)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonRpcLine);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (false, null);
            var isInitialize = root.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String &&
                               method.GetString() == "initialize";
            return (isInitialize, root.TryGetProperty("id", out var id) ? id.GetRawText() : null);
        }
        catch
        {
            // forward as-is; the matrix produces the parse error response
            return (false, null);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _http.Dispose();
        _sessionGate.Dispose();
    }

    private void AddAuthorization(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_bearerToken))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _bearerToken);
    }
}
