using System;
using System.Net;
using Feeder.Json;
using Feeder.Json.Nodes;

namespace Feeder.Reflect.Json
{
public class IPAddressJsonConverter : JsonSchemaConverter<IPAddress>, IJsonSchemaConverter
{
	public static JsonNode Schema => new JsonObject
	{
		["type"] = "string",
		["format"] = "ipv4-or-ipv6"
	};

	public static JsonNode SchemaRef => new JsonObject { ["$ref"] = "#/$defs/" + JsonSchemaConverter<IPAddress>.StaticId };

	public override JsonNode GetSchemaRef()
	{
		return SchemaRef;
	}

	public override JsonNode GetSchema()
	{
		return Schema;
	}

	public override IPAddress? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return null;
		}
		if (reader.TokenType != JsonTokenType.String)
		{
			throw new JsonException("Expected string for IPAddress.");
		}
		string text = reader.GetString();
		if (IPAddress.TryParse(text, out IPAddress address))
		{
			return address;
		}
		throw new JsonException("Invalid IPAddress format: " + text);
	}

	public override void Write(Utf8JsonWriter writer, IPAddress? value, JsonSerializerOptions options)
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
