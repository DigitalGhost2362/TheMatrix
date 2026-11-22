#nullable enable
using System;
using System.Buffers;

namespace Feeder.Json
{
    public sealed partial class JsonDocument : IDisposable
    {
        // one row per token in document order; strings start at the opening quote
        internal struct Row
        {
            internal int Start;
            internal int Length;
            internal int Link;
            internal JsonTokenType Type;
            internal bool Escaped;
        }

        readonly ReadOnlyMemory<byte> utf8;
        readonly Row[] rows;
        readonly bool isDisposable;
        bool disposed;

        JsonDocument(ReadOnlyMemory<byte> utf8, Row[] rows, bool isDisposable)
        {
            this.utf8 = utf8;
            this.rows = rows;
            this.isDisposable = isDisposable;
        }

        public JsonElement RootElement => new JsonElement(this, 0);

        internal bool IsDisposable => isDisposable;

        public static JsonDocument Parse(ReadOnlyMemory<byte> utf8Json, JsonDocumentOptions options = default) =>
            Parse(utf8Json, options.GetReaderOptions(), true);

        public static JsonDocument Parse(string json, JsonDocumentOptions options = default)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));
            var bytes = new byte[JsonText.Utf8Length(json.AsSpan())];
            JsonText.ToUtf8(json.AsSpan(), bytes);
            return Parse(bytes, options.GetReaderOptions(), true);
        }

        public static JsonDocument ParseValue(ref Utf8JsonReader reader)
        {
            TryParseValue(ref reader, out JsonDocument? document, true, true);
            return document!;
        }

        public void Dispose()
        {
            if (isDisposable)
                disposed = true;
        }

        internal static bool TryParseValue(ref Utf8JsonReader reader, out JsonDocument? document, bool shouldThrow, bool isDisposable)
        {
            Utf8JsonReader restore = reader;
            int start, length;
            try
            {
                if (reader.TokenType == JsonTokenType.None || reader.TokenType == JsonTokenType.PropertyName)
                    reader.Read();
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        start = (int)reader.TokenStartIndex;
                        reader.Skip();
                        length = (int)reader.BytesConsumed - start;
                        break;
                    case JsonTokenType.String:
                        start = (int)reader.TokenStartIndex;
                        length = reader.ValueSpan.Length + 2;
                        break;
                    case JsonTokenType.Number:
                    case JsonTokenType.True:
                    case JsonTokenType.False:
                    case JsonTokenType.Null:
                        start = (int)reader.TokenStartIndex;
                        length = reader.ValueSpan.Length;
                        break;
                    default:
                        if (shouldThrow)
                            throw new JsonException("'" + (char)reader.ValueSpan[0] + "' is an invalid start of a value.");
                        reader = restore;
                        document = null;
                        return false;
                }
            }
            catch
            {
                reader = restore;
                throw;
            }
            byte[] copy = reader.Buffer.Slice(start, length).ToArray();
            document = Parse(copy, reader.Options, isDisposable);
            return true;
        }

        static JsonDocument Parse(ReadOnlyMemory<byte> utf8, JsonReaderOptions options, bool isDisposable)
        {
            var rows = new Row[Math.Max(16, utf8.Length / 8)];
            int count = 0;
            int[] open = new int[16];
            int top = 0;
            var reader = new Utf8JsonReader(utf8.Span, options);
            while (reader.Read())
            {
                if (count == rows.Length)
                    Array.Resize(ref rows, rows.Length * 2);
                JsonTokenType type = reader.TokenType;
                ref Row row = ref rows[count];
                row.Type = type;
                row.Start = (int)reader.TokenStartIndex;
                switch (type)
                {
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        CountChild(rows, open, top);
                        if (top == open.Length)
                            Array.Resize(ref open, open.Length * 2);
                        open[top++] = count;
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        int startRow = open[--top];
                        row.Link = startRow;
                        rows[startRow].Link = count;
                        break;
                    case JsonTokenType.PropertyName:
                        rows[open[top - 1]].Length++;
                        row.Length = reader.ValueSpan.Length;
                        row.Escaped = reader.ValueIsEscaped;
                        break;
                    default:
                        CountChild(rows, open, top);
                        row.Length = reader.ValueSpan.Length;
                        row.Escaped = reader.ValueIsEscaped;
                        break;
                }
                count++;
            }
            if (count != rows.Length)
                Array.Resize(ref rows, count);
            return new JsonDocument(utf8, rows, isDisposable);
        }

        static void CountChild(Row[] rows, int[] open, int top)
        {
            if (top > 0 && rows[open[top - 1]].Type == JsonTokenType.StartArray)
                rows[open[top - 1]].Length++;
        }

        void CheckNotDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(JsonDocument));
        }

        internal JsonTokenType GetTokenType(int index)
        {
            CheckNotDisposed();
            return rows[index].Type;
        }

        internal int GetArrayLength(int index)
        {
            CheckNotDisposed();
            Row row = rows[index];
            if (row.Type != JsonTokenType.StartArray)
                throw JsonElement.WrongType(JsonValueKind.Array, row.Type);
            return row.Length;
        }

        // first row after the value at index (or its end row when includeEnd is false)
        internal int GetEndIndex(int index, bool includeEnd)
        {
            CheckNotDisposed();
            Row row = rows[index];
            if (row.Type != JsonTokenType.StartObject && row.Type != JsonTokenType.StartArray)
                return index + 1;
            return includeEnd ? row.Link + 1 : row.Link;
        }

        internal string? GetString(int index, JsonTokenType expected)
        {
            CheckNotDisposed();
            Row row = rows[index];
            if (row.Type == JsonTokenType.Null)
                return null;
            if (row.Type != expected)
                throw JsonElement.WrongType(JsonValueKind.String, row.Type);
            ReadOnlySpan<byte> text = utf8.Span.Slice(row.Start + 1, row.Length);
            return row.Escaped ? JsonText.UnescapeToString(text) : JsonText.Utf8ToString(text);
        }

        internal ReadOnlySpan<byte> GetNumber(int index)
        {
            CheckNotDisposed();
            Row row = rows[index];
            if (row.Type != JsonTokenType.Number)
                throw JsonElement.WrongType(JsonValueKind.Number, row.Type);
            return utf8.Span.Slice(row.Start, row.Length);
        }

        internal ReadOnlySpan<byte> GetRawValue(int index)
        {
            CheckNotDisposed();
            Row row = rows[index];
            switch (row.Type)
            {
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    return utf8.Span.Slice(row.Start, rows[row.Link].Start + 1 - row.Start);
                case JsonTokenType.String:
                    return utf8.Span.Slice(row.Start, row.Length + 2);
                default:
                    return utf8.Span.Slice(row.Start, row.Length);
            }
        }

        // from the opening quote of the property name to the end of its value
        internal string GetPropertyRawText(int valueIndex)
        {
            ReadOnlySpan<byte> value = GetRawValue(valueIndex);
            int start = rows[valueIndex - 1].Start;
            int end = rows[valueIndex].Start + value.Length;
            return JsonText.Utf8ToString(utf8.Span.Slice(start, end - start));
        }

        internal bool TryGetProperty(int index, string name, out JsonElement value)
        {
            CheckNotDisposed();
            Row row = rows[index];
            if (row.Type != JsonTokenType.StartObject)
                throw JsonElement.WrongType(JsonValueKind.Object, row.Type);

            int length = JsonText.Utf8Length(name.AsSpan());
            byte[]? rented = null;
            Span<byte> target = length <= 256 ? stackalloc byte[length] : (rented = ArrayPool<byte>.Shared.Rent(length)).AsSpan(0, length);
            try
            {
                JsonText.ToUtf8(name.AsSpan(), target);
                // the last definition of a duplicated name wins, so walk from the end
                for (int i = row.Link - 1; i > index;)
                {
                    int valueRow = rows[i].Type == JsonTokenType.EndObject || rows[i].Type == JsonTokenType.EndArray ? rows[i].Link : i;
                    int nameRow = valueRow - 1;
                    if (NameEquals(rows[nameRow], target))
                    {
                        value = new JsonElement(this, valueRow);
                        return true;
                    }
                    i = nameRow - 1;
                }
            }
            finally
            {
                if (rented != null)
                    ArrayPool<byte>.Shared.Return(rented);
            }
            value = default;
            return false;
        }

        // an escaped name is only decoded when it could still match, so a bad escape elsewhere never throws
        bool NameEquals(Row name, ReadOnlySpan<byte> target)
        {
            ReadOnlySpan<byte> text = utf8.Span.Slice(name.Start + 1, name.Length);
            if (!name.Escaped)
                return text.SequenceEqual(target);
            int slash = text.IndexOf((byte)'\\');
            if (text.Length <= target.Length || target.Length <= slash || !text.Slice(0, slash).SequenceEqual(target.Slice(0, slash)))
                return false;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(text.Length);
            try
            {
                int n = JsonText.Unescape(text.Slice(slash), buffer);
                return buffer.AsSpan(0, n).SequenceEqual(target.Slice(slash));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        internal JsonElement CloneElement(int index)
        {
            ReadOnlySpan<byte> raw = GetRawValue(index);
            int end = GetEndIndex(index, true);
            int shift = rows[index].Start;
            var copy = new Row[end - index];
            for (int i = 0; i < copy.Length; i++)
            {
                Row row = rows[index + i];
                row.Start -= shift;
                row.Link -= index;
                copy[i] = row;
            }
            return new JsonDocument(raw.ToArray(), copy, false).RootElement;
        }

        internal void WriteElementTo(int index, Utf8JsonWriter writer)
        {
            int end = GetEndIndex(index, true);
            for (int i = index; i < end; i++)
            {
                Row row = rows[i];
                switch (row.Type)
                {
                    case JsonTokenType.StartObject: writer.WriteStartObject(); break;
                    case JsonTokenType.EndObject: writer.WriteEndObject(); break;
                    case JsonTokenType.StartArray: writer.WriteStartArray(); break;
                    case JsonTokenType.EndArray: writer.WriteEndArray(); break;
                    case JsonTokenType.PropertyName: WritePropertyName(i, writer); break;
                    case JsonTokenType.String: WriteUnescaped(row, writer, false); break;
                    case JsonTokenType.Number: writer.WriteNumberValue(utf8.Span.Slice(row.Start, row.Length)); break;
                    case JsonTokenType.True: writer.WriteBooleanValue(true); break;
                    case JsonTokenType.False: writer.WriteBooleanValue(false); break;
                    default: writer.WriteNullValue(); break;
                }
            }
        }

        internal void WritePropertyName(int nameIndex, Utf8JsonWriter writer)
        {
            CheckNotDisposed();
            WriteUnescaped(rows[nameIndex], writer, true);
        }

        void WriteUnescaped(Row row, Utf8JsonWriter writer, bool isName)
        {
            ReadOnlySpan<byte> text = utf8.Span.Slice(row.Start + 1, row.Length);
            if (!row.Escaped)
            {
                Write(writer, text, isName);
                return;
            }
            byte[] buffer = ArrayPool<byte>.Shared.Rent(text.Length);
            try
            {
                int n = JsonText.Unescape(text, buffer);
                Write(writer, buffer.AsSpan(0, n), isName);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        static void Write(Utf8JsonWriter writer, ReadOnlySpan<byte> utf8Text, bool isName)
        {
            if (isName)
                writer.WritePropertyName(utf8Text);
            else
                writer.WriteStringValue(utf8Text);
        }
    }
}
