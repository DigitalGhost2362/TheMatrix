using System;
using Feeder.Json;
using Feeder.Json.Nodes;

namespace Feeder.Reflect.Json
{
public abstract class JsonNodeJsonConverter<T> : JsonSchemaConverter<T>, IJsonSchemaConverter where T : JsonNode
{
	protected abstract T? CreateJsonNode(JsonElement element);

	public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			if (Nullable.GetUnderlyingType(typeToConvert) != null)
			{
				return null;
			}
			throw new JsonException("Cannot convert null to non-nullable type '" + typeToConvert.GetTypeId() + "'.");
		}
		JsonTokenType tokenType = reader.TokenType;
		if ((tokenType == JsonTokenType.StartObject || tokenType == JsonTokenType.StartArray) ? true : false)
		{
			using (JsonDocument jsonDocument = JsonDocument.ParseValue(ref reader))
			{
				JsonElement element = jsonDocument.RootElement.Clone();
				return CreateJsonNode(element);
			}
		}
		throw new JsonException($"Expected Null, StartObject or StartArray token but got {reader.TokenType} for type '{typeToConvert.GetTypeId()}'");
	}

	public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
		}
		else
		{
			value.WriteTo(writer, options);
		}
	}
}
}
