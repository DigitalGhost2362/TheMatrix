#nullable enable
#if UNITY_6000_5_OR_NEWER
using System;
using System.Globalization;
using Feeder.Json;
using Feeder.Json.Serialization;
using Feeder.MCP.Data;

namespace Feeder.MCP.JsonConverters
{
    public class ObjectRefConverter : JsonConverter<ObjectRef>
    {
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
                    var propertyName = reader.GetString();
                    reader.Read(); // Move to the value token

                    switch (propertyName)
                    {
                        case ObjectRef.ObjectRefProperty.InstanceID:
                            objectRef.InstanceID = EntityIdConverter.ReadEntityIdValue(ref reader);
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

                // Write the "instanceID" property — see EntityIdConverter
                // top-of-file wire contract: outbound is always a
                // JSON string of decimal digits, never a number.
                writer.WriteString(ObjectRef.ObjectRefProperty.InstanceID, "0");

                writer.WriteEndObject();
                return;
            }

            writer.WriteStartObject();

            // Write the "instanceID" property (JSON string, see the top-of-file wire contract).
            writer.WriteString(ObjectRef.ObjectRefProperty.InstanceID, UnityEngine.EntityId.ToULong(value.InstanceID).ToString(CultureInfo.InvariantCulture));

            writer.WriteEndObject();
        }
    }
}
#endif
