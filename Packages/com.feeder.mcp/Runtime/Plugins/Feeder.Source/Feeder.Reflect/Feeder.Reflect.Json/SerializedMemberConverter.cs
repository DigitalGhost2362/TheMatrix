using System;
using System.Collections.Generic;
using System.Linq;
using Feeder.Json;
using Feeder.Json.Nodes;
using Feeder.Reflect.Model;
using Feeder.Reflect.Utils;

namespace Feeder.Reflect.Json
{
public class SerializedMemberConverter : JsonSchemaConverter<SerializedMember>, IJsonSchemaConverter
{
	private readonly Reflector _reflector;

	public static JsonNode Schema => new JsonObject
	{
		["type"] = "object",
		["properties"] = new JsonObject
		{
			["typeName"] = new JsonObject
			{
				["type"] = "string",
				["description"] = TypeUtils.GetDescription(typeof(SerializedMember).GetMember("typeName").First())
			},
			["name"] = new JsonObject
			{
				["type"] = "string",
				["description"] = TypeUtils.GetDescription(typeof(SerializedMember).GetMember("name").First())
			},
			["value"] = new JsonObject { ["description"] = TypeUtils.GetDescription(typeof(SerializedMember).GetMember("valueJsonElement").First()) },
			["fields"] = new JsonObject
			{
				["type"] = "array",
				["items"] = new JsonObject
				{
					["$ref"] = "#/$defs/" + JsonSchemaConverter<SerializedMember>.StaticId,
					["description"] = "Nested field value."
				},
				["description"] = TypeUtils.GetDescription(typeof(SerializedMember).GetMember("fields").First())
			},
			["props"] = new JsonObject
			{
				["type"] = "array",
				["items"] = new JsonObject
				{
					["$ref"] = "#/$defs/" + JsonSchemaConverter<SerializedMember>.StaticId,
					["description"] = "Nested property value."
				},
				["description"] = TypeUtils.GetDescription(typeof(SerializedMember).GetMember("props").First())
			}
		},
		["required"] = new JsonArray { "typeName" },
		["additionalProperties"] = false
	};

	public static JsonNode SchemaRef => new JsonObject { ["$ref"] = "#/$defs/" + JsonSchemaConverter<SerializedMember>.StaticId };

	public SerializedMemberConverter(Reflector reflector)
	{
		_reflector = reflector ?? throw new ArgumentNullException("reflector");
	}

	public override JsonNode GetSchemaRef()
	{
		return SchemaRef;
	}

	public override JsonNode GetSchema()
	{
		return Schema;
	}

	public override IEnumerable<Type> GetDefinedTypes()
	{
		yield return typeof(SerializedMemberList);
		yield return typeof(SerializedMember);
	}

	public override SerializedMember? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return null;
		}
		if (reader.TokenType != JsonTokenType.StartObject)
		{
			throw new JsonException($"Expected start of object, but got {reader.TokenType}");
		}
		SerializedMember serializedMember = new SerializedMember();
		while (reader.Read())
		{
			if (reader.TokenType == JsonTokenType.EndObject)
			{
				return serializedMember;
			}
			if (reader.TokenType != JsonTokenType.PropertyName)
			{
				continue;
			}
			string text = reader.GetString();
			reader.Read();
			switch (text)
			{
			case "name":
				serializedMember.name = reader.GetString();
				break;
			case "typeName":
				serializedMember.typeName = reader.GetString() ?? "[FAILED TO READ]";
				break;
			case "value":
				if (!JsonElement.TryParseValue(ref reader, out serializedMember.valueJsonElement))
				{
					throw new JsonException("Failed to parse value for property 'value'.");
				}
				break;
			case "fields":
				serializedMember.fields = _reflector.JsonSerializer.Deserialize<SerializedMemberList>(ref reader, options);
				break;
			case "props":
				serializedMember.props = _reflector.JsonSerializer.Deserialize<SerializedMemberList>(ref reader, options);
				break;
			default:
				throw new JsonException("Unexpected property name: '" + text + "'. Did you want to use 'name', 'typeName', 'value', 'fields' or 'props'?");
			}
		}
		throw new JsonException("Unexpected end of JSON while reading SerializedMember.");
	}

	public override void Write(Utf8JsonWriter writer, SerializedMember? value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
			return;
		}
		writer.WriteStartObject();
		if (value.name != null)
		{
			writer.WriteString("name", value.name);
		}
		writer.WriteString("typeName", value.typeName);
		if (value.valueJsonElement.HasValue)
		{
			writer.WritePropertyName("value");
			value.valueJsonElement.Value.WriteTo(writer);
		}
		if (value.fields != null && value.fields.Count > 0)
		{
			writer.WritePropertyName("fields");
			Feeder.Json.JsonSerializer.Serialize(writer, value.fields, options);
		}
		if (value.props != null && value.props.Count > 0)
		{
			writer.WritePropertyName("props");
			Feeder.Json.JsonSerializer.Serialize(writer, value.props, options);
		}
		writer.WriteEndObject();
	}
}
}
