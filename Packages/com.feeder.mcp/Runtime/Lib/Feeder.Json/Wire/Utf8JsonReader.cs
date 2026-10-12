#nullable enable
using System;
using System.Buffers;
using System.Globalization;

namespace Feeder.Json
{
    // forward-only reader over one complete UTF-8 buffer; the data is always the final block
    public ref partial struct Utf8JsonReader
    {
        static readonly byte[] trueLiteral = { (byte)'t', (byte)'r', (byte)'u', (byte)'e' };
        static readonly byte[] falseLiteral = { (byte)'f', (byte)'a', (byte)'l', (byte)'s', (byte)'e' };
        static readonly byte[] nullLiteral = { (byte)'n', (byte)'u', (byte)'l', (byte)'l' };

        readonly ReadOnlySpan<byte> buffer;
        readonly bool allowTrailingCommas;
        readonly bool skipComments;
        readonly int maxDepth;
        int consumed;
        int tokenStart;
        int valueStart;
        int valueLength;
        JsonTokenType tokenType;
        bool valueIsEscaped;
        bool inObject;
        bool isNotPrimitive;
        int depth;
        ulong containers;
        ulong[]? deepContainers;
        // a line comment ended by a lone CR starts a new line for error positions, as in the reference reader
        int crLines;
        int crLineStart;

        public Utf8JsonReader(ReadOnlySpan<byte> jsonData, JsonReaderOptions options = default)
        {
            if (options.CommentHandling == JsonCommentHandling.Allow)
                throw new NotSupportedException("JsonCommentHandling.Allow is not supported; use Skip or Disallow.");
            buffer = jsonData;
            allowTrailingCommas = options.AllowTrailingCommas;
            skipComments = options.CommentHandling == JsonCommentHandling.Skip;
            maxDepth = options.MaxDepth == 0 ? JsonReaderOptions.DefaultMaxDepth : options.MaxDepth;
            consumed = 0;
            tokenStart = 0;
            valueStart = 0;
            valueLength = 0;
            tokenType = JsonTokenType.None;
            valueIsEscaped = false;
            inObject = false;
            isNotPrimitive = false;
            depth = 0;
            containers = 0;
            deepContainers = null;
            crLines = 0;
            crLineStart = 0;
        }

        public readonly JsonTokenType TokenType => tokenType;

        public readonly ReadOnlySpan<byte> ValueSpan => buffer.Slice(valueStart, valueLength);

        public readonly ReadOnlySequence<byte> ValueSequence => default;

        public readonly bool HasValueSequence => false;

        public readonly bool ValueIsEscaped => valueIsEscaped;

        public readonly long TokenStartIndex => tokenStart;

        public readonly long BytesConsumed => consumed;

        public readonly int CurrentDepth => tokenType == JsonTokenType.StartObject || tokenType == JsonTokenType.StartArray ? depth - 1 : depth;

        internal readonly ReadOnlySpan<byte> Buffer => buffer;

        internal readonly JsonReaderOptions Options => new JsonReaderOptions
        {
            AllowTrailingCommas = allowTrailingCommas,
            CommentHandling = skipComments ? JsonCommentHandling.Skip : JsonCommentHandling.Disallow,
            MaxDepth = maxDepth,
        };

        public bool Read()
        {
            valueStart = 0;
            valueLength = 0;
            valueIsEscaped = false;
            int before = consumed;
            if (!SkipTrivia())
            {
                if (tokenType == JsonTokenType.None)
                    throw Error("The input does not contain any JSON tokens. Expected the input to start with a valid JSON token.", consumed);
                if (isNotPrimitive && depth != 0)
                    throw Error("Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.", consumed);
                // trailing comments leave the token start on the first of them, as the reference reader does
                while (before < buffer.Length && IsWhiteSpace(buffer[before]))
                    before++;
                if (before < buffer.Length)
                    tokenStart = before;
                return false;
            }

            byte first = buffer[consumed];
            tokenStart = consumed;
            switch (tokenType)
            {
                case JsonTokenType.None:
                    ReadFirstToken(first);
                    break;
                case JsonTokenType.StartObject:
                    if (first == '}')
                        EndContainer(true);
                    else
                        ConsumePropertyName(first);
                    break;
                case JsonTokenType.StartArray:
                    if (first == ']')
                        EndContainer(false);
                    else
                        ConsumeValue(first);
                    break;
                case JsonTokenType.PropertyName:
                    ConsumeValue(first);
                    break;
                default:
                    ConsumeAfterValue(first);
                    break;
            }
            return true;
        }

        public void Skip()
        {
            if (tokenType == JsonTokenType.PropertyName)
                Read();
            if (tokenType == JsonTokenType.StartObject || tokenType == JsonTokenType.StartArray)
            {
                int target = CurrentDepth;
                do
                {
                    Read();
                }
                while (target < CurrentDepth);
            }
        }

        public readonly string? GetString()
        {
            if (tokenType == JsonTokenType.Null)
                return null;
            if (tokenType != JsonTokenType.String && tokenType != JsonTokenType.PropertyName)
                throw JsonText.InvalidOperation("Cannot get the value of a token type '" + tokenType + "' as a string.");
            return valueIsEscaped ? JsonText.UnescapeToString(ValueSpan) : JsonText.Utf8ToString(ValueSpan);
        }

        public readonly bool GetBoolean()
        {
            if (tokenType == JsonTokenType.True)
                return true;
            if (tokenType == JsonTokenType.False)
                return false;
            throw JsonText.InvalidOperation("Cannot get the value of a token type '" + tokenType + "' as a boolean.");
        }

        public readonly byte GetByte() => TryGetByte(out byte value) ? value : throw NumberFormat("Byte");

        public readonly bool TryGetByte(out byte value) => JsonNumber.TryGetByte(NumberSpan(), out value);

        public readonly int GetInt32() => TryGetInt32(out int value) ? value : throw NumberFormat("Int32");

        public readonly bool TryGetInt32(out int value) => JsonNumber.TryGetInt32(NumberSpan(), out value);

        public readonly long GetInt64() => TryGetInt64(out long value) ? value : throw NumberFormat("Int64");

        public readonly bool TryGetInt64(out long value) => JsonNumber.TryGetInt64(NumberSpan(), out value);

        public readonly uint GetUInt32() => TryGetUInt32(out uint value) ? value : throw NumberFormat("UInt32");

        public readonly bool TryGetUInt32(out uint value) => JsonNumber.TryGetUInt32(NumberSpan(), out value);

        public readonly ulong GetUInt64() => TryGetUInt64(out ulong value) ? value : throw NumberFormat("UInt64");

        public readonly bool TryGetUInt64(out ulong value) => JsonNumber.TryGetUInt64(NumberSpan(), out value);

        public readonly decimal GetDecimal() => TryGetDecimal(out decimal value) ? value : throw NumberFormat("Decimal");

        public readonly bool TryGetDecimal(out decimal value) => JsonNumber.TryParseDecimal(NumberSpan(), out value);

        public readonly double GetDouble() => JsonNumber.ParseDouble(NumberSpan());

        public readonly bool TryGetDouble(out double value)
        {
            value = JsonNumber.ParseDouble(NumberSpan());
            return true;
        }

        public readonly float GetSingle() => JsonNumber.ParseSingle(NumberSpan());

        public readonly bool TryGetSingle(out float value)
        {
            value = JsonNumber.ParseSingle(NumberSpan());
            return true;
        }

        readonly ReadOnlySpan<byte> NumberSpan()
        {
            if (tokenType != JsonTokenType.Number)
                throw JsonText.InvalidOperation("Cannot get the value of a token type '" + tokenType + "' as a number.");
            return ValueSpan;
        }

        static FormatException NumberFormat(string type) =>
            JsonText.Format("Either the JSON value is not in a supported format, or is out of bounds for " + (type == "Int32" || type == "Int64" ? "an " : "a ") + type + ".");

        void ReadFirstToken(byte first)
        {
            if (first == '{' || first == '[')
            {
                isNotPrimitive = true;
                Push(first == '{');
                tokenType = first == '{' ? JsonTokenType.StartObject : JsonTokenType.StartArray;
                valueStart = consumed;
                valueLength = 1;
                consumed++;
                return;
            }
            ConsumeValue(first);
        }

        void ConsumeValue(byte marker)
        {
            switch (marker)
            {
                case (byte)'"':
                    ConsumeString();
                    tokenType = JsonTokenType.String;
                    return;
                case (byte)'{':
                case (byte)'[':
                    if (depth >= maxDepth)
                        throw Error("The maximum configured depth of " + maxDepth + " has been exceeded. Cannot read next JSON " + (marker == '{' ? "object." : "array."), consumed);
                    Push(marker == '{');
                    tokenType = marker == '{' ? JsonTokenType.StartObject : JsonTokenType.StartArray;
                    valueStart = consumed;
                    valueLength = 1;
                    consumed++;
                    return;
                case (byte)'t':
                    ConsumeLiteral(trueLiteral, JsonTokenType.True);
                    return;
                case (byte)'f':
                    ConsumeLiteral(falseLiteral, JsonTokenType.False);
                    return;
                case (byte)'n':
                    ConsumeLiteral(nullLiteral, JsonTokenType.Null);
                    return;
            }
            if (marker == '-' || JsonNumber.IsDigit(marker))
            {
                ConsumeNumber();
                return;
            }
            throw Error("'" + Show(marker) + "' is an invalid start of a value.", consumed);
        }

        void ConsumeAfterValue(byte first)
        {
            if (depth == 0)
                throw Error("'" + Show(first) + "' is invalid after a single JSON value. Expected end of data.", consumed);
            if (first == '}' || first == ']')
            {
                EndContainer(first == '}');
                return;
            }
            if (first != ',')
                throw Error("'" + Show(first) + "' is invalid after a value. Expected either ',', '}', or ']'.", consumed);

            // a comma that is the last byte is reported at its own position, trailing whitespace at the end of data
            if (++consumed >= buffer.Length)
                throw Error("Expected start of a property name or value, but instead reached end of data.", consumed - 1);
            if (!SkipTrivia())
                throw Error("Expected start of a property name or value, but instead reached end of data.", consumed);
            byte next = buffer[consumed];
            tokenStart = consumed;
            if (inObject)
            {
                if (next == '}')
                {
                    if (!allowTrailingCommas)
                        throw Error("The JSON object contains a trailing comma at the end which is not supported in this mode. Change the reader options.", consumed);
                    EndContainer(true);
                    return;
                }
                ConsumePropertyName(next);
                return;
            }
            if (next == ']')
            {
                if (!allowTrailingCommas)
                    throw Error("The JSON array contains a trailing comma at the end which is not supported in this mode. Change the reader options.", consumed);
                EndContainer(false);
                return;
            }
            ConsumeValue(next);
        }

        void EndContainer(bool isObject)
        {
            if (inObject != isObject || depth == 0)
                throw Error("'" + (isObject ? '}' : ']') + "' is invalid without a matching open.", consumed);
            tokenType = isObject ? JsonTokenType.EndObject : JsonTokenType.EndArray;
            valueStart = consumed;
            valueLength = 1;
            consumed++;
            depth--;
            inObject = depth > 0 && IsObjectAt(depth - 1);
        }

        void ConsumePropertyName(byte first)
        {
            if (first != '"')
                throw Error("'" + Show(first) + "' is an invalid start of a property name. Expected a '\"'.", consumed);
            ConsumeString();
            int i = consumed;
            while (i < buffer.Length && IsWhiteSpace(buffer[i]))
                i++;
            if (i >= buffer.Length)
                throw Error("Expected a value, but instead reached end of data.", i);
            if (buffer[i] != ':')
                throw Error("'" + Show(buffer[i]) + "' is invalid after a property name. Expected a ':'.", i);
            consumed = i + 1;
            tokenType = JsonTokenType.PropertyName;
        }

        void ConsumeString()
        {
            int i = consumed + 1;
            bool escaped = false;
            while (true)
            {
                if (i >= buffer.Length)
                    throw Error("Expected end of string, but instead reached end of data.", i);
                byte b = buffer[i];
                if (b == '"')
                    break;
                if (b == '\\')
                {
                    escaped = true;
                    if (++i >= buffer.Length)
                        throw Error("Expected end of string, but instead reached end of data.", i);
                    b = buffer[i];
                    if (b == 'u')
                    {
                        for (int k = 1; k <= 4; k++)
                        {
                            if (i + k >= buffer.Length)
                                throw Error("Expected end of string, but instead reached end of data.", i + k);
                            if (!IsHex(buffer[i + k]))
                                throw Error("'" + Show(buffer[i + k]) + "' is not a hex digit following '\\u' within a JSON string. The string should be correctly escaped.", i + k);
                        }
                        i += 4;
                    }
                    else if (b != '"' && b != '\\' && b != '/' && b != 'b' && b != 'f' && b != 'n' && b != 'r' && b != 't')
                    {
                        throw Error("'" + Show(b) + "' is not a valid escapable character within a JSON string. The string should be correctly escaped.", i);
                    }
                }
                else if (b < 0x20)
                {
                    throw Error("'" + Show(b) + "' is invalid within a JSON string. The string should be correctly escaped.", i);
                }
                i++;
            }
            valueStart = consumed + 1;
            valueLength = i - valueStart;
            valueIsEscaped = escaped;
            consumed = i + 1;
        }

        void ConsumeLiteral(byte[] literal, JsonTokenType type)
        {
            ReadOnlySpan<byte> rest = buffer.Slice(consumed);
            if (!rest.StartsWith(literal))
            {
                int k = 0;
                while (k < rest.Length && k < literal.Length && rest[k] == literal[k])
                    k++;
                string expected = type == JsonTokenType.True ? "true" : type == JsonTokenType.False ? "false" : "null";
                throw Error("'" + (k < rest.Length ? Show(rest[k]) : "end of data") + "' is an invalid JSON literal. Expected the literal '" + expected + "'.", consumed + k);
            }
            tokenType = type;
            valueStart = consumed;
            valueLength = literal.Length;
            consumed += literal.Length;
        }

        void ConsumeNumber()
        {
            int i = consumed;
            if (buffer[i] == '-')
            {
                i++;
                RequireDigit(i, "'{0}' is invalid within a number, immediately after a sign character ('+' or '-'). Expected a digit ('0'-'9').");
            }
            if (buffer[i] == '0')
            {
                i++;
                if (i < buffer.Length && JsonNumber.IsDigit(buffer[i]))
                    throw Error("Invalid leading zero before '" + Show(buffer[i]) + "'.", i);
            }
            else
            {
                while (i < buffer.Length && JsonNumber.IsDigit(buffer[i]))
                    i++;
            }
            if (i < buffer.Length && buffer[i] == '.')
            {
                i++;
                RequireDigit(i, "'{0}' is invalid within a number, immediately after a decimal point ('.'). Expected a digit ('0'-'9').");
                while (i < buffer.Length && JsonNumber.IsDigit(buffer[i]))
                    i++;
            }
            if (i < buffer.Length && (buffer[i] == 'e' || buffer[i] == 'E'))
            {
                i++;
                if (i < buffer.Length && (buffer[i] == '+' || buffer[i] == '-'))
                    i++;
                RequireDigit(i, "'{0}' is invalid within a number, immediately after a sign character ('+' or '-'). Expected a digit ('0'-'9').");
                while (i < buffer.Length && JsonNumber.IsDigit(buffer[i]))
                    i++;
            }
            if (i < buffer.Length)
            {
                if (!IsDelimiter(buffer[i]))
                    throw Error("'" + Show(buffer[i]) + "' is an invalid end of a number. Expected a delimiter.", i);
            }
            else if (isNotPrimitive)
            {
                throw Error("'" + Show(buffer[i - 1]) + "' is an invalid end of a number. Expected a delimiter.", i);
            }
            tokenType = JsonTokenType.Number;
            valueStart = consumed;
            valueLength = i - consumed;
            consumed = i;
        }

        readonly void RequireDigit(int i, string message)
        {
            if (i >= buffer.Length)
                throw Error("Expected a digit ('0'-'9'), but instead reached end of data.", i);
            if (!JsonNumber.IsDigit(buffer[i]))
                throw Error(string.Format(CultureInfo.InvariantCulture, message, Show(buffer[i])), i);
        }

        // whitespace, plus comments when they are skipped; false at the end of the data
        bool SkipTrivia()
        {
            while (consumed < buffer.Length)
            {
                byte b = buffer[consumed];
                if (IsWhiteSpace(b))
                {
                    consumed++;
                }
                else if (b == '/' && skipComments)
                {
                    SkipComment();
                }
                else
                {
                    return true;
                }
            }
            return false;
        }

        void SkipComment()
        {
            int i = consumed + 1;
            if (i >= buffer.Length || (buffer[i] != '/' && buffer[i] != '*'))
                throw Error("'/' is an invalid start of a value.", consumed);
            if (buffer[i] == '/')
            {
                for (i++; i < buffer.Length; i++)
                {
                    byte b = buffer[i];
                    if (b == '\n')
                        break;
                    if (b == '\r')
                    {
                        if (i + 1 < buffer.Length && buffer[i + 1] == '\n')
                        {
                            i++;
                        }
                        else
                        {
                            crLines++;
                            crLineStart = i + 1;
                        }
                        break;
                    }
                    if (b == 0xE2 && i + 2 < buffer.Length && buffer[i + 1] == 0x80 && (buffer[i + 2] == 0xA8 || buffer[i + 2] == 0xA9))
                        throw Error("Found invalid line or paragraph separator character while reading a comment.", consumed);
                }
                consumed = Math.Min(i + 1, buffer.Length);
                return;
            }
            for (i++; i + 1 < buffer.Length; i++)
            {
                if (buffer[i] == '*' && buffer[i + 1] == '/')
                {
                    consumed = i + 2;
                    return;
                }
            }
            throw Error("Expected end of comment, but instead reached end of data.", consumed);
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
                    deepContainers = new ulong[4];
                if (index >= deepContainers.Length)
                    Array.Resize(ref deepContainers, Math.Max(index + 1, deepContainers.Length * 2));
                ulong bit = 1UL << ((depth - 64) & 63);
                if (isObject)
                    deepContainers[index] |= bit;
                else
                    deepContainers[index] &= ~bit;
            }
            depth++;
            inObject = isObject;
        }

        readonly bool IsObjectAt(int level) => level < 64
            ? (containers & (1UL << level)) != 0
            : (deepContainers![(level - 64) >> 6] & (1UL << ((level - 64) & 63))) != 0;

        static bool IsWhiteSpace(byte b) => b == ' ' || b == '\n' || b == '\r' || b == '\t';

        static bool IsDelimiter(byte b) => b == ',' || b == '}' || b == ']' || b == ' ' || b == '\n' || b == '\r' || b == '\t' || b == '/';

        static bool IsHex(byte b) => JsonNumber.IsDigit(b) || (uint)((b | 0x20) - 'a') <= 5;

        static string Show(byte b) => b >= 0x20 && b < 0x7F ? ((char)b).ToString() : "0x" + b.ToString("X2", CultureInfo.InvariantCulture);

        readonly JsonException Error(string message, int position)
        {
            int line = crLines, lineStart = crLineStart;
            int end = Math.Min(position, buffer.Length);
            for (int k = 0; k < end; k++)
            {
                if (buffer[k] == '\n')
                {
                    line++;
                    lineStart = Math.Max(lineStart, k + 1);
                }
            }
            long column = position - lineStart;
            return new JsonException(message + " LineNumber: " + line + " | BytePositionInLine: " + column + ".", null, line, column);
        }
    }
}
