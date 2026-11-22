using System;
using System.Globalization;
using Feeder.Json;
using Feeder.Json.Serialization;

namespace Feeder.Reflect.Json
{
public class DateTimeJsonConverter : JsonConverter<object>
{
	public override bool CanConvert(Type typeToConvert)
	{
		return (Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert) == typeof(DateTime);
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
		if (reader.TokenType == JsonTokenType.Number)
		{
			return DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64()).DateTime;
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
			if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result))
			{
				return result;
			}
			throw new JsonException("Unable to convert '" + text + "' to " + typeof(DateTime).GetTypeId() + ".");
		}
		throw new JsonException($"Expected string or number token but got {reader.TokenType} for type '{typeToConvert.GetTypeId()}'");
	}

	public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
		}
		else
		{
			writer.WriteStringValue(((DateTime)value).ToString("o", CultureInfo.InvariantCulture));
		}
	}
}
}
