using System;
using System.Net;
using Feeder.Json;
using Feeder.Json.Nodes;

namespace Feeder.Reflect.Json
{
public class IPEndPointJsonConverter : JsonSchemaConverter<IPEndPoint>, IJsonSchemaConverter
{
	private const string AddressProperty = "address";

	private const string PortProperty = "port";

	public static JsonNode Schema => new JsonObject
	{
		["type"] = "object",
		["properties"] = new JsonObject
		{
			["address"] = IPAddressJsonConverter.SchemaRef,
			["port"] = new JsonObject
			{
				["type"] = "integer",
				["minimum"] = 0,
				["maximum"] = 65535
			}
		},
		["required"] = new JsonArray { "address", "port" }
	};

	public static JsonNode SchemaRef => new JsonObject { ["$ref"] = "#/$defs/" + JsonSchemaConverter<IPEndPoint>.StaticId };

	public override JsonNode GetSchemaRef()
	{
		return SchemaRef;
	}

	public override JsonNode GetSchema()
	{
		return Schema;
	}

	public override IPEndPoint? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return null;
		}
		if (reader.TokenType != JsonTokenType.StartObject)
		{
			throw new JsonException("Expected StartObject for IPEndPoint.");
		}
		IPAddress iPAddress = null;
		int port = 0;
		bool flag = false;
		bool flag2 = false;
		while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
		{
			if (reader.TokenType == JsonTokenType.PropertyName)
			{
				string text = reader.GetString();
				reader.Read();
				if (text == null)
				{
					reader.Skip();
				}
				else if (string.Equals(text, "address", StringComparison.OrdinalIgnoreCase))
				{
					iPAddress = JsonSerializer.Deserialize<IPAddress>(ref reader, options);
					flag = true;
				}
				else if (string.Equals(text, "port", StringComparison.OrdinalIgnoreCase))
				{
					port = reader.GetInt32();
					flag2 = true;
				}
				else
				{
					reader.Skip();
				}
			}
		}
		if (!flag || !flag2 || iPAddress == null)
		{
			throw new JsonException("IPEndPoint requires both 'address' and 'port' properties.");
		}
		return new IPEndPoint(iPAddress, port);
	}

	public override void Write(Utf8JsonWriter writer, IPEndPoint? value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
			return;
		}
		writer.WriteStartObject();
		writer.WritePropertyName("address");
		JsonSerializer.Serialize(writer, value.Address, options);
		writer.WriteNumber("port", value.Port);
		writer.WriteEndObject();
	}
}
}
