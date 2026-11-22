using System;
using System.Numerics;
using Feeder.Json;
using Feeder.Json.Nodes;

namespace Feeder.Reflect.Json
{
public class ComplexJsonConverter : JsonSchemaConverter<Complex>, IJsonSchemaConverter
{
	private const string RealProperty = "real";

	private const string ImaginaryProperty = "imaginary";

	public static JsonNode Schema => new JsonObject
	{
		["type"] = "object",
		["properties"] = new JsonObject
		{
			["real"] = new JsonObject { ["type"] = "number" },
			["imaginary"] = new JsonObject { ["type"] = "number" }
		},
		["required"] = new JsonArray { "real", "imaginary" }
	};

	public static JsonNode SchemaRef => new JsonObject { ["$ref"] = "#/$defs/" + JsonSchemaConverter<Complex>.StaticId };

	public override JsonNode GetSchemaRef()
	{
		return SchemaRef;
	}

	public override JsonNode GetSchema()
	{
		return Schema;
	}

	public override Complex Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			throw new JsonException("Cannot convert null to Complex.");
		}
		if (reader.TokenType != JsonTokenType.StartObject)
		{
			throw new JsonException("Expected StartObject for Complex.");
		}
		double real = 0.0;
		double imaginary = 0.0;
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
				else if (string.Equals(text, "real", StringComparison.OrdinalIgnoreCase))
				{
					real = reader.GetDouble();
					flag = true;
				}
				else if (string.Equals(text, "imaginary", StringComparison.OrdinalIgnoreCase))
				{
					imaginary = reader.GetDouble();
					flag2 = true;
				}
				else
				{
					reader.Skip();
				}
			}
		}
		if (!flag || !flag2)
		{
			throw new JsonException("Complex number requires both 'real' and 'imaginary' properties.");
		}
		return new Complex(real, imaginary);
	}

	public override void Write(Utf8JsonWriter writer, Complex value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		writer.WriteNumber("real", value.Real);
		writer.WriteNumber("imaginary", value.Imaginary);
		writer.WriteEndObject();
	}
}
}
