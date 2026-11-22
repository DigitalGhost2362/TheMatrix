using System;
using Feeder.Json;
using Feeder.Json.Nodes;
using Feeder.Reflect.Utils;

namespace Feeder.Reflect.Json
{
public class TypeJsonConverter : JsonSchemaConverter<Type>, IJsonSchemaConverter
{
	public static JsonNode Schema => new JsonObject { ["type"] = "string" };

	public static JsonNode SchemaRef => new JsonObject { ["$ref"] = "#/$defs/" + JsonSchemaConverter<Type>.StaticId };

	public override JsonNode GetSchemaRef()
	{
		return SchemaRef;
	}

	public override JsonNode GetSchema()
	{
		return Schema;
	}

	public override bool CanConvert(Type typeToConvert)
	{
		return (Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert) == typeof(Type);
	}

	public override Type? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return null;
		}
		if (reader.TokenType == JsonTokenType.String)
		{
			return TypeUtils.GetType(reader.GetString());
		}
		throw new JsonException("Expected string which represents System.Type in the format `System.Int32`");
	}

	public override void Write(Utf8JsonWriter writer, Type? value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
		}
		else
		{
			writer.WriteStringValue(value.GetTypeId());
		}
	}
}
}
