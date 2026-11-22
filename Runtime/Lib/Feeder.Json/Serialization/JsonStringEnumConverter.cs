#nullable enable
using System;

namespace Feeder.Json.Serialization
{
    public class JsonStringEnumConverter : JsonConverter
    {
        public JsonStringEnumConverter()
        {
        }

        public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

        // names match exactly first, then ignoring case; numeric text and comma-separated flags parse like Enum.TryParse
        internal override object? ReadAsObject(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                return PrimitiveConverter.ReadEnumNumber(ref reader, typeToConvert);
            string text = reader.GetString()!;
            if (Enum.TryParse(typeToConvert, text, false, out object? value) || Enum.TryParse(typeToConvert, text, true, out value))
                return value;
            throw JsonSerializer.CannotConvert(typeToConvert);
        }

        // undefined values have no name and fall back to the number
        internal override void WriteAsObject(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            string name = value.ToString()!;
            if (name[0] >= 'A')
                writer.WriteStringValue(name);
            else
                PrimitiveConverter.WriteEnumNumber(writer, value);
        }
    }
}
