using System;
using System.Collections.Generic;
using Feeder.Json;
using Feeder.Json.Nodes;
using Feeder.Reflect.Model;

namespace Feeder.Reflect.Json
{
public class SerializedMemberListConverter : JsonSchemaConverter<SerializedMemberList>, IJsonSchemaConverter
{
	public static JsonNode Schema => new JsonObject
	{
		["type"] = "array",
		["items"] = new JsonObject { ["$ref"] = "#/$defs/" + JsonSchemaConverter<SerializedMember>.StaticId }
	};

	public SerializedMemberListConverter(Reflector reflector)
	{
		if (reflector == null)
		{
			throw new ArgumentNullException("reflector");
		}
	}

	public override JsonNode GetSchema()
	{
		return Schema;
	}

	public override JsonNode GetSchemaRef()
	{
		return new JsonObject { ["$ref"] = "#/$defs/" + Id };
	}

	public override IEnumerable<Type> GetDefinedTypes()
	{
		yield return typeof(SerializedMemberList);
		yield return typeof(SerializedMember);
	}

	public override SerializedMemberList? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return null;
		}
		if (reader.TokenType != JsonTokenType.StartArray)
		{
			throw new JsonException($"Expected start of array, but got {reader.TokenType}");
		}
		SerializedMemberList serializedMemberList = new SerializedMemberList();
		while (reader.Read())
		{
			if (reader.TokenType == JsonTokenType.EndArray)
			{
				return serializedMemberList;
			}
			SerializedMember serializedMember = JsonSerializer.Deserialize<SerializedMember>(ref reader, options);
			if (serializedMember != null)
			{
				serializedMemberList.Add(serializedMember);
			}
		}
		throw new JsonException("Unexpected end of array.");
	}

	public override void Write(Utf8JsonWriter writer, SerializedMemberList? value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
			return;
		}
		writer.WriteStartArray();
		foreach (SerializedMember item in value)
		{
			JsonSerializer.Serialize(writer, item, options);
		}
		writer.WriteEndArray();
	}
}
}
