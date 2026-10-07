using System;
using System.Numerics;
using Feeder.Json;
using Feeder.Json.Nodes;

namespace Feeder.Reflect.Json
{
public class BigIntegerJsonConverter : JsonSchemaConverter<BigInteger>, IJsonSchemaConverter
{
	public static JsonNode Schema => new JsonObject
	{
		["type"] = "string",
		["description"] = "A large integer represented as a string"
	};

	public static JsonNode SchemaRef => new JsonObject { ["$ref"] = "#/$defs/" + JsonSchemaConverter<BigInteger>.StaticId };

	public override JsonNode GetSchemaRef()
	{
		return SchemaRef;
	}

	public override JsonNode GetSchema()
	{
		return Schema;
	}

	public override BigInteger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.String)
		{
			if (BigInteger.TryParse(reader.GetString(), out var result))
			{
				return result;
			}
		}
		else if (reader.TokenType == JsonTokenType.Number)
		{
			using JsonDocument jsonDocument = JsonDocument.ParseValue(ref reader);
			if (BigInteger.TryParse(jsonDocument.RootElement.GetRawText(), out var result2))
			{
				return result2;
			}
		}
		else if (reader.TokenType == JsonTokenType.Null)
		{
			return default(BigInteger);
		}
		throw new JsonException("Expected string or number representing BigInteger.");
	}

	public override void Write(Utf8JsonWriter writer, BigInteger value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(value.ToString());
	}
}
}
