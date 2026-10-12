#nullable enable
using System;

namespace Feeder.Json
{
    public sealed partial class Utf8JsonWriter
    {
        const int MaxBase64Bytes = 125_000_000;
        const int Base64Block = 3072;

        // base64 text never needs escaping, so it bypasses the encoder like the reference writer does
        internal void WriteBase64StringValue(byte[] bytes)
        {
            if (bytes.Length > MaxBase64Bytes)
                throw new ArgumentException("The JSON value of length " + bytes.Length + " is too large and not supported.");
            BeginValue();
            Ensure((bytes.Length + 2) / 3 * 4 + 2);
            buffer[pending++] = (byte)'"';
            var chars = new char[Base64Block / 3 * 4];
            for (int offset = 0; offset < bytes.Length; offset += Base64Block)
            {
                int written = Convert.ToBase64CharArray(bytes, offset, Math.Min(Base64Block, bytes.Length - offset), chars, 0);
                for (int i = 0; i < written; i++)
                    buffer[pending++] = (byte)chars[i];
            }
            buffer[pending++] = (byte)'"';
            EndValue(JsonTokenType.String);
        }

        // dates, times and guids are formatted ASCII the reference writer emits without escaping (a '+' offset stays '+')
        internal void WriteAsciiStringValue(string ascii)
        {
            BeginValue();
            Ensure(ascii.Length + 2);
            buffer[pending++] = (byte)'"';
            for (int i = 0; i < ascii.Length; i++)
                buffer[pending++] = (byte)ascii[i];
            buffer[pending++] = (byte)'"';
            EndValue(JsonTokenType.String);
        }
    }
}
