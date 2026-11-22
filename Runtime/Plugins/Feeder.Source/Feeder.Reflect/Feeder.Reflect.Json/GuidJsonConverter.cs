using System;
using Feeder.Json;
using Feeder.Json.Serialization;

namespace Feeder.Reflect.Json
{
public class GuidJsonConverter : JsonConverter<object>
{
	public override bool CanConvert(Type typeToConvert)
	{
		return (Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert) == typeof(Guid);
	}

	public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			if (Nullable.GetUnderlyingType(typeToConvert) != null)
			{
				return null;
			}
			throw new JsonException("Cannot convert null to non-nullable type '" + typeToConvert.GetTypeId() + "'.");
		}
		if (reader.TokenType == JsonTokenType.String)
		{
			string text = reader.GetString();
			if (text == null)
			{
				if (Nullable.GetUnderlyingType(typeToConvert) != null)
				{
					return null;
				}
				throw new JsonException("Cannot convert null string to non-nullable type '" + typeToConvert.GetTypeId() + "'.");
			}
			if (Guid.TryParse(text, out var result))
			{
				return result;
			}
			throw new JsonException("Unable to convert '" + text + "' to " + typeof(Guid).GetTypeId() + ".");
		}
		throw new JsonException($"Expected string token but got {reader.TokenType} for type '{typeToConvert.GetTypeId()}'");
	}

	public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
		}
		else
		{
			writer.WriteStringValue(((Guid)value/*cast due to constrained. prefix*/).ToString());
		}
	}
}
}
