#nullable enable
#if !UNITY_6000_5_OR_NEWER
using System;
using System.Globalization;
using Feeder.Json;
using Feeder.Json.Serialization;
using Feeder.MCP.Data;

namespace Feeder.MCP.JsonConverters
{
    public class ObjectRefConverter : JsonConverter<ObjectRef>
    {
        // agents and 6.5-era docs send the id as a string or null, and options read property names case-insensitively
        internal static string? CanonicalPropertyName(string? propertyName)
            => string.Equals(propertyName, ObjectRef.ObjectRefProperty.InstanceID, StringComparison.OrdinalIgnoreCase)
                ? ObjectRef.ObjectRefProperty.InstanceID
                : propertyName;

        internal static int ReadInstanceId(ref Utf8JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return 0;
                case JsonTokenType.String:
                    var text = reader.GetString();
                    if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
                        return parsed;
                    throw new JsonException($"'{ObjectRef.ObjectRefProperty.InstanceID}' must be a 32-bit integer before Unity 6.5, got \"{text}\".");
                case JsonTokenType.Number:
                    if (reader.TryGetInt32(out var number))
                        return number;
                    throw new JsonException($"'{ObjectRef.ObjectRefProperty.InstanceID}' must be a 32-bit integer before Unity 6.5.");
                default:
                    throw new JsonException($"'{ObjectRef.ObjectRefProperty.InstanceID}' must be a number or a string, got {reader.TokenType}.");
            }
        }

        public override ObjectRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Expected start of object token.");

            var objectRef = new ObjectRef();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    return objectRef;

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var propertyName = CanonicalPropertyName(reader.GetString());
                    reader.Read(); // Move to the value token

                    switch (propertyName)
                    {
                        case ObjectRef.ObjectRefProperty.InstanceID:
                            objectRef.InstanceID = ReadInstanceId(ref reader);
                            break;
                        default:
                            throw new JsonException($"Unexpected property name: {propertyName}. "
                                + $"Expected '{ObjectRef.ObjectRefProperty.InstanceID}'.");
                    }
                }
            }

            throw new JsonException("Expected end of object token.");
        }

        public override void Write(Utf8JsonWriter writer, ObjectRef value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteStartObject();

                // Write the "instanceID" property
                writer.WriteNumber(ObjectRef.ObjectRefProperty.InstanceID, 0);

                writer.WriteEndObject();
                return;
            }

            writer.WriteStartObject();

            // Write the "instanceID" property
            writer.WriteNumber(ObjectRef.ObjectRefProperty.InstanceID, value.InstanceID);

            writer.WriteEndObject();
        }
    }
}
#endif
