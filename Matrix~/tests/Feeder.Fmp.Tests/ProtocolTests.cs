using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Feeder.Fmp.Tests
{
    // FmpConnection against a scripted peer: wire shapes, fragmentation, keep-alive, failure and reconnect rules
    public sealed class ProtocolTests
    {
        static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
        static readonly TimeSpan[] NoRetry = new TimeSpan[0];

        readonly ITestOutputHelper _output;

        public ProtocolTests(ITestOutputHelper output)
        {
            _output = output;
        }

        sealed class Spy
        {
            readonly List<FmpState> _states = new List<FmpState>();

            public Spy(FmpConnection connection)
            {
                connection.StateChanged += state => { lock (_states) _states.Add(state); };
                connection.Closed += error => Closed.TrySetResult(error);
            }

            public TaskCompletionSource<Exception> Closed { get; } = Signal<Exception>();

            public FmpState[] States
            {
                get
                {
                    lock (_states)
                        return _states.ToArray();
                }
            }
        }

        static TaskCompletionSource<T> Signal<T>() => new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        static string Field(string frame, string name)
        {
            using (JsonDocument document = JsonDocument.Parse(frame))
                return document.RootElement.TryGetProperty(name, out JsonElement value) ? value.GetString() : null;
        }

        static string Complete(string id, string tail) => "{\"type\":3,\"invocationId\":\"" + id + "\"" + tail + "}";

        // answers every invocation that carries an id with the string "ok"
        static async Task AnswerAllAsync(FakeSession session)
        {
            string frame;
            while ((frame = await session.ReadFrameAsync()) != null)
            {
                string id = Field(frame, "invocationId");
                if (id != null)
                    await session.SendFrameAsync(Complete(id, ",\"result\":\"ok\""));
            }
        }

        static string Payload(int chars)
        {
            var text = new StringBuilder(chars + 16);
            string block = "Block \u00e9 \U0001F600 \"quoted\" \\ end|";
            while (text.Length < chars)
                text.Append(block);
            return text.ToString(0, chars - (chars > 0 && char.IsHighSurrogate(text[chars - 1]) ? 1 : 0));
        }

        [Fact]
        public async Task The_handshake_announces_json_protocol_version_1_as_one_text_frame()
        {
            var seen = Signal<string>();
            using (var server = new FakeFmpServer(async s => { seen.TrySetResult(await s.ExpectHandshakeAsync()); await s.WaitForCloseAsync(); }))
            using (var connection = new FmpConnection(server.Url))
            {
                await connection.StartAsync();

                Assert.Equal("{\"protocol\":\"json\",\"version\":1}", await seen.Task.WaitAsync(Limit));
                Assert.Equal(FmpState.Connected, connection.State);
            }
        }

        [Fact]
        public async Task A_handshake_error_fails_the_start_with_the_servers_text()
        {
            using (var server = new FakeFmpServer(async s =>
            {
                await s.ReadFrameAsync();
                await s.SendFrameAsync("{\"error\":\"Requested protocol \\u0027json\\u0027 is not available.\"}");
            }))
            using (var connection = new FmpConnection(server.Url))
            {
                var spy = new Spy(connection);

                FmpException error = await Assert.ThrowsAsync<FmpException>(() => connection.StartAsync());

                Assert.Contains("Requested protocol 'json' is not available.", error.Message);
                Assert.Equal(FmpState.Disconnected, connection.State);
                Assert.Equal(new[] { FmpState.Connecting, FmpState.Disconnected }, spy.States);
            }
        }

        [Fact]
        public async Task A_peer_that_closes_during_the_handshake_fails_the_start()
        {
            using (var server = new FakeFmpServer(async s => { await s.ReadFrameAsync(); await s.CloseAsync(); }))
            using (var connection = new FmpConnection(server.Url))
            {
                FmpException error = await Assert.ThrowsAsync<FmpException>(() => connection.StartAsync());

                Assert.Contains("handshake", error.Message);
                Assert.Equal(FmpState.Disconnected, connection.State);
            }
        }

        [Fact]
        public async Task A_peer_that_never_answers_the_handshake_times_out_the_start()
        {
            using (var server = new FakeFmpServer(async s => { await s.ReadFrameAsync(); await Task.Delay(Timeout.Infinite); }))
            using (var connection = new FmpConnection(server.Url, new FmpOptions { ServerTimeout = TimeSpan.FromMilliseconds(400) }))
            {
                var watch = Stopwatch.StartNew();

                await Assert.ThrowsAsync<TimeoutException>(() => connection.StartAsync());

                Assert.InRange(watch.ElapsedMilliseconds, 300, 5000);
                Assert.Equal(FmpState.Disconnected, connection.State);
            }
        }

        [Fact]
        public async Task Completions_are_routed_by_invocation_id_whatever_the_order()
        {
            using (var server = new FakeFmpServer(async s =>
            {
                await s.ExpectHandshakeAsync();
                var ids = new Dictionary<string, string>();
                for (int i = 0; i < 4; i++)
                {
                    string frame = await s.ReadFrameAsync();
                    ids[Field(frame, "target")] = Field(frame, "invocationId");
                }
                await s.SendFrameAsync(Complete(ids["Void"], ""));
                await s.SendFrameAsync(Complete(ids["Object"], ",\"result\":{\"n\":[1,2]}"));
                await s.SendFrameAsync(Complete(ids["Null"], ",\"result\":null"));
                await s.SendFrameAsync(Complete(ids["Text"], ",\"result\":\"te\\\"xt\\u00e9\""));
                await s.WaitForCloseAsync();
            }))
            using (var connection = new FmpConnection(server.Url))
            {
                await connection.StartAsync();

                Task<string> text = connection.InvokeAsync("Text", "[1]");
                Task<string> nothing = connection.InvokeAsync("Null", "[]");
                Task<string> obj = connection.InvokeAsync("Object", "[{}]");
                Task<string> none = connection.InvokeAsync("Void", "[]");

                Assert.Equal("\"te\\\"xt\\u00e9\"", await text.WaitAsync(Limit));
                Assert.Equal("null", await nothing.WaitAsync(Limit));
                Assert.Equal("{\"n\":[1,2]}", await obj.WaitAsync(Limit));
                Assert.Equal("null", await none.WaitAsync(Limit));
            }
        }

        [Fact]
        public async Task A_server_error_fails_only_that_call_with_the_unescaped_message()
        {
            using (var server = new FakeFmpServer(async s =>
            {
                await s.ExpectHandshakeAsync();
                string frame = await s.ReadFrameAsync();
                await s.SendFrameAsync(Complete(Field(frame, "invocationId"), ",\"error\":\"An unexpected error occurred invoking \\u0027Register\\u0027 on the server.\""));
                await AnswerAllAsync(s);
            }))
            using (var connection = new FmpConnection(server.Url))
            {
                await connection.StartAsync();

                FmpException error = await Assert.ThrowsAsync<FmpException>(() => connection.InvokeAsync("Register", "[{}]"));

                Assert.Equal("An unexpected error occurred invoking 'Register' on the server.", error.Message);
                Assert.Equal(FmpState.Connected, connection.State);
                Assert.Equal("\"ok\"", await connection.InvokeAsync("Again", "[]"));
            }
        }

        [Fact]
        public async Task Invocation_frames_have_the_exact_signalr_shape()
        {
            var frames = new List<string>();
            var enough = Signal<bool>();
            using (var server = new FakeFmpServer(async s =>
            {
                await s.ExpectHandshakeAsync();
                string frame;
                while ((frame = await s.ReadFrameAsync()) != null)
                {
                    frames.Add(frame);
                    string id = Field(frame, "invocationId");
                    if (id != null)
                        await s.SendFrameAsync(Complete(id, ""));
                    else
                        enough.TrySetResult(true);
                }
            }))
            using (var connection = new FmpConnection(server.Url))
            {
                await connection.StartAsync();

                await connection.InvokeAsync("Register", "[{\"a\":1}]");
                await connection.SendAsync("Heartbeat", "[{\"x\":1}]");
                await enough.Task.WaitAsync(Limit);

                Assert.Equal("{\"type\":1,\"invocationId\":\"1\",\"target\":\"Register\",\"arguments\":[{\"a\":1}]}", frames[0]);
                Assert.Equal("{\"type\":1,\"target\":\"Heartbeat\",\"arguments\":[{\"x\":1}]}", frames[1]);
            }
        }

        [Fact]
        public async Task Arguments_that_are_not_a_json_array_are_refused_without_touching_the_link()
        {
            using (var server = new FakeFmpServer(async s => { await s.ExpectHandshakeAsync(); await AnswerAllAsync(s); }))
            using (var connection = new FmpConnection(server.Url))
            {
                await connection.StartAsync();

                await Assert.ThrowsAsync<ArgumentException>(() => connection.SendAsync("T", "{}"));
                await Assert.ThrowsAsync<ArgumentException>(() => connection.InvokeAsync("T", null));
                await Assert.ThrowsAsync<ArgumentException>(() => connection.InvokeAsync("", "[]"));

                Assert.Equal("\"ok\"", await connection.InvokeAsync("T", "[]"));
            }
        }

        [Fact]
        public async Task A_message_over_the_send_limit_is_refused_and_the_link_survives()
        {
            using (var server = new FakeFmpServer(async s => { await s.ExpectHandshakeAsync(); await AnswerAllAsync(s); }))
            using (var connection = new FmpConnection(server.Url, new FmpOptions { MaxSendMessageSize = 200 }))
            {
                await connection.StartAsync();

                FmpException error = await Assert.ThrowsAsync<FmpException>(() => connection.SendAsync("Big", "[\"" + new string('x', 300) + "\"]"));

                Assert.Contains("limit", error.Message);
                Assert.Equal(FmpState.Connected, connection.State);
                Assert.Equal("\"ok\"", await connection.InvokeAsync("Small", "[1]"));
            }
        }

        [Theory]
        [InlineData(200, true)]
        [InlineData(201, false)]
        public async Task The_send_limit_counts_the_separator_and_is_exact_to_the_byte(int totalBytes, bool accepted)
        {
            int baseBytes = FmpFrame.Invocation(null, "T", "[\"\"]").Length;
            string arguments = "[\"" + new string('x', totalBytes - baseBytes) + "\"]";
            using (var server = new FakeFmpServer(async s => { await s.ExpectHandshakeAsync(); await AnswerAllAsync(s); }))
            using (var connection = new FmpConnection(server.Url, new FmpOptions { MaxSendMessageSize = 200 }))
            {
                await connection.StartAsync();

                Exception error = await Record.ExceptionAsync(() => connection.SendAsync("T", arguments));

                Assert.Equal(accepted, error == null);
                Assert.Equal(FmpState.Connected, connection.State);
            }
        }

        [Fact]
        public async Task A_handler_receives_the_raw_arguments_array_and_target_matching_ignores_case()
        {
            var seen = Signal<string>();
            using (var server = new FakeFmpServer(async s =>
            {
                await s.ExpectHandshakeAsync();
                await s.SendFrameAsync("{\"type\":1,\"target\":\"invokeTOOL\",\"arguments\":[{\"a\":\"b\\\"c\",\"n\":[1,{\"z\":null}]},1,null,\"\\u00e9\"]}");
                await s.WaitForCloseAsync();
            }))
            using (var connection = new FmpConnection(server.Url))
            {
                connection.On("InvokeTool", arguments => { seen.TrySetResult(arguments); return Task.CompletedTask; });
                await connection.StartAsync();

                Assert.Equal("[{\"a\":\"b\\\"c\",\"n\":[1,{\"z\":null}]},1,null,\"\\u00e9\"]", await seen.Task.WaitAsync(Limit));
            }
        }

        [Fact]
        public async Task A_slow_handler_does_not_hold_up_the_next_invocation()
        {
            var release = Signal<bool>();
            var fastRan = Signal<bool>();
            using (var server = new FakeFmpServer(async s =>
            {
                await s.ExpectHandshakeAsync();
                await s.SendFrameAsync("{\"type\":1,\"target\":\"InvokeTool\",\"arguments\":[]}");
                await s.SendFrameAsync("{\"type\":1,\"target\":\"CancelTool\",\"arguments\":[\"op\"]}");
                await s.WaitForCloseAsync();
            }))
            using (var connection = new FmpConnection(server.Url))
            {
                connection.On("InvokeTool", _ => release.Task);
                connection.On("CancelTool", _ => { fastRan.TrySetResult(true); return Task.CompletedTask; });
                await connection.StartAsync();

                await fastRan.Task.WaitAsync(Limit);

                Assert.False(release.Task.IsCompleted);
                release.TrySetResult(true);
            }
        }

        [Fact]
        public async Task An_invocation_that_carries_an_id_is_answered_with_a_completion()
        {
            var answers = new Dictionary<string, string>();
            var all = Signal<bool>();
            var failures = new List<string>();
            using (var server = new FakeFmpServer(async s =>
            {
                await s.ExpectHandshakeAsync();
                await s.SendFrameAsync("{\"type\":1,\"invocationId\":\"a1\",\"target\":\"Fine\",\"arguments\":[]}");
                await s.SendFrameAsync("{\"type\":1,\"invocationId\":\"a2\",\"target\":\"Breaks\",\"arguments\":[]}");
                await s.SendFrameAsync("{\"type\":1,\"invocationId\":\"a3\",\"target\":\"Nobody\",\"arguments\":[]}");
                for (int i = 0; i < 3; i++)
                {
                    string frame = await s.ReadFrameAsync();
                    lock (answers)
                    {
                        answers[Field(frame, "invocationId")] = frame;
                        if (answers.Count == 3)
                            all.TrySetResult(true);
                    }
                }
                await s.WaitForCloseAsync();
            }))
            using (var connection = new FmpConnection(server.Url))
            {
                connection.On("Fine", _ => Task.CompletedTask);
                connection.On("Breaks", _ => throw new InvalidOperationException("boom"));
                connection.HandlerFailed += (target, error) => { lock (failures) failures.Add(target + ":" + error.Message); };
                await connection.StartAsync();

                await all.Task.WaitAsync(Limit);

                Assert.Equal("{\"type\":3,\"invocationId\":\"a1\",\"result\":null}", answers["a1"]);
                Assert.Equal("{\"type\":3,\"invocationId\":\"a2\",\"error\":\"boom\"}", answers["a2"]);
                Assert.Equal("{\"type\":3,\"invocationId\":\"a3\",\"error\":\"No handler is registered for \\u0027Nobody\\u0027.\"}".Replace("\\u0027", "'"), answers["a3"]);
                await Json.WaitUntilAsync(() => { lock (failures) return failures.Count == 2; }, what: "both HandlerFailed events");
                Assert.Equal(FmpState.Connected, connection.State);
            }
        }

        [Fact]
        public async Task An_invocation_without_a_handler_raises_HandlerFailed_and_keeps_the_link()
        {
            var failed = Signal<string>();
            using (var server = new FakeFmpServer(async s =>
            {
                await s.ExpectHandshakeAsync();
                await s.SendFrameAsync("{\"type\":1,\"target\":\"Unknown\",\"arguments\":[]}");
                await AnswerAllAsync(s);
            }))
            using (var connection = new FmpConnection(server.Url))
            {
                connection.HandlerFailed += (target, error) => failed.TrySetResult(target);
                await connection.StartAsync();

                Assert.Equal("Unknown", await failed.Task.WaitAsync(Limit));
                Assert.Equal("\"ok\"", await connection.InvokeAsync("Still", "[]"));
            }
        }

        [Theory]
        [InlineData(1000, 100_000)]
        [InlineData(1, 300)]
        [InlineData(4093, 70_000)]
        public async Task A_frame_cut_into_websocket_fragments_is_reassembled(int fragmentSize, int chars)
        {
            string text = Payload(chars);
            string frame = "{\"type\":1,\"target\":\"Big\",\"arguments\":[" + JsonSerializer.Serialize(text, Json.Relaxed) + "]}";
            var seen = Signal<string>();
            using (var server = new FakeFmpServer(async s =>
            {
                await s.ExpectHandshakeAsync();
                await s.SendMessageAsync(Encoding.UTF8.GetBytes(frame + "\u001e"), fragmentSize);
                await s.WaitForCloseAsync();
            }))
            using (var connection = new FmpConnection(server.Url))
            {
                connection.On("Big", arguments => { seen.TrySetResult(arguments); return Task.CompletedTask; });
                await connection.StartAsync();

                string arguments = await seen.Task.WaitAsync(Limit);

                Assert.Equal("[" + JsonSerializer.Serialize(text, Json.Relaxed) + "]", arguments);
            }
        }

        [Fact]
        public async Task Frames_may_share_a_message_or_straddle_messages_and_fragments_may_split_a_character()
        {
            string Frame(string tag) => "{\"type\":1,\"target\":\"Tag\",\"arguments\":[\"" + tag + "\"]}";
            byte[] b = Encoding.UTF8.GetBytes(Frame("B \U0001F600 emoji"));
            int emoji = Array.IndexOf(b, (byte)0xF0);
            byte[] bFirst = b.Take(emoji).ToArray();
            byte[] bRest = b.Skip(emoji).ToArray();
            var seen = new List<string>();
            var all = Signal<bool>();
            using (var server = new FakeFmpServer(async s =>
            {
                await s.ExpectHandshakeAsync();
                await s.SendMessageAsync(Encoding.UTF8.GetBytes(Frame("A")).Concat(bFirst).ToArray());
                await s.SendMessageAsync(bRest.Concat(Encoding.UTF8.GetBytes(Frame("C"))).ToArray(), 3);
                await s.SendMessageAsync(Encoding.UTF8.GetBytes(Frame("D").TrimEnd('')));
                await s.SendMessageAsync(new byte[] { 0x1E });
                await s.SendMessageAsync(Encoding.UTF8.GetBytes("{\"type\":6}{\"type\":6}" + Frame("E")));
                await s.WaitForCloseAsync();
            }))
            using (var connection = new FmpConnection(server.Url))
            {
                connection.On("Tag", arguments =>
                {
                    lock (seen)
                    {
                        seen.Add(arguments);
                        if (seen.Count == 5)
                            all.TrySetResult(true);
                    }
                    return Task.CompletedTask;
                });
                await connection.StartAsync();

                await all.Task.WaitAsync(Limit);

                lock (seen)
                    Assert.Equal(new[] { "[\"A\"]", "[\"B \U0001F600 emoji\"]", "[\"C\"]", "[\"D\"]", "[\"E\"]" }, seen.OrderBy(x => x, StringComparer.Ordinal).ToArray());
                Assert.Equal(FmpState.Connected, connection.State);
            }
        }

        [Fact]
        public async Task A_thousand_frames_in_one_message_are_all_delivered()
        {
            int count = 0;
            var all = Signal<bool>();
            string frame = "{\"type\":1,\"target\":\"Tick\",\"arguments\":[1]}\u001e";
            using (var server = new FakeFmpServer(async s =>
            {
                await s.ExpectHandshakeAsync();
                await s.SendMessageAsync(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(frame, 1000))));
                await s.WaitForCloseAsync();
            }))
            using (var connection = new FmpConnection(server.Url))
            {
                connection.On("Tick", _ =>
                {
                    if (Interlocked.Increment(ref count) == 1000)
                        all.TrySetResult(true);
                    return Task.CompletedTask;
                });
                await connection.StartAsync();

                await all.Task.WaitAsync(Limit);
            }
        }

        [Fact]
        public async Task Ten_megabytes_are_received_and_sent_quickly()
        {
            string text = Payload(10 * 1024 * 1024);
            string args = "[" + JsonSerializer.Serialize(text, Json.Relaxed) + "]";
            byte[] frame = Encoding.UTF8.GetBytes("{\"type\":1,\"target\":\"Big\",\"arguments\":" + args + "}\u001e");
            long sendStarted = 0;
            long clientReceived = 0;
            long serverReceived = 0;
            string serverSaw = null;
            var received = Signal<string>();
            var uploaded = Signal<bool>();
            using (var server = new FakeFmpServer(async s =>
            {
                await s.ExpectHandshakeAsync();
                sendStarted = Stopwatch.GetTimestamp();
                await s.SendMessageAsync(frame, 8192);
                string upload = await s.ReadFrameAsync();
                serverReceived = Stopwatch.GetTimestamp();
                serverSaw = upload;
                uploaded.TrySetResult(true);
                await s.WaitForCloseAsync();
            }))
            using (var connection = new FmpConnection(server.Url))
            {
                connection.On("Big", arguments => { clientReceived = Stopwatch.GetTimestamp(); received.TrySetResult(arguments); return Task.CompletedTask; });
                await connection.StartAsync();
                string arguments = await received.Task.WaitAsync(Limit);
                Assert.Equal(args.Length, arguments.Length);
                Assert.Equal(args, arguments);

                long uploadStarted = Stopwatch.GetTimestamp();
                await connection.SendAsync("Big", arguments);
                await uploaded.Task.WaitAsync(Limit);

                double downMs = (clientReceived - sendStarted) * 1000.0 / Stopwatch.Frequency;
                double upMs = (serverReceived - uploadStarted) * 1000.0 / Stopwatch.Frequency;
                double mib = frame.Length / 1048576.0;
                _output.WriteLine("frame {0:N2} MiB: received in {1:N0} ms ({2:N0} MiB/s, 8 KiB fragments), sent in {3:N0} ms ({4:N0} MiB/s)", mib, downMs, mib * 1000 / downMs, upMs, mib * 1000 / upMs);
                Assert.Equal("{\"type\":1,\"target\":\"Big\",\"arguments\":" + args + "}", serverSaw);
                Assert.True(downMs < 5000 && upMs < 5000);
            }
        }

        [Fact]
        public async Task A_frame_over_the_receive_limit_fails_the_link()
        {
            using (var server = new FakeFmpServer(async s =>
            {
                await s.ExpectHandshakeAsync();
                await s.SendMessageAsync(Encoding.UTF8.GetBytes("{\"type\":1,\"target\":\"Big\",\"arguments\":[\"" + new string('x', 2 * 1024 * 1024) + "\"]}\u001e"), 16384);
                await s.WaitForCloseAsync();
            }))
            using (var connection = new FmpConnection(server.Url, new FmpOptions { MaxReceiveMessageSize = 1024 * 1024, ReconnectDelays = NoRetry }))
            {
                var spy = new Spy(connection);
                await connection.StartAsync();

                Exception error = await spy.Closed.Task.WaitAsync(Limit);

                Assert.IsType<FmpException>(error);
                Assert.Contains("limit", error.Message);
                Assert.Equal(FmpState.Disconnected, connection.State);
            }
        }

        [Theory]
        [InlineData(99_999, true)]
        [InlineData(100_000, false)]
        public async Task The_receive_limit_counts_the_separator_and_is_exact_to_the_byte(int frameBytes, bool accepted)
        {
            string head = "{\"type\":1,\"target\":\"Pad\",\"arguments\":[\"";
            string tail = "\"]}";
            string frame = head + new string('x', frameBytes - head.Length - tail.Length) + tail;
            var delivered = Signal<bool>();
            using (var server = new FakeFmpServer(async s =>
            {
                await s.ExpectHandshakeAsync();
                await s.SendMessageAsync(Encoding.UTF8.GetBytes(frame + ""), 7000);
                await s.WaitForCloseAsync();
            }))
            using (var connection = new FmpConnection(server.Url, new FmpOptions { MaxReceiveMessageSize = 100_000, ReconnectDelays = NoRetry }))
            {
                var spy = new Spy(connection);
                connection.On("Pad", _ => { delivered.TrySetResult(true); return Task.CompletedTask; });
                await connection.StartAsync();

                Task first = await Task.WhenAny(delivered.Task, spy.Closed.Task).WaitAsync(Limit);

                Assert.Equal(accepted, ReferenceEquals(first, delivered.Task));
            }
        }

        [Fact]
        public async Task Concurrent_senders_and_pings_never_interleave_frames()
        {
            const int count = 400;
            var seen = new HashSet<int>();
            int damaged = 0;
            var all = Signal<bool>();
            using (var server = new FakeFmpServer(async s =>
            {
                await s.ExpectHandshakeAsync();
                string frame;
                while ((frame = await s.ReadFrameAsync()) != null)
                {
                    if (frame == "{\"type\":6}")
                        continue;
                    try
                    {
                        using (JsonDocument document = JsonDocument.Parse(frame))
                        {
                            JsonElement first = document.RootElement.GetProperty("arguments")[0];
                            if (first.GetProperty("pad").GetString().Length != 20_000)
                                Interlocked.Increment(ref damaged);
                            lock (seen)
                            {
                                seen.Add(first.GetProperty("n").GetInt32());
                                if (seen.Count == count)
                                    all.TrySetResult(true);
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        Interlocked.Increment(ref damaged);
                    }
                }
            }))
            using (var connection = new FmpConnection(server.Url, new FmpOptions { KeepAliveInterval = TimeSpan.FromMilliseconds(5) }))
            {
                await connection.StartAsync();
                string pad = new string('p', 20_000);

                await Task.WhenAll(Enumerable.Range(0, count).Select(n => Task.Run(() => connection.SendAsync("Many", "[{\"n\":" + n + ",\"pad\":\"" + pad + "\"}]"))));
                await all.Task.WaitAsync(Limit);

                Assert.Equal(0, Volatile.Read(ref damaged));
                Assert.Equal(FmpState.Connected, connection.State);
            }
        }

        [Fact]
        public async Task The_client_sends_a_ping_whenever_it_has_been_idle_for_the_keepalive_interval()
        {
            int pings = 0;
            using (var server = new FakeFmpServer(async s =>
            {
                await s.ExpectHandshakeAsync();
                string frame;
                while ((frame = await s.ReadFrameAsync()) != null)
                {
                    if (frame == "{\"type\":6}")
                        Interlocked.Increment(ref pings);
                }
            }))
            using (var connection = new FmpConnection(server.Url, new FmpOptions { KeepAliveInterval = TimeSpan.FromMilliseconds(100) }))
            {
                await connection.StartAsync();

                await Task.Delay(1050);

                int seen = Volatile.Read(ref pings);
                _output.WriteLine("{0} pings in 1.05 s at a 100 ms interval", seen);
                Assert.InRange(seen, 6, 12);
                Assert.Equal(FmpState.Connected, connection.State);
            }
        }

        [Fact]
        public async Task Regular_traffic_replaces_pings()
        {
            int pings = 0;
            using (var server = new FakeFmpServer(async s =>
            {
                await s.ExpectHandshakeAsync();
                string frame;
                while ((frame = await s.ReadFrameAsync()) != null)
                {
                    if (frame == "{\"type\":6}")
                        Interlocked.Increment(ref pings);
                }
            }))
            using (var connection = new FmpConnection(server.Url, new FmpOptions { KeepAliveInterval = TimeSpan.FromMilliseconds(300) }))
            {
                await connection.StartAsync();

                for (int i = 0; i < 20; i++)
                {
                    await connection.SendAsync("Progress", "[" + i + "]");
                    await Task.Delay(50);
                }

                Assert.Equal(0, Volatile.Read(ref pings));
            }
        }

        [Fact]
        public async Task A_silent_server_is_declared_dead_after_the_server_timeout()
        {
            using (var server = new FakeFmpServer(async s => { await s.ExpectHandshakeAsync(); await s.WaitForCloseAsync(); }))
            using (var connection = new FmpConnection(server.Url, new FmpOptions { KeepAliveInterval = TimeSpan.FromMilliseconds(100), ServerTimeout = TimeSpan.FromMilliseconds(500), ReconnectDelays = NoRetry }))
            {
                var spy = new Spy(connection);
                await connection.StartAsync();
                var watch = Stopwatch.StartNew();

                Exception error = await spy.Closed.Task.WaitAsync(Limit);

                Assert.IsType<TimeoutException>(error);
                Assert.InRange(watch.ElapsedMilliseconds, 300, 3000);
                Assert.Equal(new[] { FmpState.Connecting, FmpState.Connected, FmpState.Disconnected }, spy.States);
            }
        }

        [Fact]
        public async Task Server_pings_keep_a_quiet_link_alive()
        {
            using (var server = new FakeFmpServer(async s =>
            {
                await s.ExpectHandshakeAsync();
                for (int i = 0; i < 16; i++)
                {
                    await Task.Delay(100);
                    await s.SendFrameAsync("{\"type\":6}");
                }
                await s.WaitForCloseAsync();
            }))
            using (var connection = new FmpConnection(server.Url, new FmpOptions { ServerTimeout = TimeSpan.FromMilliseconds(500), ReconnectDelays = NoRetry }))
            {
                var spy = new Spy(connection);
                await connection.StartAsync();

                await Task.Delay(1500);

                Assert.Equal(FmpState.Connected, connection.State);
                Assert.False(spy.Closed.Task.IsCompleted);
            }
        }

        [Fact]
        public async Task A_dropped_link_fails_pending_calls_with_a_clear_exception()
        {
            using (var server = new FakeFmpServer(async s => { await s.ExpectHandshakeAsync(); await s.ReadFrameAsync(); s.Drop(); }))
            using (var connection = new FmpConnection(server.Url, new FmpOptions { ReconnectDelays = NoRetry }))
            {
                var spy = new Spy(connection);
                await connection.StartAsync();

                FmpException error = await Assert.ThrowsAsync<FmpException>(() => connection.InvokeAsync("Slow", "[]").WaitAsync(Limit));

                Assert.Contains("lost", error.Message);
                Assert.NotNull(error.InnerException);
                await spy.Closed.Task.WaitAsync(Limit);
                Assert.Equal(new[] { FmpState.Connecting, FmpState.Connected, FmpState.Disconnected }, spy.States);
                await Assert.ThrowsAsync<InvalidOperationException>(() => connection.SendAsync("Late", "[]"));
            }
        }

        [Fact]
        public async Task A_dropped_link_is_reconnected_with_a_fresh_handshake()
        {
            var handshakes = new List<string>();
            using (var server = new FakeFmpServer(async s =>
            {
                string handshake = await s.ExpectHandshakeAsync();
                lock (handshakes)
                    handshakes.Add(handshake);
                if (s.Number == 1)
                    s.Drop();
                else
                    await AnswerAllAsync(s);
            }))
            using (var connection = new FmpConnection(server.Url, new FmpOptions { ReconnectDelays = new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(50) } }))
            {
                var spy = new Spy(connection);
                await connection.StartAsync();

                await Json.WaitUntilAsync(() => spy.States.Length == 4, what: "the reconnect");

                Assert.Equal(new[] { FmpState.Connecting, FmpState.Connected, FmpState.Reconnecting, FmpState.Connected }, spy.States);
                Assert.Equal(2, handshakes.Count);
                Assert.Equal("\"ok\"", await connection.InvokeAsync("Again", "[]"));
                Assert.False(spy.Closed.Task.IsCompleted);
            }
        }

        [Fact]
        public async Task A_reconnect_policy_that_runs_out_raises_Closed_once_with_the_last_error()
        {
            using (var server = new FakeFmpServer(async s =>
            {
                if (s.Number == 1)
                    await s.ExpectHandshakeAsync();
                else
                    await s.ReadFrameAsync();
                s.Drop();
            }))
            using (var connection = new FmpConnection(server.Url, new FmpOptions { ReconnectDelays = new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50) } }))
            {
                int closedCount = 0;
                connection.Closed += _ => Interlocked.Increment(ref closedCount);
                var spy = new Spy(connection);
                await connection.StartAsync();

                Exception error = await spy.Closed.Task.WaitAsync(Limit);
                await Task.Delay(300);

                Assert.Equal(1, closedCount);
                Assert.Equal(FmpState.Disconnected, connection.State);
                Assert.NotNull(error);
                Assert.Equal(FmpState.Reconnecting, spy.States[2]);
                Assert.Equal(FmpState.Disconnected, spy.States.Last());
            }
        }

        [Fact]
        public async Task A_close_message_without_an_error_ends_the_link_without_reconnecting()
        {
            using (var server = new FakeFmpServer(async s => { await s.ExpectHandshakeAsync(); await s.SendFrameAsync("{\"type\":7}"); await s.WaitForCloseAsync(); }))
            using (var connection = new FmpConnection(server.Url))
            {
                var spy = new Spy(connection);
                await connection.StartAsync();

                Exception error = await spy.Closed.Task.WaitAsync(Limit);
                await Task.Delay(500);

                Assert.Contains("closed", error.Message);
                Assert.Equal(1, server.Connections);
                Assert.Equal(new[] { FmpState.Connecting, FmpState.Connected, FmpState.Disconnected }, spy.States);
            }
        }

        [Theory]
        [InlineData("{\"type\":7,\"error\":\"Connection closed with an error.\",\"allowReconnect\":true}")]
        [InlineData("{\"type\":7,\"error\":\"boom\"}")]
        [InlineData("{\"type\":7,\"allowReconnect\":true}")]
        public async Task A_close_message_with_an_error_or_allowReconnect_triggers_a_reconnect(string close)
        {
            using (var server = new FakeFmpServer(async s =>
            {
                await s.ExpectHandshakeAsync();
                if (s.Number == 1)
                    await s.SendFrameAsync(close);
                await AnswerAllAsync(s);
            }))
            using (var connection = new FmpConnection(server.Url))
            {
                var spy = new Spy(connection);
                await connection.StartAsync();

                await Json.WaitUntilAsync(() => spy.States.Length == 4, what: "the reconnect");

                Assert.Equal(new[] { FmpState.Connecting, FmpState.Connected, FmpState.Reconnecting, FmpState.Connected }, spy.States);
                Assert.Equal(2, server.Connections);
            }
        }

        [Fact]
        public async Task A_websocket_close_without_a_hub_close_message_ends_the_link_without_reconnecting()
        {
            using (var server = new FakeFmpServer(async s => { await s.ExpectHandshakeAsync(); await s.CloseAsync(); await s.WaitForCloseAsync(); }))
            using (var connection = new FmpConnection(server.Url))
            {
                var spy = new Spy(connection);
                await connection.StartAsync();

                await spy.Closed.Task.WaitAsync(Limit);

                Assert.Equal(1, server.Connections);
                Assert.Equal(FmpState.Disconnected, connection.State);
            }
        }

        [Theory]
        [InlineData("{\"type\":4,\"invocationId\":\"1\",\"target\":\"Stream\",\"arguments\":[]}", "unsupported message type 4")]
        [InlineData("{\"type\":2,\"invocationId\":\"1\",\"item\":1}", "unsupported message type 2")]
        [InlineData("{\"type\":5,\"invocationId\":\"1\"}", "unsupported message type 5")]
        [InlineData("{\"type\":99}", "unsupported message type 99")]
        [InlineData("{\"target\":\"NoType\"}", "unsupported message type -1")]
        [InlineData("{\"type\":", "malformed")]
        [InlineData("not json at all", "malformed")]
        [InlineData("{\"type\":1,\"arguments\":[]}", "without a target")]
        public async Task Protocol_violations_fail_the_link(string frame, string expected)
        {
            using (var server = new FakeFmpServer(async s => { await s.ExpectHandshakeAsync(); await s.SendFrameAsync(frame); await s.WaitForCloseAsync(); }))
            using (var connection = new FmpConnection(server.Url, new FmpOptions { ReconnectDelays = NoRetry }))
            {
                var spy = new Spy(connection);
                await connection.StartAsync();

                Exception error = await spy.Closed.Task.WaitAsync(Limit);

                Assert.IsType<FmpException>(error);
                Assert.Contains(expected, error.Message);
            }
        }

        [Fact]
        public async Task A_cancelled_call_is_abandoned_and_a_late_completion_for_it_is_ignored()
        {
            using (var server = new FakeFmpServer(async s =>
            {
                await s.ExpectHandshakeAsync();
                string first = await s.ReadFrameAsync();
                string second = await s.ReadFrameAsync();
                await s.SendFrameAsync(Complete(Field(first, "invocationId"), ",\"result\":\"late\""));
                await s.SendFrameAsync(Complete(Field(second, "invocationId"), ",\"result\":\"second\""));
                await s.WaitForCloseAsync();
            }))
            using (var connection = new FmpConnection(server.Url))
            using (var cancel = new CancellationTokenSource())
            {
                await connection.StartAsync();
                Task<string> abandoned = connection.InvokeAsync("Never", "[]", cancel.Token);
                cancel.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);

                Assert.Equal("\"second\"", await connection.InvokeAsync("Second", "[]").WaitAsync(Limit));
                Assert.Equal(FmpState.Connected, connection.State);
            }
        }

        [Fact]
        public async Task StopAsync_is_graceful_idempotent_and_raises_StateChanged_but_not_Closed()
        {
            var clientClosed = Signal<bool>();
            using (var server = new FakeFmpServer(async s => { await s.ExpectHandshakeAsync(); await s.WaitForCloseAsync(); clientClosed.TrySetResult(true); }))
            using (var connection = new FmpConnection(server.Url))
            {
                var spy = new Spy(connection);
                await connection.StartAsync();

                await connection.StopAsync();
                await connection.StopAsync();

                await clientClosed.Task.WaitAsync(Limit);
                Assert.Equal(new[] { FmpState.Connecting, FmpState.Connected, FmpState.Disconnected }, spy.States);
                Assert.False(spy.Closed.Task.IsCompleted);
                await connection.StartAsync();
                Assert.Equal(FmpState.Connected, connection.State);
            }
        }

        [Fact]
        public async Task StopAsync_gives_up_waiting_for_a_peer_that_ignores_the_close()
        {
            using (var server = new FakeFmpServer(async s => { await s.ExpectHandshakeAsync(); await Task.Delay(Timeout.Infinite); }))
            using (var connection = new FmpConnection(server.Url))
            {
                await connection.StartAsync();
                Task<string> pending = connection.InvokeAsync("Never", "[]");
                var watch = Stopwatch.StartNew();

                await connection.StopAsync();

                _output.WriteLine("StopAsync against a deaf peer: {0} ms", watch.ElapsedMilliseconds);
                Assert.True(watch.ElapsedMilliseconds < 2500, "StopAsync took " + watch.ElapsedMilliseconds + " ms");
                Assert.Equal(FmpState.Disconnected, connection.State);
                await Assert.ThrowsAsync<FmpException>(() => pending.WaitAsync(Limit));
            }
        }

        [Fact]
        public async Task Dispose_returns_at_once_fails_pending_calls_and_raises_no_events()
        {
            using (var server = new FakeFmpServer(async s => { await s.ExpectHandshakeAsync(); await Task.Delay(Timeout.Infinite); }))
            {
                var connection = new FmpConnection(server.Url);
                var spy = new Spy(connection);
                await connection.StartAsync();
                Task<string> pending = connection.InvokeAsync("Never", "[]");

                var watch = Stopwatch.StartNew();
                connection.Dispose();
                connection.Dispose();
                long ms = watch.ElapsedMilliseconds;

                _output.WriteLine("Dispose with a pending call: {0} ms", ms);
                Assert.True(ms < 200, "Dispose took " + ms + " ms");
                await Assert.ThrowsAsync<FmpException>(() => pending.WaitAsync(Limit));
                await Task.Delay(100);
                Assert.Equal(new[] { FmpState.Connecting, FmpState.Connected }, spy.States);
                Assert.False(spy.Closed.Task.IsCompleted);
                await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.InvokeAsync("X", "[]"));
                await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.StartAsync());
                await connection.StopAsync();
            }
        }

        [Fact]
        public async Task Disposing_during_a_reconnect_stops_it()
        {
            using (var server = new FakeFmpServer(async s => { await s.ExpectHandshakeAsync(); s.Drop(); }))
            {
                var connection = new FmpConnection(server.Url, new FmpOptions { ReconnectDelays = new[] { TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300) } });
                var spy = new Spy(connection);
                await connection.StartAsync();
                await Json.WaitUntilAsync(() => connection.State == FmpState.Reconnecting, what: "Reconnecting");

                connection.Dispose();
                await Task.Delay(800);

                Assert.Equal(1, server.Connections);
                Assert.False(spy.Closed.Task.IsCompleted);
            }
        }

        [Fact]
        public async Task StopAsync_during_a_reconnect_ends_in_disconnected_without_Closed()
        {
            using (var server = new FakeFmpServer(async s => { await s.ExpectHandshakeAsync(); s.Drop(); }))
            using (var connection = new FmpConnection(server.Url, new FmpOptions { ReconnectDelays = new[] { TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(400) } }))
            {
                var spy = new Spy(connection);
                await connection.StartAsync();
                await Json.WaitUntilAsync(() => connection.State == FmpState.Reconnecting, what: "Reconnecting");

                await connection.StopAsync();
                await Task.Delay(900);

                Assert.Equal(FmpState.Disconnected, connection.State);
                Assert.Equal(1, server.Connections);
                Assert.Equal(new[] { FmpState.Connecting, FmpState.Connected, FmpState.Reconnecting, FmpState.Disconnected }, spy.States);
                Assert.False(spy.Closed.Task.IsCompleted);
            }
        }

        [Fact]
        public async Task A_subscriber_can_call_back_into_the_connection_from_StateChanged()
        {
            using (var server = new FakeFmpServer(async s => { await s.ExpectHandshakeAsync(); await s.WaitForCloseAsync(); }))
            using (var connection = new FmpConnection(server.Url))
            {
                var spy = new Spy(connection);
                FmpState seen = FmpState.Disconnected;
                Task stopping = null;
                connection.StateChanged += state =>
                {
                    if (state == FmpState.Connected)
                    {
                        seen = connection.State;
                        stopping = connection.StopAsync();
                    }
                };

                await connection.StartAsync().WaitAsync(Limit);
                await stopping.WaitAsync(Limit);

                Assert.Equal(FmpState.Connected, seen);
                Assert.Equal(FmpState.Disconnected, connection.State);
                Assert.Equal(new[] { FmpState.Connecting, FmpState.Connected, FmpState.Disconnected }, spy.States);
            }
        }

        [Fact]
        public async Task A_throwing_subscriber_cannot_break_the_connection()
        {
            using (var server = new FakeFmpServer(async s => { await s.ExpectHandshakeAsync(); await AnswerAllAsync(s); }))
            using (var connection = new FmpConnection(server.Url))
            {
                connection.StateChanged += _ => throw new InvalidOperationException("subscriber bug");
                connection.Closed += _ => throw new InvalidOperationException("subscriber bug");

                await connection.StartAsync();

                Assert.Equal("\"ok\"", await connection.InvokeAsync("Fine", "[]"));
            }
        }

        [Fact]
        public async Task The_api_refuses_misuse_with_clear_exceptions()
        {
            Assert.Throws<ArgumentNullException>(() => new FmpConnection(null));
            Assert.Throws<ArgumentException>(() => new FmpConnection(new Uri("/fmp", UriKind.Relative)));
            Assert.Throws<ArgumentException>(() => new FmpConnection(new Uri("ftp://127.0.0.1/fmp")));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FmpConnection(new Uri("ws://127.0.0.1/fmp"), new FmpOptions { KeepAliveInterval = TimeSpan.Zero }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FmpConnection(new Uri("ws://127.0.0.1/fmp"), new FmpOptions { MaxSendMessageSize = 0 }));
            using (var server = new FakeFmpServer(async s => { await s.ExpectHandshakeAsync(); await s.WaitForCloseAsync(); }))
            using (var connection = new FmpConnection(server.Url))
            {
                Assert.Throws<ArgumentException>(() => connection.On("", _ => Task.CompletedTask));
                Assert.Throws<ArgumentNullException>(() => connection.On("X", null));
                await Assert.ThrowsAsync<InvalidOperationException>(() => connection.InvokeAsync("X", "[]"));
                await Assert.ThrowsAsync<InvalidOperationException>(() => connection.SendAsync("X", "[]"));

                await connection.StartAsync();

                await Assert.ThrowsAsync<InvalidOperationException>(() => connection.StartAsync());
            }
        }

        [Theory]
        [InlineData("http")]
        [InlineData("ws")]
        public async Task Http_and_ws_urls_both_reach_the_websocket_endpoint(string scheme)
        {
            using (var server = new FakeFmpServer(async s => { await s.ExpectHandshakeAsync(); await s.WaitForCloseAsync(); }))
            using (var connection = new FmpConnection(new Uri(scheme + "://127.0.0.1:" + server.Port + "/fmp")))
            {
                await connection.StartAsync();

                Assert.Equal(FmpState.Connected, connection.State);
            }
        }

        [Fact]
        public async Task Failure_paths_leave_no_unobserved_task_exceptions()
        {
            int unobserved = 0;
            void OnUnobserved(object sender, UnobservedTaskExceptionEventArgs args) => Interlocked.Increment(ref unobserved);
            TaskScheduler.UnobservedTaskException += OnUnobserved;
            try
            {
                for (int round = 0; round < 3; round++)
                {
                    using (var server = new FakeFmpServer(async s =>
                    {
                        await s.ExpectHandshakeAsync();
                        if (s.Number == 1)
                        {
                            await s.ReadFrameAsync();
                            await s.SendFrameAsync("{\"type\":1,\"target\":\"Boom\",\"arguments\":[]}");
                            await Task.Delay(100);
                            s.Drop();
                        }
                        else
                            await AnswerAllAsync(s);
                    }))
                    {
                        var connection = new FmpConnection(server.Url, new FmpOptions { ReconnectDelays = new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(30) } });
                        connection.On("Boom", _ => throw new InvalidOperationException("handler bug"));
                        await connection.StartAsync();
                        using (var cancel = new CancellationTokenSource())
                        {
                            Task cancelled = connection.InvokeAsync("Cancel", "[]", cancel.Token);
                            cancel.Cancel();
                            await Record.ExceptionAsync(() => cancelled);
                        }
                        await Record.ExceptionAsync(() => connection.InvokeAsync("Dies", "[]").WaitAsync(Limit));
                        await Json.WaitUntilAsync(() => connection.State == FmpState.Connected, what: "the reconnect");
                        Task pending = connection.InvokeAsync("Pending", "[]");
                        await pending;
                        Task stuck = connection.InvokeAsync("Stuck", "[]");
                        connection.Dispose();
                        await Record.ExceptionAsync(() => stuck.WaitAsync(Limit));
                    }
                }

                for (int i = 0; i < 3; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }

                Assert.Equal(0, Volatile.Read(ref unobserved));
            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= OnUnobserved;
            }
        }
    }
}
