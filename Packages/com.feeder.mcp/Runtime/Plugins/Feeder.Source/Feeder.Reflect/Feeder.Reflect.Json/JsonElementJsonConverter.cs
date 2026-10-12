using System;
using Feeder.Json;
using Feeder.Json.Nodes;

namespace Feeder.Reflect.Json
{
public class JsonElementJsonConverter : JsonSchemaConverter<JsonElement>, IJsonSchemaConverter
{
	public static JsonNode Schema => JsonArrayJsonConverter.JsonAnySchema;

	public static JsonNode SchemaRef => new JsonObject { ["$ref"] = "#/$defs/" + JsonSchemaConverter<JsonElement>.StaticId };

	public override JsonNode GetSchema()
	{
		return Schema;
	}

	public override JsonNode GetSchemaRef()
	{
		return SchemaRef;
	}

	public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return default(JsonElement);
		}
		return JsonDocument.ParseValue(ref reader).RootElement.Clone();
	}

	public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options)
	{
		if (value.ValueKind == JsonValueKind.Null)
		{
			writer.WriteNullValue();
		}
		else
		{
			value.WriteTo(writer);
		}
	}
}
}
