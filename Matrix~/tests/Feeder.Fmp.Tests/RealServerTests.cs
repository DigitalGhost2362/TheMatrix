using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Feeder.Fmp.Tests
{
    public sealed class RegisterReply
    {
        public string SessionId { get; set; }

        public string ChosenProtocolVersion { get; set; }

        public bool RegistryUpToDate { get; set; }

        public string[] AwaitedOperations { get; set; }

        public int QueueCapacity { get; set; }

        public object Error { get; set; }
    }

    public sealed class SharedServer : IAsyncLifetime
    {
        internal MatrixServer Server { get; private set; }

        public async Task InitializeAsync() => Server = await MatrixServer.StartAsync();

        public Task DisposeAsync()
        {
            Server?.Dispose();
            return Task.CompletedTask;
        }
    }

    // black-box: the real matrix-server on one side, FmpConnection plus an MCP HTTP client on the other
    public sealed class RealServerTests : IClassFixture<SharedServer>
    {
        static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

        readonly MatrixServer _server;
        readonly ITestOutputHelper _output;

        public RealServerTests(SharedServer shared, ITestOutputHelper output)
        {
            _server = shared.Server;
            _output = output;
        }

        [Fact]
        public async Task Direct_websocket_without_negotiate_registers_and_returns_a_typed_response()
        {
            using (var unity = new UnitySide(_server.HubUrl))
            {
                await unity.StartAsync();
                Assert.Equal(FmpState.Connected, unity.Connection.State);

                string json = await unity.RegisterAsync();
                RegisterReply reply = JsonSerializer.Deserialize<RegisterReply>(json, Json.Web);

                Assert.False(string.IsNullOrEmpty(reply.SessionId));
                Assert.Equal("1", reply.ChosenProtocolVersion);
                Assert.False(reply.RegistryUpToDate);
                Assert.Equal(32, reply.QueueCapacity);
                Assert.Null(reply.Error);

                await Json.WaitUntilAsync(() => unity.ClientLists.Length > 0, what: "the McpClientsChanged seed");
                using (JsonDocument seed = JsonDocument.Parse(unity.ClientLists[0]))
                {
                    Assert.Equal(1, seed.RootElement.GetArrayLength());
                    Assert.Equal(JsonValueKind.Array, seed.RootElement[0].ValueKind);
                }

                JsonElement link = await LinkAsync(_server, unity.ProjectId);
                Assert.True(link.GetProperty("connected").GetBoolean());
                Assert.Equal(unity.InstanceId, link.GetProperty("unityInstanceId").GetString());
            }
        }

        [Fact]
        public async Task A_published_registry_shows_up_in_tools_list()
        {
            using (var unity = new UnitySide(_server.HubUrl))
            await using (var mcp = new McpHttpClient(_server.McpUrl))
            {
                await unity.StartAsync();
                await unity.RegisterAsync();
                await unity.PublishToolsAsync("echo");
                await mcp.InitializeAsync();

                using (JsonDocument list = await WaitForToolAsync(mcp, "echo"))
                {
                    JsonElement tool = list.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "echo");
                    Assert.Equal("echo title", tool.GetProperty("title").GetString());
                    Assert.Equal("echo description", tool.GetProperty("description").GetString());
                    Assert.Equal("string", tool.GetProperty("inputSchema").GetProperty("properties").GetProperty("text").GetProperty("type").GetString());
                }
            }
        }

        [Fact]
        public async Task A_tool_call_travels_mcp_to_InvokeTool_to_CompleteTool_to_mcp()
        {
            using (var unity = new UnitySide(_server.HubUrl))
            await using (var mcp = new McpHttpClient(_server.McpUrl))
            {
                ToolCall seen = null;
                unity.OnTool = call =>
                {
                    seen = call;
                    using (JsonDocument arguments = JsonDocument.Parse(call.ArgumentsJson))
                        return Task.FromResult(UnitySide.TextResult("echo:" + arguments.RootElement.GetProperty("text").GetString()));
                };
                await unity.StartAsync();
                await unity.RegisterAsync();
                await unity.PublishToolsAsync("echo");
                await mcp.InitializeAsync();
                (await WaitForToolAsync(mcp, "echo")).Dispose();

                using (JsonDocument response = await mcp.CallToolAsync("echo", "{\"text\":\"h\\u00e9llo \\\"matrix\\\" \\\\ \\n+\"}").WaitAsync(Limit))
                    Assert.Equal("echo:h\u00e9llo \"matrix\" \\ \n+", McpHttpClient.FirstText(response));

                Assert.Equal("echo", seen.ToolName);
                Assert.False(string.IsNullOrEmpty(seen.OperationId));
                await Json.WaitUntilAsync(() => unity.ClientLists.Any(list => list.Contains("\"clientName\":\"fmp-tests\"")), what: "the MCP client announcement");
            }
        }

        [Fact]
        public async Task A_completion_may_arrive_on_a_new_connection_after_the_old_one_dropped()
        {
            using (var first = new UnitySide(_server.HubUrl))
            await using (var mcp = new McpHttpClient(_server.McpUrl))
            {
                var invoked = new TaskCompletionSource<ToolCall>(TaskCreationOptions.RunContinuationsAsynchronously);
                first.OnTool = call =>
                {
                    invoked.TrySetResult(call);
                    return new TaskCompletionSource<string>().Task;
                };
                await first.StartAsync();
                await first.RegisterAsync();
                await first.PublishToolsAsync("reload");
                await mcp.InitializeAsync();
                (await WaitForToolAsync(mcp, "reload")).Dispose();

                Task<JsonDocument> pending = mcp.CallToolAsync("reload", "{}");
                ToolCall call = await invoked.Task.WaitAsync(Limit);
                first.Dispose();

                using (var second = new UnitySide(_server.HubUrl))
                {
                    await second.StartAsync();
                    await second.Connection.InvokeAsync("Register", RegisterFor(first, pending: call.OperationId));
                    await second.Connection.SendAsync("CompleteTool",
                        "[{\"operationId\":\"" + call.OperationId + "\",\"success\":true,\"contentJsonUtf8\":\"" +
                        Convert.ToBase64String(Encoding.UTF8.GetBytes(UnitySide.TextResult("finished after reload"))) + "\"}]");
                    using (JsonDocument response = await pending.WaitAsync(Limit))
                        Assert.Equal("finished after reload", McpHttpClient.FirstText(response));
                }
            }
        }

        [Fact]
        public async Task CancelTool_is_delivered_while_the_InvokeTool_handler_is_still_running()
        {
            using (MatrixServer server = await MatrixServer.StartAsync(toolTimeout: TimeSpan.FromSeconds(2)))
            using (var unity = new UnitySide(server.HubUrl))
            await using (var mcp = new McpHttpClient(server.McpUrl))
            {
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var started = new TaskCompletionSource<ToolCall>(TaskCreationOptions.RunContinuationsAsynchronously);
                var cancelled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                unity.OnTool = async call =>
                {
                    started.TrySetResult(call);
                    await release.Task;
                    return UnitySide.TextResult("too late");
                };
                unity.OnCancel = arguments =>
                {
                    cancelled.TrySetResult(release.Task.IsCompleted ? "after:" + arguments : "during:" + arguments);
                    return Task.CompletedTask;
                };
                await unity.StartAsync();
                await unity.RegisterAsync();
                await unity.PublishToolsAsync("slow");
                await mcp.InitializeAsync();
                (await WaitForToolAsync(mcp, "slow")).Dispose();

                Task<JsonDocument> call = mcp.CallToolAsync("slow", "{}");
                ToolCall invoked = await started.Task.WaitAsync(Limit);
                string seen = await cancelled.Task.WaitAsync(Limit);
                release.TrySetResult(true);

                Assert.Equal("during:[\"" + invoked.OperationId + "\"]", seen);
                using (JsonDocument response = await call.WaitAsync(Limit))
                    Assert.Contains("timed out", response.RootElement.GetRawText());
            }
        }

        [Fact]
        public async Task Ten_megabyte_payload_round_trips_in_both_directions()
        {
            string text = BuildPayload(10_000_000);
            string expectedHash = Hash(text);
            string receivedHash = null;
            int receivedChars = 0;
            long parsedMs = 0;
            using (var unity = new UnitySide(_server.HubUrl))
            await using (var mcp = new McpHttpClient(_server.McpUrl))
            {
                unity.OnTool = call =>
                {
                    var watch = Stopwatch.StartNew();
                    using (JsonDocument arguments = JsonDocument.Parse(call.ArgumentsJson))
                    {
                        string received = arguments.RootElement.GetProperty("text").GetString();
                        receivedChars = received.Length;
                        receivedHash = Hash(received);
                        parsedMs = watch.ElapsedMilliseconds;
                        return Task.FromResult(UnitySide.TextResult(received));
                    }
                };
                await unity.StartAsync();
                await unity.RegisterAsync();
                await unity.PublishToolsAsync("echo");
                await mcp.InitializeAsync();
                (await WaitForToolAsync(mcp, "echo")).Dispose();

                string body = "{\"text\":" + JsonSerializer.Serialize(text, Json.Relaxed) + "}";
                var loop = Stopwatch.StartNew();
                using (JsonDocument response = await mcp.CallToolAsync("echo", body).WaitAsync(TimeSpan.FromSeconds(100)))
                {
                    loop.Stop();
                    string echoed = McpHttpClient.FirstText(response);
                    Assert.Equal(text.Length, echoed.Length);
                    Assert.Equal(expectedHash, Hash(echoed));
                }

                Assert.Equal(text.Length, receivedChars);
                Assert.Equal(expectedHash, receivedHash);
                double inboundMb = unity.LastInvokeChars / 1048576.0;
                _output.WriteLine("payload {0:N0} UTF-8 bytes, request body {1:N0} chars", Encoding.UTF8.GetByteCount(text), body.Length);
                _output.WriteLine("server -> Unity frame arguments {0:N1} MiB (chars); parse+hash in handler {1} ms", inboundMb, parsedMs);
                _output.WriteLine("Unity -> server SendAsync of the result: {0} ms", unity.LastCompleteMs);
                _output.WriteLine("whole loop (HTTP in, FMP down, FMP up, HTTP out): {0} ms", loop.ElapsedMilliseconds);
            }
        }

        [Fact]
        public async Task Many_concurrent_calls_and_sends_share_one_socket_without_corruption()
        {
            using (var unity = new UnitySide(_server.HubUrl, projectId: "fmp-concurrent"))
            {
                await unity.StartAsync();
                string schema = new string('s', 150_000);
                IEnumerable<Task> work = Enumerable.Range(0, 120).SelectMany(n => new[]
                {
                    Task.Run(async () => Assert.False(string.IsNullOrEmpty(SessionOf(await unity.RegisterAsync())))),
                    Task.Run(() => unity.Connection.SendAsync("Heartbeat", "[{\"unityInstanceId\":\"" + unity.InstanceId + "\",\"editorState\":" + n % 5 + "}]")),
                    Task.Run(() => unity.Connection.SendAsync("ReportProgress", "[{\"operationId\":\"op" + n + "\",\"progress\":0.5,\"message\":\"" + schema + "\"}]")),
                });

                await Task.WhenAll(work).WaitAsync(TimeSpan.FromSeconds(60));

                Assert.Equal(FmpState.Connected, unity.Connection.State);
                Assert.Equal(new[] { FmpState.Connecting, FmpState.Connected }, unity.States);
                Assert.False(string.IsNullOrEmpty(SessionOf(await unity.RegisterAsync())));
            }
        }

        [Theory]
        [InlineData(16_777_215, true)]
        [InlineData(16_777_216, false)]
        public async Task The_servers_receive_limit_counts_the_separator_and_matches_the_default_send_limit(int frameBytes, bool accepted)
        {
            using (var unity = new UnitySide(_server.HubUrl, new FmpOptions { MaxSendMessageSize = 64 * 1024 * 1024, ReconnectDelays = new TimeSpan[0] }, "fmp-limit"))
            {
                var closed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
                unity.Connection.Closed += error => closed.TrySetResult(error);
                await unity.StartAsync();
                await unity.RegisterAsync();

                // frameBytes excludes the separator; the library writes {"type":1,"target":"Heartbeat","arguments":<json>}
                const string prefix = "{\"type\":1,\"target\":\"Heartbeat\",\"arguments\":";
                string arguments = "[\"" + new string('x', frameBytes - prefix.Length - 1 - 4) + "\"]";
                await unity.Connection.SendAsync("Heartbeat", arguments).WaitAsync(Limit);

                if (accepted)
                {
                    await Task.Delay(1500);
                    Assert.Equal(FmpState.Connected, unity.Connection.State);
                    Assert.False(closed.Task.IsCompleted);
                    Assert.False(string.IsNullOrEmpty(SessionOf(await unity.RegisterAsync())));
                }
                else
                {
                    Exception error = await closed.Task.WaitAsync(Limit);
                    _output.WriteLine("a {0:N0} byte frame made the server drop the link: {1}", frameBytes, error.Message);
                    Assert.Contains("closed the connection", error.Message);
                }
            }
        }

        [Fact]
        [Trait("Category", "Slow")]
        public async Task Keepalive_pings_keep_the_link_alive_far_past_the_servers_thirty_second_client_timeout()
        {
            using (MatrixServer server = await MatrixServer.StartAsync())
            using (var alive = new UnitySide(server.HubUrl, projectId: "fmp-alive"))
            {
                await alive.StartAsync();
                await alive.RegisterAsync();

                // control: a client that pings once and then goes quiet is cut off by this very server
                var idle = Stopwatch.StartNew();
                string[] control = await QuietClientAsync(server).WaitAsync(TimeSpan.FromSeconds(60));
                double cutOffAfter = idle.Elapsed.TotalSeconds;
                _output.WriteLine("control client (one ping, then silence) was cut off by the server after {0:N1} s", cutOffAfter);
                _output.WriteLine("frames it received: {0}", string.Join(" ", control));
                Assert.InRange(cutOffAfter, 28, 45);
                Assert.Equal("{\"type\":7,\"error\":\"Connection closed with an error.\",\"allowReconnect\":true}", control[control.Length - 2]);
                Assert.Equal("close", control[control.Length - 1]);

                TimeSpan rest = TimeSpan.FromSeconds(65) - idle.Elapsed;
                if (rest > TimeSpan.Zero)
                    await Task.Delay(rest);
                _output.WriteLine("library connection idle for {0:N1} s", idle.Elapsed.TotalSeconds);

                Assert.Equal(FmpState.Connected, alive.Connection.State);
                Assert.Equal(new[] { FmpState.Connecting, FmpState.Connected }, alive.States);
                RegisterReply reply = JsonSerializer.Deserialize<RegisterReply>(await alive.RegisterAsync(), Json.Web);
                Assert.False(string.IsNullOrEmpty(reply.SessionId));
                Assert.True((await LinkAsync(server, "fmp-alive")).GetProperty("connected").GetBoolean());
            }
        }

        [Fact]
        public async Task A_killed_server_restarted_on_the_same_port_is_reconnected_and_registered_again()
        {
            var delays = new[] { TimeSpan.Zero }.Concat(Enumerable.Repeat(TimeSpan.FromMilliseconds(500), 60)).ToArray();
            MatrixServer first = await MatrixServer.StartAsync();
            try
            {
                using (var unity = new UnitySide(first.HubUrl, new FmpOptions { ReconnectDelays = delays }))
                {
                    await unity.StartAsync();
                    string firstSession = SessionOf(await unity.RegisterAsync());
                    await Json.WaitUntilAsync(() => unity.ClientLists.Length == 1, what: "the first client list");

                    first.Kill();
                    await Json.WaitUntilAsync(() => unity.Connection.State == FmpState.Reconnecting, what: "Reconnecting");
                    var started = Stopwatch.StartNew();
                    first = await StartOnPortAsync(first.Port);
                    await Json.WaitUntilAsync(() => unity.Connection.State == FmpState.Connected, 30000, "Connected again");
                    _output.WriteLine("reconnected {0} ms after the replacement server was launched", started.ElapsedMilliseconds);

                    string secondSession = SessionOf(await unity.RegisterAsync());
                    Assert.NotEqual(firstSession, secondSession);
                    await Json.WaitUntilAsync(() => unity.ClientLists.Length == 2, what: "the client list of the new server");
                    Assert.Equal(new[] { FmpState.Connecting, FmpState.Connected, FmpState.Reconnecting, FmpState.Connected }, unity.States);
                    Assert.True((await LinkAsync(first, unity.ProjectId)).GetProperty("connected").GetBoolean());
                }
            }
            finally
            {
                first.Dispose();
            }
        }

        [Fact]
        public async Task An_exhausted_reconnect_policy_raises_Closed_and_ends_disconnected()
        {
            using (MatrixServer server = await MatrixServer.StartAsync())
            using (var unity = new UnitySide(server.HubUrl))
            {
                var closed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
                unity.Connection.Closed += error => closed.TrySetResult(error);
                await unity.StartAsync();
                await unity.RegisterAsync();

                var watch = Stopwatch.StartNew();
                server.Kill();
                Exception error = await closed.Task.WaitAsync(TimeSpan.FromSeconds(30));
                watch.Stop();

                _output.WriteLine("Closed after {0} ms: {1}", watch.ElapsedMilliseconds, error.GetType().Name + ": " + error.Message);
                Assert.InRange(watch.ElapsedMilliseconds, 3000, 15000);
                Assert.Equal(new[] { FmpState.Connecting, FmpState.Connected, FmpState.Reconnecting, FmpState.Disconnected }, unity.States);
                Assert.Equal(FmpState.Disconnected, unity.Connection.State);
                await Assert.ThrowsAsync<InvalidOperationException>(() => unity.RegisterAsync());
            }
        }

        [Fact]
        public async Task StopAsync_and_Dispose_return_quickly_and_the_server_sees_the_link_end()
        {
            var unity = new UnitySide(_server.HubUrl, projectId: "fmp-stop");
            try
            {
                await unity.StartAsync();
                await unity.RegisterAsync();

                var watch = Stopwatch.StartNew();
                await unity.Connection.StopAsync();
                long stopMs = watch.ElapsedMilliseconds;
                await unity.Connection.StopAsync();
                _output.WriteLine("StopAsync {0} ms", stopMs);
                Assert.True(stopMs < 1500, "StopAsync took " + stopMs + " ms");
                Assert.Equal(FmpState.Disconnected, unity.Connection.State);
                await Json.WaitUntilAsync(async () => !(await LinkAsync(_server, "fmp-stop")).GetProperty("connected").GetBoolean(), 5000, "the server to see the link end");

                await unity.StartAsync();
                await unity.RegisterAsync();
                watch.Restart();
                unity.Dispose();
                long disposeMs = watch.ElapsedMilliseconds;
                _output.WriteLine("Dispose {0} ms", disposeMs);
                await Task.Delay(300);
                _output.WriteLine(_server.Output);
                Assert.True(disposeMs < 200, "Dispose took " + disposeMs + " ms");
                await Assert.ThrowsAsync<ObjectDisposedException>(() => unity.RegisterAsync());
                await Json.WaitUntilAsync(async () => !(await LinkAsync(_server, "fmp-stop")).GetProperty("connected").GetBoolean(), 5000, "the server to see the link end again");
            }
            finally
            {
                unity.Dispose();
            }
        }

        [Fact]
        public async Task Bad_endpoints_fail_the_start_quickly_and_leave_the_connection_disconnected()
        {
            int closedPort;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            closedPort = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var endpoints = new[]
            {
                new Uri("ws://127.0.0.1:" + closedPort + "/fmp"),
                new Uri("http://127.0.0.1:" + _server.Port + "/healthz"),
                new Uri("http://127.0.0.1:" + _server.Port + "/not-a-hub"),
            };
            foreach (Uri endpoint in endpoints)
            {
                using (var unity = new UnitySide(endpoint))
                {
                    var watch = Stopwatch.StartNew();
                    Exception error = await Record.ExceptionAsync(() => unity.StartAsync());
                    _output.WriteLine("{0} -> {1} in {2} ms: {3}", endpoint, error?.GetType().Name, watch.ElapsedMilliseconds, error?.Message);
                    Assert.NotNull(error);
                    Assert.True(watch.ElapsedMilliseconds < 3000);
                    Assert.Equal(FmpState.Disconnected, unity.Connection.State);
                    Assert.Equal(new[] { FmpState.Connecting, FmpState.Disconnected }, unity.States);
                }
            }
        }

        // plain websocket, no library: handshake, a single hub ping, then nothing; returns what the server sent, "close" last
        static async Task<string[]> QuietClientAsync(MatrixServer server)
        {
            var frames = new List<string>();
            using (var socket = new System.Net.WebSockets.ClientWebSocket())
            {
                socket.Options.KeepAliveInterval = TimeSpan.Zero;
                await socket.ConnectAsync(server.HubUrl, CancellationToken.None);
                await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}"), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
                await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":6}"), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
                var buffer = new byte[4096];
                while (true)
                {
                    System.Net.WebSockets.WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    {
                        frames.Add("close");
                        return frames.ToArray();
                    }
                    frames.AddRange(Encoding.UTF8.GetString(buffer, 0, result.Count).Split('', StringSplitOptions.RemoveEmptyEntries));
                }
            }
        }

        static string RegisterFor(UnitySide original, string pending)
            => "[{\"protocolVersion\":\"1\",\"projectId\":\"" + original.ProjectId + "\",\"unityInstanceId\":\"" + original.InstanceId +
               "\",\"projectPathHash\":\"h\",\"unityVersion\":\"2022.3.62f3\",\"pluginVersion\":\"0.0.0-test\"," +
               "\"supportedProtocolVersions\":[\"1\"],\"toolRegistryHash\":\"registry-1\",\"authToken\":\"\",\"editorState\":0,\"pendingOperations\":[\"" + pending + "\"]}]";

        static string SessionOf(string registerReply)
            => JsonSerializer.Deserialize<RegisterReply>(registerReply, Json.Web).SessionId;

        static async Task<JsonElement> LinkAsync(MatrixServer server, string projectId)
        {
            using (JsonDocument document = JsonDocument.Parse(await server.HealthAsync()))
                return document.RootElement.GetProperty("unityLinks").EnumerateArray().Single(l => l.GetProperty("projectId").GetString() == projectId).Clone();
        }

        static async Task<JsonDocument> WaitForToolAsync(McpHttpClient mcp, string tool)
        {
            var watch = Stopwatch.StartNew();
            while (true)
            {
                JsonDocument list = await mcp.ListToolsAsync();
                if (list.RootElement.TryGetProperty("result", out _) && McpHttpClient.ToolNames(list).Contains(tool))
                    return list;
                list.Dispose();
                if (watch.Elapsed > Limit)
                    throw new TimeoutException("tools/list never showed '" + tool + "'.");
                await Task.Delay(50);
            }
        }

        static async Task<MatrixServer> StartOnPortAsync(int port)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    return await MatrixServer.StartAsync(port);
                }
                catch (InvalidOperationException) when (attempt < 10)
                {
                    await Task.Delay(500);
                }
            }
        }

        // ascii blocks with quotes, backslashes, newlines, two-byte and four-byte characters
        static string BuildPayload(int utf8Bytes)
        {
            const string block = "AbCdEfGh0123456789+/=ijklmnopqrstuvwxyz \\ \" \n \t \u00e9 \U0001F600 end|";
            return new StringBuilder().Insert(0, block, utf8Bytes / Encoding.UTF8.GetByteCount(block)).ToString();
        }

        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
