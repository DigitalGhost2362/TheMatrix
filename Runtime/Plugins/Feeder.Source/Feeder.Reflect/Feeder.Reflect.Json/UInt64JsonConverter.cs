using System;
using System.Globalization;
using Feeder.Json;
using Feeder.Json.Serialization;

namespace Feeder.Reflect.Json
{
public class UInt64JsonConverter : JsonConverter<object>
{
	public override bool CanConvert(Type typeToConvert)
	{
		return (Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert) == typeof(ulong);
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
			return ConvertToUInt64(reader.GetDouble());
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
			return ParseUInt64(text);
		}
		throw new JsonException($"Expected string or number token but got {reader.TokenType} for type '{typeToConvert.GetTypeId()}'");
	}

	public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
		}
		else
		{
			writer.WriteNumberValue((ulong)value);
		}
	}

	private static ulong ParseUInt64(string stringValue)
	{
		if (ulong.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return result;
		}
		throw new JsonException("Unable to convert '" + stringValue + "' to " + typeof(ulong).GetTypeId() + ".");
	}

	private static ulong ConvertToUInt64(double value)
	{
		if (value >= 0.0 && value <= 1.8446744073709552E+19 && value == Math.Floor(value))
		{
			return (ulong)value;
		}
		throw new JsonException($"Value {value} is out of range for {typeof(ulong).GetTypeId()}.");
	}
}
}
