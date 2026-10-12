using System;

namespace Feeder.Fmp
{
    public enum FmpState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
    }

    public sealed class FmpException : Exception
    {
        public FmpException(string message) : base(message) { }

        public FmpException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class FmpOptions
    {
        // one entry per reconnect attempt, each is the wait before that attempt; empty means never reconnect
        public TimeSpan[] ReconnectDelays { get; set; } =
        {
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
        };

        public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);

        public TimeSpan ServerTimeout { get; set; } = TimeSpan.FromSeconds(30);

        public int MaxSendMessageSize { get; set; } = 16 * 1024 * 1024;

        public int MaxReceiveMessageSize { get; set; } = 64 * 1024 * 1024;
    }
}
