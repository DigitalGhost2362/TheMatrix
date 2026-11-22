using System;
using System.Globalization;
using Feeder.Json;
using Feeder.Json.Serialization;

namespace Feeder.Reflect.Json
{
public class UInt16JsonConverter : JsonConverter<object>
{
	public override bool CanConvert(Type typeToConvert)
	{
		return (Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert) == typeof(ushort);
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
			return ConvertToUInt16(reader.GetDouble());
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
			return ParseUInt16(text);
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
			writer.WriteNumberValue((ushort)value);
		}
	}

	private static ushort ParseUInt16(string stringValue)
	{
		if (ushort.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return result;
		}
		throw new JsonException("Unable to convert '" + stringValue + "' to " + typeof(ushort).GetTypeId() + ".");
	}

	private static ushort ConvertToUInt16(double value)
	{
		if (value >= 0.0 && value <= 65535.0 && value == Math.Floor(value))
		{
			return (ushort)value;
		}
		throw new JsonException($"Value {value} is out of range for {typeof(ushort).GetTypeId()}.");
	}
}
}
