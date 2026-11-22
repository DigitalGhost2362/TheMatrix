using System;
using Feeder.Json;
using Feeder.Json.Nodes;

namespace Feeder.Reflect.Json
{
public class VersionJsonConverter : JsonSchemaConverter<Version>, IJsonSchemaConverter
{
	public static JsonNode Schema => new JsonObject { ["type"] = "string" };

	public static JsonNode SchemaRef => new JsonObject { ["$ref"] = "#/$defs/" + JsonSchemaConverter<Version>.StaticId };

	public override JsonNode GetSchema()
	{
		return Schema;
	}

	public override JsonNode GetSchemaRef()
	{
		return SchemaRef;
	}

	public override Version? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return null;
		}
		if (reader.TokenType != JsonTokenType.String)
		{
			throw new JsonException($"Expected string token for Version, but got {reader.TokenType}");
		}
		string text = reader.GetString();
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		if (Version.TryParse(text, out Version result))
		{
			return result;
		}
		throw new JsonException("Unable to parse '" + text + "' as a Version.");
	}

	public override void Write(Utf8JsonWriter writer, Version? value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
		}
		else
		{
			writer.WriteStringValue(value.ToString());
		}
	}
}
}
