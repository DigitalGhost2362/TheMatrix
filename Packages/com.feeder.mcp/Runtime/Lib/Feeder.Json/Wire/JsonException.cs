#nullable enable
using System;

namespace Feeder.Json
{
    public partial class JsonException : Exception
    {
        string? message;

        public JsonException(string? message) : base(message)
        {
            this.message = message;
        }

        public JsonException(string? message, Exception? innerException) : base(message, innerException)
        {
            this.message = message;
        }

        public JsonException(string? message, string? path, long? lineNumber, long? bytePositionInLine) : base(message)
        {
            this.message = message;
            Path = path;
            LineNumber = lineNumber;
            BytePositionInLine = bytePositionInLine;
        }

        public long? LineNumber { get; internal set; }

        public long? BytePositionInLine { get; internal set; }

        public string? Path { get; internal set; }

        public override string Message => message ?? base.Message;

        internal void SetMessage(string? value) => message = value;
    }
}
