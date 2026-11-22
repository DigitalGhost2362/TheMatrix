#nullable enable
using System;
using Feeder.Json.Serialization;

namespace Feeder.Json.Nodes
{
    public abstract class JsonValue : JsonNode
    {
        private protected JsonValue(JsonNodeOptions? options) : base(options)
        {
        }

        public static JsonValue Create(bool value, JsonNodeOptions? options = null) => new JsonValueOf<bool>(value, options);

        public static JsonValue? Create(bool? value, JsonNodeOptions? options = null) => Of(value, options);

        public static JsonValue Create(byte value, JsonNodeOptions? options = null) => new JsonValueOf<byte>(value, options);

        public static JsonValue? Create(byte? value, JsonNodeOptions? options = null) => Of(value, options);

        public static JsonValue Create(char value, JsonNodeOptions? options = null) => new JsonValueOf<char>(value, options);

        public static JsonValue? Create(char? value, JsonNodeOptions? options = null) => Of(value, options);

        public static JsonValue Create(DateTime value, JsonNodeOptions? options = null) => new JsonValueOf<DateTime>(value, options);

        public static JsonValue? Create(DateTime? value, JsonNodeOptions? options = null) => Of(value, options);

        public static JsonValue Create(DateTimeOffset value, JsonNodeOptions? options = null) => new JsonValueOf<DateTimeOffset>(value, options);

        public static JsonValue? Create(DateTimeOffset? value, JsonNodeOptions? options = null) => Of(value, options);

        public static JsonValue Create(decimal value, JsonNodeOptions? options = null) => new JsonValueOf<decimal>(value, options);

        public static JsonValue? Create(decimal? value, JsonNodeOptions? options = null) => Of(value, options);

        public static JsonValue Create(double value, JsonNodeOptions? options = null) => new JsonValueOf<double>(value, options);

        public static JsonValue? Create(double? value, JsonNodeOptions? options = null) => Of(value, options);

        public static JsonValue Create(Guid value, JsonNodeOptions? options = null) => new JsonValueOf<Guid>(value, options);

        public static JsonValue? Create(Guid? value, JsonNodeOptions? options = null) => Of(value, options);

        public static JsonValue Create(short value, JsonNodeOptions? options = null) => new JsonValueOf<short>(value, options);

        public static JsonValue? Create(short? value, JsonNodeOptions? options = null) => Of(value, options);

        public static JsonValue Create(int value, JsonNodeOptions? options = null) => new JsonValueOf<int>(value, options);

        public static JsonValue? Create(int? value, JsonNodeOptions? options = null) => Of(value, options);

        public static JsonValue Create(long value, JsonNodeOptions? options = null) => new JsonValueOf<long>(value, options);

        public static JsonValue? Create(long? value, JsonNodeOptions? options = null) => Of(value, options);

        public static JsonValue Create(sbyte value, JsonNodeOptions? options = null) => new JsonValueOf<sbyte>(value, options);

        public static JsonValue? Create(sbyte? value, JsonNodeOptions? options = null) => Of(value, options);

        public static JsonValue Create(float value, JsonNodeOptions? options = null) => new JsonValueOf<float>(value, options);

        public static JsonValue? Create(float? value, JsonNodeOptions? options = null) => Of(value, options);

        public static JsonValue? Create(string? value, JsonNodeOptions? options = null) => value == null ? null : new JsonValueOf<string>(value, options);

        public static JsonValue Create(ushort value, JsonNodeOptions? options = null) => new JsonValueOf<ushort>(value, options);

        public static JsonValue? Create(ushort? value, JsonNodeOptions? options = null) => Of(value, options);

        public static JsonValue Create(uint value, JsonNodeOptions? options = null) => new JsonValueOf<uint>(value, options);

        public static JsonValue? Create(uint? value, JsonNodeOptions? options = null) => Of(value, options);

        public static JsonValue Create(ulong value, JsonNodeOptions? options = null) => new JsonValueOf<ulong>(value, options);

        public static JsonValue? Create(ulong? value, JsonNodeOptions? options = null) => Of(value, options);

        public static JsonValue? Create(JsonElement value, JsonNodeOptions? options = null)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Null:
                    return null;
                case JsonValueKind.Object:
                case JsonValueKind.Array:
                    throw new InvalidOperationException("A JsonElement of type 'Object' or 'Array' cannot be converted to a JsonValue.");
                default:
                    return new JsonValueOfElement(value, options);
            }
        }

        public abstract bool TryGetValue<T>(out T? value);

        internal abstract bool TryGetString(out string? value);

        static JsonValue? Of<T>(T? value, JsonNodeOptions? options) where T : struct =>
            value.HasValue ? new JsonValueOf<T>(value.Value, options) : null;
    }

    // a CLR value; GetValue and TryGetValue only hand it out as its own type, never converted
    internal sealed class JsonValueOf<TValue> : JsonValue
    {
        readonly TValue value;

        internal JsonValueOf(TValue value, JsonNodeOptions? options) : base(options)
        {
            this.value = value;
        }

        public override T GetValue<T>() =>
            value is T result ? result : throw new InvalidOperationException("A value of type '" + typeof(TValue) + "' cannot be converted to a '" + typeof(T) + "'.");

        public override bool TryGetValue<T>(out T? result) where T : default
        {
            if (value is T typed)
            {
                result = typed;
                return true;
            }
            result = default;
            return false;
        }

        public override void WriteTo(Utf8JsonWriter writer, JsonSerializerOptions? options = null) =>
            JsonSerializer.WriteNodeValue(writer, value!, typeof(TValue), options);

        internal override bool TryGetString(out string? text)
        {
            text = value as string;
            return typeof(TValue) == typeof(string);
        }

        private protected override JsonValueKind GetValueKindCore()
        {
            switch (Type.GetTypeCode(typeof(TValue)))
            {
                case TypeCode.Boolean:
                    return (bool)(object)value! ? JsonValueKind.True : JsonValueKind.False;
                case TypeCode.String:
                case TypeCode.Char:
                    return JsonValueKind.String;
                case TypeCode.Object:
                case TypeCode.DateTime:
                    return JsonSerializer.SerializeToElement(value).ValueKind;
                default:
                    return JsonValueKind.Number;
            }
        }

        internal override JsonNode DeepCloneCore() => new JsonValueOf<TValue>(value, Options);
    }

    // a parsed primitive; the getters convert its text the way the reference element-backed value does
    internal sealed class JsonValueOfElement : JsonValue
    {
        readonly JsonElement element;

        internal JsonValueOfElement(JsonElement element, JsonNodeOptions? options) : base(options)
        {
            this.element = element;
        }

        public override T GetValue<T>() =>
            TryGetValue(out T? result) ? result! : throw new InvalidOperationException("An element of type '" + element.ValueKind + "' cannot be converted to a '" + typeof(T) + "'.");

        // like the reference, a failed numeric or date read still hands out the type's zero, not null
        public override bool TryGetValue<T>(out T? result) where T : default
        {
            if (element is T same)
            {
                result = same;
                return true;
            }
            if (TryConvert(Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), out object? converted, out bool success))
            {
                result = (T)converted!;
                return success;
            }
            result = default;
            return false;
        }

        public override void WriteTo(Utf8JsonWriter writer, JsonSerializerOptions? options = null)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            element.WriteTo(writer);
        }

        internal override bool TryGetString(out string? text)
        {
            bool isString = element.ValueKind == JsonValueKind.String;
            text = isString ? element.GetString() : null;
            return isString;
        }

        private protected override JsonValueKind GetValueKindCore() => element.ValueKind;

        internal override JsonNode DeepCloneCore() => new JsonValueOfElement(element.Clone(), Options);

        // false when the element kind has no conversion to the type at all; strings never become numbers
        bool TryConvert(Type target, out object? value, out bool success)
        {
            value = null;
            success = false;
            switch (element.ValueKind)
            {
                case JsonValueKind.Number:
                    switch (Type.GetTypeCode(target))
                    {
                        case TypeCode.Int32:
                            success = element.TryGetInt32(out int int32);
                            value = int32;
                            return true;
                        case TypeCode.Int64:
                            success = element.TryGetInt64(out long int64);
                            value = int64;
                            return true;
                        case TypeCode.Double:
                            success = element.TryGetDouble(out double float64);
                            value = float64;
                            return true;
                        case TypeCode.Int16:
                            success = element.TryGetInt32(out int int16) && int16 >= short.MinValue && int16 <= short.MaxValue;
                            value = success ? (short)int16 : (short)0;
                            return true;
                        case TypeCode.Decimal:
                            success = element.TryGetDecimal(out decimal number);
                            value = number;
                            return true;
                        case TypeCode.Byte:
                            success = element.TryGetByte(out byte uint8);
                            value = uint8;
                            return true;
                        case TypeCode.Single:
                            success = element.TryGetSingle(out float float32);
                            value = float32;
                            return true;
                        case TypeCode.UInt32:
                            success = element.TryGetUInt32(out uint uint32);
                            value = uint32;
                            return true;
                        case TypeCode.UInt16:
                            success = element.TryGetUInt32(out uint uint16) && uint16 <= ushort.MaxValue;
                            value = success ? (ushort)uint16 : (ushort)0;
                            return true;
                        case TypeCode.UInt64:
                            success = element.TryGetUInt64(out ulong uint64);
                            value = uint64;
                            return true;
                        case TypeCode.SByte:
                            success = element.TryGetInt32(out int int8) && int8 >= sbyte.MinValue && int8 <= sbyte.MaxValue;
                            value = success ? (sbyte)int8 : (sbyte)0;
                            return true;
                    }
                    return false;
                case JsonValueKind.String:
                    string text = element.GetString()!;
                    if (target == typeof(string))
                    {
                        value = text;
                        success = true;
                        return true;
                    }
                    if (target == typeof(DateTime))
                    {
                        success = IsoDate.TryParse(text, out DateTime date);
                        value = date;
                        return true;
                    }
                    if (target == typeof(DateTimeOffset))
                    {
                        success = IsoDate.TryParse(text, out DateTimeOffset offset);
                        value = offset;
                        return true;
                    }
                    if (target == typeof(Guid))
                    {
                        Guid guid = Guid.Empty;
                        success = text.Length == 36 && Guid.TryParseExact(text, "D", out guid);
                        value = guid;
                        return true;
                    }
                    if (target == typeof(char) && text.Length == 1)
                    {
                        value = text[0];
                        success = true;
                        return true;
                    }
                    return false;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    if (target != typeof(bool))
                        return false;
                    value = element.GetBoolean();
                    success = true;
                    return true;
                default:
                    return false;
            }
        }
    }
}
