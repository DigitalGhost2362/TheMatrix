using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Feeder.Fmp
{
    public sealed class FmpConnection : IDisposable
    {
        const int InitialBuffer = 16 * 1024;
        const int ShrinkAbove = 1024 * 1024;
        static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(1);
        static readonly byte[] HandshakeFrame = Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e");
        static readonly byte[] PingFrame = Encoding.UTF8.GetBytes("{\"type\":6}\u001e");

        readonly Uri _url;
        readonly TimeSpan[] _delays;
        readonly TimeSpan _keepAlive;
        readonly TimeSpan _serverTimeout;
        readonly int _maxSend;
        readonly int _maxReceive;
        readonly ConcurrentDictionary<string, Func<string, Task>> _handlers =
            new ConcurrentDictionary<string, Func<string, Task>>(StringComparer.OrdinalIgnoreCase);
        readonly Queue<Action> _events = new Queue<Action>();
        readonly object _gate = new object();
        FmpState _state;
        Link _link;
        CancellationTokenSource _life;
        bool _pumping;
        bool _disposed;

        public FmpConnection(Uri url, FmpOptions options = null)
        {
            if (url == null)
                throw new ArgumentNullException(nameof(url));
            if (!url.IsAbsoluteUri)
                throw new ArgumentException("An absolute URL is required.", nameof(url));
            options = options ?? new FmpOptions();
            if (options.KeepAliveInterval <= TimeSpan.Zero || options.ServerTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "KeepAliveInterval and ServerTimeout must be positive.");
            if (options.MaxSendMessageSize <= 0 || options.MaxReceiveMessageSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "The message size limits must be positive.");
            _url = ToWebSocketUrl(url);
            _delays = (TimeSpan[])(options.ReconnectDelays ?? Array.Empty<TimeSpan>()).Clone();
            _keepAlive = options.KeepAliveInterval;
            _serverTimeout = options.ServerTimeout;
            _maxSend = options.MaxSendMessageSize;
            _maxReceive = options.MaxReceiveMessageSize;
        }

        public FmpState State
        {
            get
            {
                lock (_gate)
                    return _state;
            }
        }

        public event Action<FmpState> StateChanged;

        public event Action<Exception> Closed;

        public event Action<string, Exception> HandlerFailed;

        public void On(string target, Func<string, Task> handler)
        {
            if (string.IsNullOrEmpty(target))
                throw new ArgumentException("A target is required.", nameof(target));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            _handlers[target] = handler;
        }

        public async Task StartAsync(CancellationToken ct = default)
        {
            CancellationTokenSource life;
            lock (_gate)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(FmpConnection));
                if (_state != FmpState.Disconnected)
                    throw new InvalidOperationException("The connection is already started (" + _state + ").");
                life = _life = new CancellationTokenSource();
                Transition(FmpState.Connecting);
            }
            Pump();
            Link link = null;
            bool installed = false;
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, life.Token))
                    link = await ConnectLinkAsync(linked.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_disposed || life.IsCancellationRequested)
                        throw new OperationCanceledException();
                    _link = link;
                    installed = true;
                    Transition(FmpState.Connected);
                    StartLoops(link);
                }
            }
            catch (Exception)
            {
                if (link != null && !installed)
                    Kill(link, new FmpException("The connection was stopped."));
                lock (_gate)
                {
                    if (_state == FmpState.Connecting && ReferenceEquals(_life, life))
                        Transition(FmpState.Disconnected);
                }
                Pump();
                throw;
            }
            Pump();
        }

        public async Task StopAsync()
        {
            Link link;
            CancellationTokenSource life;
            lock (_gate)
            {
                if (_disposed)
                    return;
                link = _link;
                life = _life;
                _link = null;
                if (_state != FmpState.Disconnected)
                    Transition(FmpState.Disconnected);
            }
            Pump();
            life?.Cancel();
            if (link == null)
                return;
            link.Closing = true;
            try
            {
                using (var limit = new CancellationTokenSource(CloseTimeout))
                    await link.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, limit.Token).ConfigureAwait(false);
                await Task.WhenAny(link.ReadTask, Task.Delay(CloseTimeout)).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
            Kill(link, new FmpException("The connection was stopped."));
        }

        public async Task<string> InvokeAsync(string target, string argumentsJson, CancellationToken ct = default)
        {
            var link = RequireLink();
            string id = Interlocked.Increment(ref link.NextId).ToString(CultureInfo.InvariantCulture);
            byte[] frame = Build(id, target, argumentsJson);
            var call = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            link.Pending[id] = call;
            try
            {
                if (link.IsDead)
                    throw Lost(link.Failure);
                using (ct.Register(() => call.TrySetCanceled(ct)))
                {
                    await WriteAsync(link, frame, ct).ConfigureAwait(false);
                    return await call.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                link.Pending.TryRemove(id, out _);
                // a fault set after we left would go unobserved
                call.TrySetCanceled();
                if (call.Task.IsFaulted)
                    _ = call.Task.Exception;
            }
        }

        public async Task SendAsync(string target, string argumentsJson, CancellationToken ct = default)
        {
            var link = RequireLink();
            await WriteAsync(link, Build(null, target, argumentsJson), ct).ConfigureAwait(false);
        }

        public void Dispose()
        {
            Link link;
            CancellationTokenSource life;
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                link = _link;
                life = _life;
                _link = null;
                _state = FmpState.Disconnected;
                _events.Clear();
            }
            life?.Cancel();
            if (link != null)
                Kill(link, new FmpException("The connection was disposed."));
        }

        static Uri ToWebSocketUrl(Uri url)
        {
            switch (url.Scheme)
            {
                case "ws":
                case "wss":
                    return url;
                case "http":
                    return new UriBuilder(url) { Scheme = "ws" }.Uri;
                case "https":
                    return new UriBuilder(url) { Scheme = "wss" }.Uri;
                default:
                    throw new ArgumentException("Only ws, wss, http and https URLs are supported.", nameof(url));
            }
        }

        byte[] Build(string id, string target, string argumentsJson)
        {
            byte[] frame = FmpFrame.Invocation(id, target, argumentsJson);
            // the matrix counts the separator towards its limit
            if (frame.Length > _maxSend)
                throw new FmpException("The message is " + frame.Length + " bytes, over the limit of " + _maxSend + ".");
            return frame;
        }

        Link RequireLink()
        {
            lock (_gate)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(FmpConnection));
                if (_state != FmpState.Connected || _link == null)
                    throw new InvalidOperationException("The connection is not connected (" + _state + ").");
                return _link;
            }
        }

        static FmpException Lost(Exception reason)
            => reason as FmpException ?? new FmpException("The connection to the matrix was lost: " + reason.Message, reason);

        // queued under the lock, raised after it so a subscriber can call back in and delivery stays in order
        void Transition(FmpState state)
        {
            _state = state;
            _events.Enqueue(() => StateChanged?.Invoke(state));
        }

        void Pump()
        {
            lock (_gate)
            {
                if (_pumping)
                    return;
                _pumping = true;
            }
            while (true)
            {
                Action next;
                lock (_gate)
                {
                    if (_events.Count == 0)
                    {
                        _pumping = false;
                        return;
                    }
                    next = _events.Dequeue();
                }
                Raise(next);
            }
        }

        static void Raise(Action action)
        {
            try
            {
                action();
            }
            catch (Exception)
            {
            }
        }

        async Task<Link> ConnectLinkAsync(CancellationToken ct)
        {
            var link = new Link();
            try
            {
                using (var limit = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    limit.CancelAfter(_serverTimeout);
                    await link.Socket.ConnectAsync(_url, limit.Token).ConfigureAwait(false);
                    await link.Socket.SendAsync(new ArraySegment<byte>(HandshakeFrame), WebSocketMessageType.Text, true, limit.Token).ConfigureAwait(false);
                    if (!await NextFrameAsync(link, limit.Token).ConfigureAwait(false))
                        throw new FmpException("The matrix closed the connection during the handshake.");
                    if (!FmpFrame.TryScan(link.Buffer, link.FrameStart, link.FrameEnd, out FrameFields reply))
                        throw new FmpException("The matrix sent a malformed handshake reply.");
                    string error = FmpFrame.Text(link.Buffer, reply.Error);
                    if (error != null)
                        throw new FmpException("The matrix rejected the handshake: " + error);
                }
                link.LastReceived = link.LastSent = Stopwatch.GetTimestamp();
                return link;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                var timeout = new TimeoutException("The matrix did not answer within " + _serverTimeout + ".");
                Kill(link, timeout);
                throw timeout;
            }
            catch (Exception e)
            {
                Kill(link, e);
                throw;
            }
        }

        void StartLoops(Link link)
        {
            link.ReadTask = Task.Run(() => ReadLoopAsync(link));
            _ = Task.Run(() => KeepAliveLoopAsync(link));
        }

        // leaves the next complete frame at link.FrameStart..FrameEnd, valid until the next call; false when the peer closed
        async Task<bool> NextFrameAsync(Link link, CancellationToken ct)
        {
            if (link.Position == link.Length)
            {
                link.Position = link.Length = link.Scan = 0;
                if (link.Buffer.Length > ShrinkAbove)
                    link.Buffer = new byte[InitialBuffer];
            }
            while (true)
            {
                int at = Array.IndexOf(link.Buffer, FmpFrame.Separator, link.Scan, link.Length - link.Scan);
                if ((at >= 0 ? at : link.Length) + 1 - link.Position > _maxReceive)
                    throw new FmpException("The matrix sent a message over the limit of " + _maxReceive + " bytes.");
                if (at >= 0)
                {
                    link.FrameStart = link.Position;
                    link.FrameEnd = at;
                    link.Position = link.Scan = at + 1;
                    return true;
                }
                link.Scan = link.Length;
                if (link.Position > 0)
                {
                    Buffer.BlockCopy(link.Buffer, link.Position, link.Buffer, 0, link.Length - link.Position);
                    link.Length -= link.Position;
                    link.Scan -= link.Position;
                    link.Position = 0;
                }
                if (link.Buffer.Length - link.Length < 4096)
                    Array.Resize(ref link.Buffer, (int)Math.Min(link.Buffer.Length * 2L, _maxReceive + 4096L));
                var result = await link.Socket.ReceiveAsync(new ArraySegment<byte>(link.Buffer, link.Length, link.Buffer.Length - link.Length), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return false;
                link.Length += result.Count;
                Volatile.Write(ref link.LastReceived, Stopwatch.GetTimestamp());
            }
        }

        async Task ReadLoopAsync(Link link)
        {
            try
            {
                while (await NextFrameAsync(link, link.Cts.Token).ConfigureAwait(false))
                {
                    if (!Handle(link))
                        break;
                }
                if (link.Closing)
                    return;
                bool retry = link.CloseError != null || link.CloseAllowReconnect;
                Fail(link, new FmpException("The matrix closed the connection" + (link.CloseError == null ? "." : ": " + link.CloseError)), retry);
            }
            catch (Exception e)
            {
                Fail(link, e, true);
            }
        }

        // false once the matrix has said it is closing
        bool Handle(Link link)
        {
            if (link.FrameStart == link.FrameEnd)
                return true;
            byte[] b = link.Buffer;
            if (!FmpFrame.TryScan(b, link.FrameStart, link.FrameEnd, out FrameFields f))
                throw new FmpException("The matrix sent a malformed frame.");
            switch (f.Type)
            {
                case 1:
                    Dispatch(link, b, f);
                    return true;
                case 3:
                    Complete(link, b, f);
                    return true;
                case 6:
                    return true;
                case 7:
                    link.CloseError = FmpFrame.Text(b, f.Error);
                    link.CloseAllowReconnect = FmpFrame.IsTrue(b, f.AllowReconnect);
                    return false;
                default:
                    // streaming is never used, so every other type is a protocol error
                    throw new FmpException("The matrix sent an unsupported message type " + f.Type + ".");
            }
        }

        void Dispatch(Link link, byte[] b, FrameFields f)
        {
            string target = FmpFrame.Text(b, f.Target);
            if (target == null)
                throw new FmpException("The matrix sent an invocation without a target.");
            string arguments = f.Arguments.IsEmpty ? "[]" : FmpFrame.Raw(b, f.Arguments);
            string id = FmpFrame.Text(b, f.InvocationId);
            _handlers.TryGetValue(target, out Func<string, Task> handler);
            _ = Task.Run(() => RunHandlerAsync(link, target, handler, arguments, id));
        }

        static void Complete(Link link, byte[] b, FrameFields f)
        {
            string id = FmpFrame.Text(b, f.InvocationId);
            if (id == null || !link.Pending.TryRemove(id, out TaskCompletionSource<string> call))
                return;
            string error = FmpFrame.Text(b, f.Error);
            if (error != null)
                call.TrySetException(new FmpException(error));
            else
                call.TrySetResult(f.Result.IsEmpty ? "null" : FmpFrame.Raw(b, f.Result));
        }

        async Task RunHandlerAsync(Link link, string target, Func<string, Task> handler, string arguments, string id)
        {
            string error = null;
            try
            {
                if (handler == null)
                    throw new FmpException("No handler is registered for '" + target + "'.");
                await handler(arguments).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                error = e.Message;
                Raise(() => HandlerFailed?.Invoke(target, e));
            }
            if (id == null)
                return;
            try
            {
                await WriteAsync(link, FmpFrame.Completion(id, error), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        async Task WriteAsync(Link link, byte[] frame, CancellationToken ct)
        {
            await link.SendGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (link.IsDead)
                    throw Lost(link.Failure);
                try
                {
                    // only the link token reaches the socket: a cancelled write would leave half a frame on the wire
                    await link.Socket.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Text, true, link.Cts.Token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Fail(link, e, true);
                    throw Lost(link.Failure);
                }
                Volatile.Write(ref link.LastSent, Stopwatch.GetTimestamp());
            }
            finally
            {
                link.SendGate.Release();
            }
        }

        async Task KeepAliveLoopAsync(Link link)
        {
            try
            {
                long keepAlive = (long)(_keepAlive.TotalSeconds * Stopwatch.Frequency);
                long timeout = (long)(_serverTimeout.TotalSeconds * Stopwatch.Frequency);
                while (true)
                {
                    long now = Stopwatch.GetTimestamp();
                    long silenceLeft = Volatile.Read(ref link.LastReceived) + timeout - now;
                    if (silenceLeft <= 0)
                        throw new TimeoutException("The matrix sent nothing for " + _serverTimeout + ".");
                    long pingLeft = Volatile.Read(ref link.LastSent) + keepAlive - now;
                    if (pingLeft <= 0)
                    {
                        using (var limit = new CancellationTokenSource(_serverTimeout))
                            await WriteAsync(link, PingFrame, limit.Token).ConfigureAwait(false);
                        continue;
                    }
                    long wait = Math.Min(silenceLeft, pingLeft) * 1000 / Stopwatch.Frequency;
                    await Task.Delay((int)Math.Max(1, wait), link.Cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (link.IsDead)
            {
            }
            catch (OperationCanceledException)
            {
                Fail(link, new TimeoutException("A keep-alive ping could not be sent within " + _serverTimeout + "."), true);
            }
            catch (Exception e)
            {
                Fail(link, e, true);
            }
        }

        // first failure wins, so the teardown's own errors never replace the real reason
        static bool Kill(Link link, Exception reason)
        {
            if (Interlocked.CompareExchange(ref link.Failure, reason, null) != null)
                return false;
            try { link.Cts.Cancel(); } catch (Exception) { }
            try { link.Socket.Abort(); } catch (Exception) { }
            try { link.Socket.Dispose(); } catch (Exception) { }
            FmpException lost = Lost(reason);
            foreach (string id in link.Pending.Keys)
            {
                if (link.Pending.TryRemove(id, out TaskCompletionSource<string> call))
                    call.TrySetException(lost);
            }
            return true;
        }

        void Fail(Link link, Exception reason, bool reconnect)
        {
            if (!Kill(link, reason))
                return;
            CancellationTokenSource life = null;
            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(_link, link))
                    return;
                _link = null;
                if (reconnect && _delays.Length > 0)
                {
                    life = _life;
                    Transition(FmpState.Reconnecting);
                }
                else
                {
                    Transition(FmpState.Disconnected);
                    _events.Enqueue(() => Closed?.Invoke(reason));
                }
            }
            Pump();
            if (life != null)
                _ = Task.Run(() => ReconnectAsync(reason, life.Token));
        }

        async Task ReconnectAsync(Exception cause, CancellationToken life)
        {
            Exception last = cause;
            try
            {
                foreach (TimeSpan delay in _delays)
                {
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, life).ConfigureAwait(false);
                    try
                    {
                        Link link = await ConnectLinkAsync(life).ConfigureAwait(false);
                        bool installed;
                        lock (_gate)
                        {
                            installed = !_disposed && !life.IsCancellationRequested && _state == FmpState.Reconnecting;
                            if (installed)
                            {
                                _link = link;
                                Transition(FmpState.Connected);
                                StartLoops(link);
                            }
                        }
                        if (!installed)
                            Kill(link, new FmpException("The connection was stopped."));
                        Pump();
                        return;
                    }
                    catch (OperationCanceledException) when (life.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception e)
                    {
                        last = e;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            lock (_gate)
            {
                if (_disposed || life.IsCancellationRequested || _state != FmpState.Reconnecting)
                    return;
                Transition(FmpState.Disconnected);
                _events.Enqueue(() => Closed?.Invoke(last));
            }
            Pump();
        }

        sealed class Link
        {
            public readonly ClientWebSocket Socket = new ClientWebSocket();
            public readonly SemaphoreSlim SendGate = new SemaphoreSlim(1, 1);
            public readonly ConcurrentDictionary<string, TaskCompletionSource<string>> Pending =
                new ConcurrentDictionary<string, TaskCompletionSource<string>>();
            public readonly CancellationTokenSource Cts = new CancellationTokenSource();
            public byte[] Buffer = new byte[InitialBuffer];
            public int Length;
            public int Position;
            public int Scan;
            public int FrameStart;
            public int FrameEnd;
            public long LastReceived;
            public long LastSent;
            public long NextId;
            public Exception Failure;
            public volatile bool Closing;
            public string CloseError;
            public bool CloseAllowReconnect;
            public Task ReadTask;

            public bool IsDead => Volatile.Read(ref Failure) != null;
        }
    }
}
