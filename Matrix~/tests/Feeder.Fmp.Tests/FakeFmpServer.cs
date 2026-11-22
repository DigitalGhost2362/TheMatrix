using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Feeder.Fmp.Tests
{
    // a hand-rolled websocket endpoint whose script decides every byte it sends, for what the real server never does
    internal sealed class FakeFmpServer : IDisposable
    {
        const string AcceptGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        readonly TcpListener _listener;
        readonly Func<FakeSession, Task> _script;
        readonly List<TcpClient> _clients = new List<TcpClient>();
        int _connections;

        public FakeFmpServer(Func<FakeSession, Task> script)
        {
            _script = script;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoopAsync);
        }

        public int Port { get; }

        public Uri Url => new Uri("ws://127.0.0.1:" + Port + "/fmp");

        public int Connections => Volatile.Read(ref _connections);

        public void Dispose()
        {
            _listener.Stop();
            lock (_clients)
            {
                foreach (TcpClient client in _clients)
                    client.Dispose();
            }
        }

        async Task AcceptLoopAsync()
        {
            try
            {
                while (true)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    lock (_clients)
                        _clients.Add(client);
                    int number = Interlocked.Increment(ref _connections);
                    _ = Task.Run(() => ServeAsync(client, number));
                }
            }
            catch (Exception)
            {
            }
        }

        async Task ServeAsync(TcpClient client, int number)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                var request = new StringBuilder();
                var one = new byte[1];
                while (!request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    if (await stream.ReadAsync(one, 0, 1).ConfigureAwait(false) == 0)
                        return;
                    request.Append((char)one[0]);
                }
                string key = Regex.Match(request.ToString(), "Sec-WebSocket-Key: *([^\r\n]+)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + AcceptGuid)));
                byte[] answer = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 101 Switching Protocols\r\nConnection: Upgrade\r\nUpgrade: websocket\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
                await stream.WriteAsync(answer, 0, answer.Length).ConfigureAwait(false);
                WebSocket socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.Zero });
                await _script(new FakeSession(socket, client, number)).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }
    }

    internal sealed class FakeSession
    {
        readonly WebSocket _socket;
        readonly TcpClient _client;
        byte[] _buffer = new byte[64 * 1024];
        int _length;
        int _position;

        public FakeSession(WebSocket socket, TcpClient client, int number)
        {
            _socket = socket;
            _client = client;
            Number = number;
        }

        // 1 for the first connection the server accepted, 2 for the next, and so on
        public int Number { get; }

        // the next hub frame the client sent, without its separator; null once the client is gone
        public async Task<string> ReadFrameAsync()
        {
            try
            {
                while (true)
                {
                    int at = Array.IndexOf(_buffer, (byte)0x1E, _position, _length - _position);
                    if (at >= 0)
                    {
                        string frame = Encoding.UTF8.GetString(_buffer, _position, at - _position);
                        _position = at + 1;
                        return frame;
                    }
                    if (_position > 0)
                    {
                        Buffer.BlockCopy(_buffer, _position, _buffer, 0, _length - _position);
                        _length -= _position;
                        _position = 0;
                    }
                    if (_length == _buffer.Length)
                        Array.Resize(ref _buffer, _buffer.Length * 2);
                    WebSocketReceiveResult result = await _socket.ReceiveAsync(new ArraySegment<byte>(_buffer, _length, _buffer.Length - _length), CancellationToken.None).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return null;
                    _length += result.Count;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async Task<string> ExpectHandshakeAsync()
        {
            string frame = await ReadFrameAsync().ConfigureAwait(false);
            await SendFrameAsync("{}").ConfigureAwait(false);
            return frame;
        }

        public Task SendFrameAsync(string json) => SendMessageAsync(Encoding.UTF8.GetBytes(json + "\u001e"));

        // one websocket message, optionally cut into fragments of the given size
        public async Task SendMessageAsync(byte[] bytes, int fragmentSize = 0)
        {
            if (fragmentSize <= 0)
                fragmentSize = bytes.Length;
            for (int offset = 0; offset < bytes.Length || offset == 0; offset += fragmentSize)
            {
                int count = Math.Min(fragmentSize, bytes.Length - offset);
                await _socket.SendAsync(new ArraySegment<byte>(bytes, offset, count), WebSocketMessageType.Text, offset + count >= bytes.Length, CancellationToken.None).ConfigureAwait(false);
            }
        }

        // reads until the client goes away, answering nothing
        public async Task WaitForCloseAsync()
        {
            while (await ReadFrameAsync().ConfigureAwait(false) != null)
            {
            }
        }

        public Task CloseAsync() => _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);

        // resets the tcp connection without a closing handshake
        public void Drop()
        {
            _client.Client.LingerState = new LingerOption(true, 0);
            _client.Dispose();
        }
    }
}
