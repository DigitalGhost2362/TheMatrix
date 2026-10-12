using System;
using Feeder.Json;
using Feeder.Json.Serialization;

namespace Feeder.Reflect.Json
{
public class BoolJsonConverter : JsonConverter<object>
{
	public override bool CanConvert(Type typeToConvert)
	{
		return (Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert) == typeof(bool);
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
		if (reader.TokenType == JsonTokenType.True)
		{
			return true;
		}
		if (reader.TokenType == JsonTokenType.False)
		{
			return false;
		}
		if (reader.TokenType == JsonTokenType.Number)
		{
			return reader.GetInt32() != 0;
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
				throw new JsonException("Cannot convert null string to non-nullable type '" + typeToConvert.GetTypeId() + "'.\nInput value: null");
			}
			if (bool.TryParse(text, out var result))
			{
				return result;
			}
			throw new JsonException("Unable to convert '" + text + "' to '" + typeof(bool).GetTypeId() + "'.\nInput value: " + text);
		}
		throw new JsonException($"Expected string, boolean, or number token but got {reader.TokenType} for type '{typeToConvert.GetTypeId()}'.");
	}

	public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
	{
		writer.WriteBooleanValue((bool)value);
	}
}
}
