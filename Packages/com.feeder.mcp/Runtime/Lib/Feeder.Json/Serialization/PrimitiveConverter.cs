#nullable enable
using System;
using System.Globalization;
using System.Text;
using Feeder.Json.Nodes;

namespace Feeder.Json.Serialization
{
    internal enum PrimitiveCode : byte
    {
        Boolean,
        Byte,
        SByte,
        Int16,
        UInt16,
        Int32,
        UInt32,
        Int64,
        UInt64,
        Single,
        Double,
        Decimal,
        Char,
        String,
        DateTime,
        DateTimeOffset,
        Guid,
        TimeSpan,
        Uri,
        Bytes,
        Enum,
        Element,
        Document,
        Node,
        Object,
        Unsupported,
    }

    internal sealed class PrimitiveConverter : JsonConverter
    {
        const NumberStyles IntegerStyle = NumberStyles.AllowLeadingSign;
        const NumberStyles FloatStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

        readonly Type type;
        readonly PrimitiveCode code;

        internal PrimitiveConverter(Type type, PrimitiveCode code)
        {
            this.type = type;
            this.code = code;
        }

        // the types a JsonValue writes with its own built-in converter instead of the caller's options
        internal bool IsNodePrimitive => code <= PrimitiveCode.Uri;

        internal override ConverterStrategy Strategy => code == PrimitiveCode.Object ? ConverterStrategy.Polymorphic : ConverterStrategy.Value;

        // struct converters see null tokens and fail inside the reader, as the reference built-ins do
        internal override bool HandleNullOnRead => type.IsValueType;

        public override bool CanConvert(Type typeToConvert) => typeToConvert == type;

        internal override object? ReadAsObject(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            JsonNumberHandling handling = options.NumberHandling;
            switch (code)
            {
                case PrimitiveCode.Boolean:
                    return reader.GetBoolean();
                case PrimitiveCode.Single:
                case PrimitiveCode.Double:
                    return ReadFloatingPoint(ref reader, handling);
                case PrimitiveCode.Decimal:
                    if (reader.TokenType == JsonTokenType.String && (handling & JsonNumberHandling.AllowReadingFromString) != 0)
                        return ParseDecimal(reader.GetString()!);
                    return reader.GetDecimal();
                case PrimitiveCode.Char:
                    string text = ReadString(ref reader);
                    return text.Length == 1 ? text[0] : throw JsonText.InvalidOperation("Cannot get the value of a token type 'String' as a char.");
                case PrimitiveCode.String:
                    return reader.GetString();
                case PrimitiveCode.DateTime:
                    return IsoDate.TryParse(ReadString(ref reader), out DateTime dateTime) ? dateTime : throw JsonText.Format("The JSON value is not in a supported DateTime format.");
                case PrimitiveCode.DateTimeOffset:
                    return IsoDate.TryParse(ReadString(ref reader), out DateTimeOffset offset) ? offset : throw JsonText.Format("The JSON value is not in a supported DateTimeOffset format.");
                case PrimitiveCode.Guid:
                    string guid = ReadString(ref reader);
                    return guid.Length == 36 && Guid.TryParseExact(guid, "D", out Guid value) ? value : throw JsonText.Format("The JSON value is not in a supported Guid format.");
                case PrimitiveCode.TimeSpan:
                    return ParseTimeSpan(ReadString(ref reader));
                case PrimitiveCode.Uri:
                    return Uri.TryCreate(reader.GetString(), UriKind.RelativeOrAbsolute, out Uri? uri) ? uri : throw JsonSerializer.CannotConvert(type);
                case PrimitiveCode.Bytes:
                    return ReadBase64(ref reader);
                case PrimitiveCode.Enum:
                    return ReadEnumNumber(ref reader, type);
                case PrimitiveCode.Element:
                case PrimitiveCode.Object:
                    return JsonSerializer.ParseElement(ref reader);
                case PrimitiveCode.Document:
                    return JsonDocument.ParseValue(ref reader);
                case PrimitiveCode.Node:
                    return ReadNode(ref reader, options);
                case PrimitiveCode.Unsupported:
                    throw Unsupported();
                default:
                    return ReadInteger(ref reader, handling);
            }
        }

        internal override void WriteAsObject(Utf8JsonWriter writer, object value, JsonSerializerOptions options) =>
            Write(writer, value, options.NumberHandling, options);

        internal void Write(Utf8JsonWriter writer, object value, JsonNumberHandling handling, JsonSerializerOptions? options)
        {
            switch (code)
            {
                case PrimitiveCode.Boolean: writer.WriteBooleanValue((bool)value); break;
                case PrimitiveCode.Byte: writer.WriteNumberValue((byte)value); break;
                case PrimitiveCode.SByte: writer.WriteNumberValue((sbyte)value); break;
                case PrimitiveCode.Int16: writer.WriteNumberValue((short)value); break;
                case PrimitiveCode.UInt16: writer.WriteNumberValue((ushort)value); break;
                case PrimitiveCode.Int32: writer.WriteNumberValue((int)value); break;
                case PrimitiveCode.UInt32: writer.WriteNumberValue((uint)value); break;
                case PrimitiveCode.Int64: writer.WriteNumberValue((long)value); break;
                case PrimitiveCode.UInt64: writer.WriteNumberValue((ulong)value); break;
                case PrimitiveCode.Single: WriteFloatingPoint(writer, (float)value, handling); break;
                case PrimitiveCode.Double: WriteFloatingPoint(writer, (double)value, handling); break;
                case PrimitiveCode.Decimal: writer.WriteNumberValue((decimal)value); break;
                case PrimitiveCode.Char: writer.WriteStringValue(((char)value).ToString()); break;
                case PrimitiveCode.String: writer.WriteStringValue((string)value); break;
                case PrimitiveCode.DateTime: writer.WriteAsciiStringValue(IsoDate.Format(((DateTime)value).ToString("O", CultureInfo.InvariantCulture))); break;
                case PrimitiveCode.DateTimeOffset: writer.WriteAsciiStringValue(IsoDate.Format(((DateTimeOffset)value).ToString("O", CultureInfo.InvariantCulture))); break;
                case PrimitiveCode.Guid: writer.WriteAsciiStringValue(((Guid)value).ToString("D")); break;
                case PrimitiveCode.TimeSpan: writer.WriteAsciiStringValue(((TimeSpan)value).ToString("c", CultureInfo.InvariantCulture)); break;
                case PrimitiveCode.Uri: writer.WriteStringValue(((Uri)value).OriginalString); break;
                case PrimitiveCode.Bytes: writer.WriteBase64StringValue((byte[])value); break;
                case PrimitiveCode.Enum: WriteEnumNumber(writer, value); break;
                case PrimitiveCode.Element: ((JsonElement)value).WriteTo(writer); break;
                case PrimitiveCode.Document: ((JsonDocument)value).RootElement.WriteTo(writer); break;
                case PrimitiveCode.Node: ((JsonNode)value).WriteTo(writer, options); break;
                default: throw Unsupported();
            }
        }

        internal static object ReadEnumNumber(ref Utf8JsonReader reader, Type enumType)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                TypeCode typeCode = Type.GetTypeCode(enumType);
                if (typeCode == TypeCode.UInt64)
                {
                    if (reader.TryGetUInt64(out ulong unsigned))
                        return Enum.ToObject(enumType, unsigned);
                }
                else if (reader.TryGetInt64(out long signed) && InRange(typeCode, signed))
                {
                    return Enum.ToObject(enumType, signed);
                }
            }
            throw JsonSerializer.CannotConvert(enumType);
        }

        internal static void WriteEnumNumber(Utf8JsonWriter writer, object value)
        {
            switch (Type.GetTypeCode(value.GetType()))
            {
                case TypeCode.Byte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                    writer.WriteNumberValue(Convert.ToUInt64(value, CultureInfo.InvariantCulture));
                    break;
                default:
                    writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                    break;
            }
        }

        static void WriteFloatingPoint(Utf8JsonWriter writer, double value, JsonNumberHandling handling)
        {
            if ((handling & JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0 && (double.IsNaN(value) || double.IsInfinity(value)))
                writer.WriteStringValue(double.IsNaN(value) ? "NaN" : value > 0 ? "Infinity" : "-Infinity");
            else
                writer.WriteNumberValue(value);
        }

        static void WriteFloatingPoint(Utf8JsonWriter writer, float value, JsonNumberHandling handling)
        {
            if ((handling & JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0 && (float.IsNaN(value) || float.IsInfinity(value)))
                writer.WriteStringValue(float.IsNaN(value) ? "NaN" : value > 0 ? "Infinity" : "-Infinity");
            else
                writer.WriteNumberValue(value);
        }

        object ReadInteger(ref Utf8JsonReader reader, JsonNumberHandling handling)
        {
            if (reader.TokenType == JsonTokenType.String && (handling & JsonNumberHandling.AllowReadingFromString) != 0)
            {
                string text = reader.GetString()!;
                if (code == PrimitiveCode.UInt64)
                    return ulong.TryParse(text, IntegerStyle, CultureInfo.InvariantCulture, out ulong unsigned) ? unsigned : throw NumberFormat();
                return long.TryParse(text, IntegerStyle, CultureInfo.InvariantCulture, out long parsed) && InRange(Code(), parsed) ? Box(parsed) : throw NumberFormat();
            }
            switch (code)
            {
                case PrimitiveCode.Byte: return reader.GetByte();
                case PrimitiveCode.Int32: return reader.GetInt32();
                case PrimitiveCode.UInt32: return reader.GetUInt32();
                case PrimitiveCode.Int64: return reader.GetInt64();
                case PrimitiveCode.UInt64: return reader.GetUInt64();
            }
            long value = reader.GetInt64();
            return InRange(Code(), value) ? Box(value) : throw NumberFormat();
        }

        object ReadFloatingPoint(ref Utf8JsonReader reader, JsonNumberHandling handling)
        {
            bool single = code == PrimitiveCode.Single;
            if (reader.TokenType == JsonTokenType.String && (handling & (JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)) != 0)
            {
                string text = reader.GetString()!;
                double named;
                switch (text)
                {
                    case "NaN": named = double.NaN; break;
                    case "Infinity": named = double.PositiveInfinity; break;
                    case "-Infinity": named = double.NegativeInfinity; break;
                    default:
                        if ((handling & JsonNumberHandling.AllowReadingFromString) == 0)
                            throw NumberFormat();
                        return single ? ParseSingle(text) : ParseDouble(text);
                }
                return single ? (object)(float)named : named;
            }
            return single ? reader.GetSingle() : (object)reader.GetDouble();
        }

        object ParseDouble(string text)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(text);
            double value = JsonNumber.IsValid(utf8) ? JsonNumber.ParseDouble(utf8)
                : double.TryParse(text, FloatStyle, CultureInfo.InvariantCulture, out double parsed) ? parsed : double.NaN;
            return double.IsNaN(value) || double.IsInfinity(value) ? throw NumberFormat() : value;
        }

        object ParseSingle(string text)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(text);
            float value = JsonNumber.IsValid(utf8) ? JsonNumber.ParseSingle(utf8)
                : float.TryParse(text, FloatStyle, CultureInfo.InvariantCulture, out float parsed) ? parsed : float.NaN;
            return float.IsNaN(value) || float.IsInfinity(value) ? throw NumberFormat() : value;
        }

        object ParseDecimal(string text)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(text);
            if (JsonNumber.IsValid(utf8) ? JsonNumber.TryParseDecimal(utf8, out decimal value) : decimal.TryParse(text, FloatStyle, CultureInfo.InvariantCulture, out value))
                return value;
            throw NumberFormat();
        }

        static object ParseTimeSpan(string text)
        {
            if (text.Length > 0 && (char.IsDigit(text[0]) || text[0] == '-') && char.IsDigit(text[text.Length - 1])
                && TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out TimeSpan value))
                return value;
            throw JsonText.Format("The JSON value is not in a supported TimeSpan format.");
        }

        static byte[] ReadBase64(ref Utf8JsonReader reader)
        {
            string text = ReadString(ref reader);
            try
            {
                return Convert.FromBase64String(text);
            }
            catch (FormatException)
            {
                throw JsonText.Format("The JSON value is not in a supported Base64 format.");
            }
        }

        object? ReadNode(ref Utf8JsonReader reader, JsonSerializerOptions options)
        {
            JsonTokenType token = reader.TokenType;
            if (type == typeof(JsonObject) && token != JsonTokenType.StartObject)
                throw JsonText.InvalidOperation("Cannot get the value of a token type '" + token + "' as an object.");
            if (type == typeof(JsonArray) && token != JsonTokenType.StartArray)
                throw JsonText.InvalidOperation("Cannot get the value of a token type '" + token + "' as an array.");
            JsonElement element = JsonSerializer.ParseElement(ref reader);
            JsonNodeOptions nodeOptions = options.GetNodeOptions();
            return type == typeof(JsonValue) ? JsonValue.Create(element, nodeOptions) : JsonNode.FromElement(element, nodeOptions);
        }

        static string ReadString(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw JsonText.InvalidOperation("Cannot get the value of a token type '" + reader.TokenType + "' as a string.");
            return reader.GetString()!;
        }

        TypeCode Code() => code switch
        {
            PrimitiveCode.Byte => TypeCode.Byte,
            PrimitiveCode.SByte => TypeCode.SByte,
            PrimitiveCode.Int16 => TypeCode.Int16,
            PrimitiveCode.UInt16 => TypeCode.UInt16,
            PrimitiveCode.Int32 => TypeCode.Int32,
            PrimitiveCode.UInt32 => TypeCode.UInt32,
            _ => TypeCode.Int64,
        };

        object Box(long value) => code switch
        {
            PrimitiveCode.Byte => (byte)value,
            PrimitiveCode.SByte => (sbyte)value,
            PrimitiveCode.Int16 => (short)value,
            PrimitiveCode.UInt16 => (ushort)value,
            PrimitiveCode.Int32 => (int)value,
            PrimitiveCode.UInt32 => (uint)value,
            _ => (object)value,
        };

        static bool InRange(TypeCode typeCode, long value) => typeCode switch
        {
            TypeCode.Byte => value >= byte.MinValue && value <= byte.MaxValue,
            TypeCode.SByte => value >= sbyte.MinValue && value <= sbyte.MaxValue,
            TypeCode.Int16 => value >= short.MinValue && value <= short.MaxValue,
            TypeCode.UInt16 => value >= ushort.MinValue && value <= ushort.MaxValue,
            TypeCode.Int32 => value >= int.MinValue && value <= int.MaxValue,
            TypeCode.UInt32 => value >= uint.MinValue && value <= uint.MaxValue,
            _ => true,
        };

        FormatException NumberFormat() => JsonText.Format("The JSON value is not in a supported " + type.Name + " format.");

        NotSupportedException Unsupported() =>
            new NotSupportedException("Serialization and deserialization of '" + type + "' instances is not supported.");
    }
}
