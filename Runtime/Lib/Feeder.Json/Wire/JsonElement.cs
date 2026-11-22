#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Feeder.Json
{
    public readonly partial struct JsonElement
    {
        readonly JsonDocument? parent;
        readonly int idx;

        internal JsonElement(JsonDocument parent, int idx)
        {
            this.parent = parent;
            this.idx = idx;
        }

        JsonTokenType TokenType => parent?.GetTokenType(idx) ?? JsonTokenType.None;

        JsonDocument Parent => parent ?? throw new InvalidOperationException("Operation is not valid due to the current state of the object.");

        public JsonValueKind ValueKind => ToValueKind(TokenType);

        public JsonElement GetProperty(string propertyName)
        {
            if (TryGetProperty(propertyName, out JsonElement value))
                return value;
            throw new KeyNotFoundException("The given key was not present in the dictionary.");
        }

        public bool TryGetProperty(string propertyName, out JsonElement value)
        {
            if (propertyName == null)
                throw new ArgumentNullException(nameof(propertyName));
            return Parent.TryGetProperty(idx, propertyName, out value);
        }

        public int GetArrayLength() => Parent.GetArrayLength(idx);

        public string? GetString() => Parent.GetString(idx, JsonTokenType.String);

        public bool GetBoolean()
        {
            JsonTokenType type = TokenType;
            if (type == JsonTokenType.True)
                return true;
            if (type == JsonTokenType.False)
                return false;
            throw WrongType("Boolean", type);
        }

        // unlike the other getters the reference leaves this one without the rethrow marker
        public byte GetByte() => TryGetByte(out byte value) ? value : throw new FormatException("One of the identified items was in an invalid format.");

        public bool TryGetByte(out byte value) => JsonNumber.TryGetByte(Parent.GetNumber(idx), out value);

        public int GetInt32() => TryGetInt32(out int value) ? value : throw NumberFormat();

        public bool TryGetInt32(out int value) => JsonNumber.TryGetInt32(Parent.GetNumber(idx), out value);

        public long GetInt64() => TryGetInt64(out long value) ? value : throw NumberFormat();

        public bool TryGetInt64(out long value) => JsonNumber.TryGetInt64(Parent.GetNumber(idx), out value);

        public uint GetUInt32() => TryGetUInt32(out uint value) ? value : throw NumberFormat();

        public bool TryGetUInt32(out uint value) => JsonNumber.TryGetUInt32(Parent.GetNumber(idx), out value);

        public ulong GetUInt64() => TryGetUInt64(out ulong value) ? value : throw NumberFormat();

        public bool TryGetUInt64(out ulong value) => JsonNumber.TryGetUInt64(Parent.GetNumber(idx), out value);

        public decimal GetDecimal() => TryGetDecimal(out decimal value) ? value : throw NumberFormat();

        public bool TryGetDecimal(out decimal value) => JsonNumber.TryParseDecimal(Parent.GetNumber(idx), out value);

        public double GetDouble() => JsonNumber.ParseDouble(Parent.GetNumber(idx));

        public bool TryGetDouble(out double value)
        {
            value = JsonNumber.ParseDouble(Parent.GetNumber(idx));
            return true;
        }

        public float GetSingle() => JsonNumber.ParseSingle(Parent.GetNumber(idx));

        public bool TryGetSingle(out float value)
        {
            value = JsonNumber.ParseSingle(Parent.GetNumber(idx));
            return true;
        }

        public string GetRawText() => JsonText.Utf8ToString(Parent.GetRawValue(idx));

        public void WriteTo(Utf8JsonWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            Parent.WriteElementTo(idx, writer);
        }

        public ArrayEnumerator EnumerateArray()
        {
            JsonTokenType type = Parent.GetTokenType(idx);
            if (type != JsonTokenType.StartArray)
                throw WrongType(JsonValueKind.Array, type);
            return new ArrayEnumerator(this);
        }

        public ObjectEnumerator EnumerateObject()
        {
            JsonTokenType type = Parent.GetTokenType(idx);
            if (type != JsonTokenType.StartObject)
                throw WrongType(JsonValueKind.Object, type);
            return new ObjectEnumerator(this);
        }

        public JsonElement Clone()
        {
            JsonDocument document = Parent;
            return document.IsDisposable ? document.CloneElement(idx) : this;
        }

        public static bool TryParseValue(ref Utf8JsonReader reader, [NotNullWhen(true)] out JsonElement? element)
        {
            bool parsed = JsonDocument.TryParseValue(ref reader, out JsonDocument? document, false, false);
            element = document?.RootElement;
            return parsed;
        }

        public override string? ToString()
        {
            switch (TokenType)
            {
                case JsonTokenType.True:
                    return bool.TrueString;
                case JsonTokenType.False:
                    return bool.FalseString;
                case JsonTokenType.String:
                    return GetString();
                case JsonTokenType.Number:
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    return GetRawText();
                default:
                    return string.Empty;
            }
        }

        internal string GetPropertyName() => Parent.GetString(idx - 1, JsonTokenType.PropertyName)!;

        internal string GetPropertyRawText() => Parent.GetPropertyRawText(idx);

        internal void WritePropertyNameTo(Utf8JsonWriter writer) => Parent.WritePropertyName(idx - 1, writer);

        static JsonValueKind ToValueKind(JsonTokenType type)
        {
            switch (type)
            {
                case JsonTokenType.StartObject: return JsonValueKind.Object;
                case JsonTokenType.StartArray: return JsonValueKind.Array;
                case JsonTokenType.String: return JsonValueKind.String;
                case JsonTokenType.Number: return JsonValueKind.Number;
                case JsonTokenType.True: return JsonValueKind.True;
                case JsonTokenType.False: return JsonValueKind.False;
                case JsonTokenType.Null: return JsonValueKind.Null;
                default: return JsonValueKind.Undefined;
            }
        }

        internal static InvalidOperationException WrongType(JsonValueKind expected, JsonTokenType actual) => WrongType(expected.ToString(), actual);

        static InvalidOperationException WrongType(string expected, JsonTokenType actual) =>
            JsonText.InvalidOperation("The requested operation requires an element of type '" + expected + "', but the target element has type '" + ToValueKind(actual) + "'.");

        static FormatException NumberFormat() => JsonText.Format("One of the identified items was in an invalid format.");
    }
}
