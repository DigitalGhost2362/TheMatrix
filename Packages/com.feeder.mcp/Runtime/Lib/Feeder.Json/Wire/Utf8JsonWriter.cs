#nullable enable
using System;
using System.IO;
using System.Text;

namespace Feeder.Json
{
    // output is buffered until Flush or Dispose, like the reference writer over a stream
    public sealed partial class Utf8JsonWriter : IDisposable
    {
        const int MaxTokenSize = 166_666_666;
        const int ChunkChars = 4096;

        static readonly byte[] newLine = Encoding.UTF8.GetBytes(Environment.NewLine);
        static readonly byte[] hexDigits = Encoding.ASCII.GetBytes("0123456789ABCDEF");

        readonly JavaScriptEncoder policy;
        readonly bool indented;
        readonly bool validate;
        readonly int maxDepth;
        Stream? stream;
        byte[] buffer = new byte[256];
        int pending;
        int depth;
        bool needComma;
        bool inObject;
        JsonTokenType tokenType;
        ulong containers;
        ulong[]? deepContainers;

        public Utf8JsonWriter(Stream utf8Json, JsonWriterOptions options = default)
        {
            if (utf8Json == null)
                throw new ArgumentNullException(nameof(utf8Json));
            if (!utf8Json.CanWrite)
                throw new ArgumentException("Stream was not writable.", nameof(utf8Json));
            stream = utf8Json;
            policy = options.Encoder ?? JavaScriptEncoder.Default;
            indented = options.Indented;
            validate = !options.SkipValidation;
            maxDepth = options.MaxDepth == 0 ? JsonWriterOptions.DefaultMaxDepth : options.MaxDepth;
        }

        public int CurrentDepth => depth;

        public void Flush()
        {
            Stream target = stream ?? throw new ObjectDisposedException(nameof(Utf8JsonWriter));
            if (pending > 0)
            {
                target.Write(buffer, 0, pending);
                pending = 0;
            }
            target.Flush();
        }

        public void Dispose()
        {
            if (stream == null)
                return;
            Flush();
            stream = null;
        }

        public void WriteStartObject() => WriteStart(true);

        public void WriteStartArray() => WriteStart(false);

        public void WriteStartObject(string propertyName) => WriteStart(propertyName, true);

        public void WriteStartArray(string propertyName) => WriteStart(propertyName, false);

        public void WriteEndObject() => WriteEnd(true);

        public void WriteEndArray() => WriteEnd(false);

        public void WritePropertyName(string propertyName)
        {
            BeginProperty(propertyName);
            WriteName(propertyName.AsSpan());
            tokenType = JsonTokenType.PropertyName;
        }

        public void WriteString(string propertyName, string? value)
        {
            if (value == null)
            {
                WriteNull(propertyName);
                return;
            }
            BeginProperty(propertyName, value.Length);
            WriteName(propertyName.AsSpan());
            WriteQuoted(value.AsSpan());
            EndValue(JsonTokenType.String);
        }

        public void WriteNumber(string propertyName, int value) => WriteNumber(propertyName, (long)value);

        public void WriteNumber(string propertyName, long value)
        {
            BeginProperty(propertyName);
            WriteName(propertyName.AsSpan());
            Ensure(JsonNumber.MaxFormattedLength);
            pending += JsonNumber.FormatInt64(value, buffer.AsSpan(pending));
            EndValue(JsonTokenType.Number);
        }

        public void WriteNumber(string propertyName, uint value) => WriteNumber(propertyName, (ulong)value);

        public void WriteNumber(string propertyName, ulong value)
        {
            BeginProperty(propertyName);
            WriteName(propertyName.AsSpan());
            Ensure(JsonNumber.MaxFormattedLength);
            pending += JsonNumber.FormatUInt64(value, buffer.AsSpan(pending));
            EndValue(JsonTokenType.Number);
        }

        public void WriteNumber(string propertyName, float value)
        {
            BeginProperty(propertyName, 0, float.IsNaN(value) || float.IsInfinity(value));
            WriteName(propertyName.AsSpan());
            Ensure(JsonNumber.MaxFormattedLength);
            pending += JsonNumber.FormatSingle(value, buffer.AsSpan(pending));
            EndValue(JsonTokenType.Number);
        }

        public void WriteNumber(string propertyName, double value)
        {
            BeginProperty(propertyName, 0, double.IsNaN(value) || double.IsInfinity(value));
            WriteName(propertyName.AsSpan());
            Ensure(JsonNumber.MaxFormattedLength);
            pending += JsonNumber.FormatDouble(value, buffer.AsSpan(pending));
            EndValue(JsonTokenType.Number);
        }

        public void WriteNumber(string propertyName, decimal value)
        {
            BeginProperty(propertyName);
            WriteName(propertyName.AsSpan());
            Ensure(JsonNumber.MaxFormattedLength);
            pending += JsonNumber.FormatDecimal(value, buffer.AsSpan(pending));
            EndValue(JsonTokenType.Number);
        }

        public void WriteBoolean(string propertyName, bool value)
        {
            BeginProperty(propertyName);
            WriteName(propertyName.AsSpan());
            WriteLiteral(value ? "true" : "false");
            EndValue(value ? JsonTokenType.True : JsonTokenType.False);
        }

        public void WriteNull(string propertyName)
        {
            BeginProperty(propertyName);
            WriteName(propertyName.AsSpan());
            WriteLiteral("null");
            EndValue(JsonTokenType.Null);
        }

        public void WriteStringValue(string? value)
        {
            if (value == null)
            {
                WriteNullValue();
                return;
            }
            CheckLength(value.Length);
            BeginValue();
            WriteQuoted(value.AsSpan());
            EndValue(JsonTokenType.String);
        }

        public void WriteNumberValue(int value) => WriteNumberValue((long)value);

        public void WriteNumberValue(long value)
        {
            BeginValue();
            Ensure(JsonNumber.MaxFormattedLength);
            pending += JsonNumber.FormatInt64(value, buffer.AsSpan(pending));
            EndValue(JsonTokenType.Number);
        }

        public void WriteNumberValue(uint value) => WriteNumberValue((ulong)value);

        public void WriteNumberValue(ulong value)
        {
            BeginValue();
            Ensure(JsonNumber.MaxFormattedLength);
            pending += JsonNumber.FormatUInt64(value, buffer.AsSpan(pending));
            EndValue(JsonTokenType.Number);
        }

        public void WriteNumberValue(float value)
        {
            CheckFinite(float.IsNaN(value) || float.IsInfinity(value));
            BeginValue();
            Ensure(JsonNumber.MaxFormattedLength);
            pending += JsonNumber.FormatSingle(value, buffer.AsSpan(pending));
            EndValue(JsonTokenType.Number);
        }

        public void WriteNumberValue(double value)
        {
            CheckFinite(double.IsNaN(value) || double.IsInfinity(value));
            BeginValue();
            Ensure(JsonNumber.MaxFormattedLength);
            pending += JsonNumber.FormatDouble(value, buffer.AsSpan(pending));
            EndValue(JsonTokenType.Number);
        }

        public void WriteNumberValue(decimal value)
        {
            BeginValue();
            Ensure(JsonNumber.MaxFormattedLength);
            pending += JsonNumber.FormatDecimal(value, buffer.AsSpan(pending));
            EndValue(JsonTokenType.Number);
        }

        public void WriteBooleanValue(bool value)
        {
            BeginValue();
            WriteLiteral(value ? "true" : "false");
            EndValue(value ? JsonTokenType.True : JsonTokenType.False);
        }

        public void WriteNullValue()
        {
            BeginValue();
            WriteLiteral("null");
            EndValue(JsonTokenType.Null);
        }

        // utf8Value may hold invalid sequences; each maximal invalid subpart is written as �
        internal void WriteStringValue(ReadOnlySpan<byte> utf8Value)
        {
            CheckLength(utf8Value.Length);
            BeginValue();
            WriteQuoted(utf8Value);
            EndValue(JsonTokenType.String);
        }

        internal void WritePropertyName(ReadOnlySpan<byte> utf8Name)
        {
            CheckLength(utf8Name.Length);
            CheckProperty();
            WriteNameSeparator();
            WriteQuoted(utf8Name);
            WriteColon();
            tokenType = JsonTokenType.PropertyName;
        }

        internal void WriteNumberValue(ReadOnlySpan<byte> utf8FormattedNumber)
        {
            if (!JsonNumber.IsValid(utf8FormattedNumber))
                throw new ArgumentException("'" + Encoding.UTF8.GetString(utf8FormattedNumber.ToArray()) + "' is not a valid JSON number.", nameof(utf8FormattedNumber));
            BeginValue();
            Ensure(utf8FormattedNumber.Length);
            utf8FormattedNumber.CopyTo(buffer.AsSpan(pending));
            pending += utf8FormattedNumber.Length;
            EndValue(JsonTokenType.Number);
        }

        void WriteStart(bool isObject)
        {
            if (depth >= maxDepth)
                throw DepthTooLarge();
            if (validate)
            {
                CheckValue();
                Push(isObject);
            }
            WriteValueSeparator();
            Ensure(1);
            buffer[pending++] = isObject ? (byte)'{' : (byte)'[';
            OpenContainer(isObject);
        }

        void WriteStart(string propertyName, bool isObject)
        {
            if (propertyName == null)
                throw new ArgumentNullException(nameof(propertyName));
            if (propertyName.Length > MaxTokenSize)
                throw TooLarge(propertyName.Length);
            if (depth >= maxDepth)
                throw DepthTooLarge();
            CheckProperty();
            if (validate)
                Push(isObject);
            WriteName(propertyName.AsSpan());
            Ensure(1);
            buffer[pending++] = isObject ? (byte)'{' : (byte)'[';
            OpenContainer(isObject);
        }

        void OpenContainer(bool isObject)
        {
            depth++;
            needComma = false;
            tokenType = isObject ? JsonTokenType.StartObject : JsonTokenType.StartArray;
        }

        void WriteEnd(bool isObject)
        {
            byte token = isObject ? (byte)'}' : (byte)']';
            if (validate)
            {
                if (depth == 0 || tokenType == JsonTokenType.PropertyName)
                    throw JsonText.InvalidOperation("'" + (char)token + "' is invalid " + (depth == 0 ? "without a matching open." : "following a property name."));
                if (inObject != isObject)
                    throw JsonText.InvalidOperation("'" + (char)token + "' is invalid without a matching open.");
                inObject = depth > 1 && IsObjectAt(depth - 2);
            }
            if (indented && tokenType != JsonTokenType.StartObject && tokenType != JsonTokenType.StartArray)
            {
                WriteNewLine();
                WriteIndent(depth == 0 ? 0 : depth - 1);
            }
            Ensure(1);
            buffer[pending++] = token;
            needComma = true;
            if (depth != 0)
                depth--;
            tokenType = isObject ? JsonTokenType.EndObject : JsonTokenType.EndArray;
        }

        void BeginProperty(string propertyName, int valueLength = 0, bool notFinite = false)
        {
            if (propertyName == null)
                throw new ArgumentNullException(nameof(propertyName));
            CheckLength(propertyName.Length);
            CheckLength(valueLength);
            CheckFinite(notFinite);
            CheckProperty();
        }

        void CheckProperty()
        {
            if (validate && (!inObject || tokenType == JsonTokenType.PropertyName))
                throw JsonText.InvalidOperation("Cannot write a JSON property within an array or as the first JSON token. Current token type is '" + tokenType + "'.");
        }

        void CheckValue()
        {
            if (inObject)
            {
                if (tokenType != JsonTokenType.PropertyName)
                    throw JsonText.InvalidOperation("Cannot write a JSON value within an object without a property name. Current token type is '" + tokenType + "'.");
            }
            else if (depth == 0 && tokenType != JsonTokenType.None)
            {
                throw JsonText.InvalidOperation("Cannot write a JSON value after a single JSON value or outside of an existing closed object/array. Current token type is '" + tokenType + "'.");
            }
        }

        void BeginValue()
        {
            if (validate)
                CheckValue();
            WriteValueSeparator();
        }

        void EndValue(JsonTokenType type)
        {
            needComma = true;
            tokenType = type;
        }

        void WriteValueSeparator()
        {
            if (needComma)
            {
                Ensure(1);
                buffer[pending++] = (byte)',';
            }
            if (indented && tokenType != JsonTokenType.PropertyName)
            {
                if (tokenType != JsonTokenType.None)
                    WriteNewLine();
                WriteIndent(depth);
            }
        }

        void WriteNameSeparator()
        {
            if (needComma)
            {
                Ensure(1);
                buffer[pending++] = (byte)',';
            }
            if (indented)
            {
                if (tokenType != JsonTokenType.None)
                    WriteNewLine();
                WriteIndent(depth);
            }
        }

        void WriteName(ReadOnlySpan<char> name)
        {
            WriteNameSeparator();
            WriteQuoted(name);
            WriteColon();
        }

        void WriteColon()
        {
            Ensure(2);
            buffer[pending++] = (byte)':';
            if (indented)
                buffer[pending++] = (byte)' ';
            needComma = false;
        }

        void WriteNewLine()
        {
            Ensure(newLine.Length);
            for (int i = 0; i < newLine.Length; i++)
                buffer[pending++] = newLine[i];
        }

        void WriteIndent(int level)
        {
            int count = level * 2;
            Ensure(count);
            buffer.AsSpan(pending, count).Fill((byte)' ');
            pending += count;
        }

        void WriteLiteral(string literal)
        {
            Ensure(literal.Length);
            for (int i = 0; i < literal.Length; i++)
                buffer[pending++] = (byte)literal[i];
        }

        void WriteQuoted(ReadOnlySpan<char> text)
        {
            Ensure(2);
            buffer[pending++] = (byte)'"';
            bool relaxed = policy.Relaxed;
            int i = 0;
            while (i < text.Length)
            {
                int stop = Math.Min(text.Length, i + ChunkChars);
                Ensure((stop - i) * 6 + 13);
                byte[] b = buffer;
                while (i < stop)
                {
                    int c = text[i++];
                    if (c < 0x80)
                    {
                        if (policy.Allows(c))
                            b[pending++] = (byte)c;
                        else
                            WriteAsciiEscape(c, relaxed);
                    }
                    else if (c >= 0xD800 && c <= 0xDFFF)
                    {
                        if (c <= 0xDBFF && i < text.Length && text[i] >= 0xDC00 && text[i] <= 0xDFFF)
                        {
                            WriteHexEscape(c);
                            WriteHexEscape(text[i++]);
                        }
                        else
                        {
                            WriteHexEscape(0xFFFD);
                        }
                    }
                    else if (relaxed && policy.Allows(c))
                    {
                        pending += JsonText.EncodeScalar(c, b.AsSpan(pending));
                    }
                    else
                    {
                        WriteHexEscape(c);
                    }
                }
            }
            Ensure(1);
            buffer[pending++] = (byte)'"';
        }

        void WriteQuoted(ReadOnlySpan<byte> text)
        {
            Ensure(2);
            buffer[pending++] = (byte)'"';
            bool relaxed = policy.Relaxed;
            int i = 0;
            while (i < text.Length)
            {
                int stop = Math.Min(text.Length, i + ChunkChars);
                Ensure((stop - i) * 6 + 13);
                byte[] b = buffer;
                while (i < stop)
                {
                    int c = text[i];
                    if (c < 0x80)
                    {
                        i++;
                        if (policy.Allows(c))
                            b[pending++] = (byte)c;
                        else
                            WriteAsciiEscape(c, relaxed);
                        continue;
                    }
                    int cp = JsonText.DecodeScalar(text, i, out int length);
                    if (cp < 0)
                    {
                        WriteHexEscape(0xFFFD);
                    }
                    else if (cp >= 0x10000)
                    {
                        cp -= 0x10000;
                        WriteHexEscape(0xD800 + (cp >> 10));
                        WriteHexEscape(0xDC00 + (cp & 0x3FF));
                    }
                    else if (relaxed && policy.Allows(cp))
                    {
                        text.Slice(i, length).CopyTo(b.AsSpan(pending));
                        pending += length;
                    }
                    else
                    {
                        WriteHexEscape(cp);
                    }
                    i += length;
                }
            }
            Ensure(1);
            buffer[pending++] = (byte)'"';
        }

        void WriteAsciiEscape(int c, bool relaxed)
        {
            byte shortForm;
            switch (c)
            {
                case '\b': shortForm = (byte)'b'; break;
                case '\t': shortForm = (byte)'t'; break;
                case '\n': shortForm = (byte)'n'; break;
                case '\f': shortForm = (byte)'f'; break;
                case '\r': shortForm = (byte)'r'; break;
                case '\\': shortForm = (byte)'\\'; break;
                case '"' when relaxed: shortForm = (byte)'"'; break;
                default:
                    WriteHexEscape(c);
                    return;
            }
            buffer[pending++] = (byte)'\\';
            buffer[pending++] = shortForm;
        }

        void WriteHexEscape(int c)
        {
            byte[] b = buffer;
            b[pending++] = (byte)'\\';
            b[pending++] = (byte)'u';
            b[pending++] = hexDigits[(c >> 12) & 0xF];
            b[pending++] = hexDigits[(c >> 8) & 0xF];
            b[pending++] = hexDigits[(c >> 4) & 0xF];
            b[pending++] = hexDigits[c & 0xF];
        }

        void Ensure(int count)
        {
            if (stream == null)
                throw new ObjectDisposedException(nameof(Utf8JsonWriter));
            if (buffer.Length - pending < count)
                Array.Resize(ref buffer, Math.Max(buffer.Length * 2, pending + count));
        }

        void Push(bool isObject)
        {
            if (depth < 64)
            {
                if (isObject)
                    containers |= 1UL << depth;
                else
                    containers &= ~(1UL << depth);
            }
            else
            {
                int index = (depth - 64) >> 6;
                if (deepContainers == null)
                    deepContainers = new ulong[16];
                if (index >= deepContainers.Length)
                    Array.Resize(ref deepContainers, Math.Max(index + 1, deepContainers.Length * 2));
                ulong bit = 1UL << ((depth - 64) & 63);
                if (isObject)
                    deepContainers[index] |= bit;
                else
                    deepContainers[index] &= ~bit;
            }
            inObject = isObject;
        }

        bool IsObjectAt(int level) => level < 64
            ? (containers & (1UL << level)) != 0
            : (deepContainers![(level - 64) >> 6] & (1UL << ((level - 64) & 63))) != 0;

        static void CheckLength(int length)
        {
            if (length > MaxTokenSize)
                throw TooLarge(length);
        }

        static void CheckFinite(bool notFinite)
        {
            if (notFinite)
                throw new ArgumentException(".NET number values such as positive and negative infinity cannot be written as valid JSON.");
        }

        static ArgumentException TooLarge(int length) =>
            new ArgumentException("The JSON value of length " + length + " is too large and not supported.");

        InvalidOperationException DepthTooLarge() =>
            JsonText.InvalidOperation("CurrentDepth (" + depth + ") is equal to or larger than the maximum allowed depth of " + maxDepth + ". Cannot write the next JSON object or array.");
    }
}
