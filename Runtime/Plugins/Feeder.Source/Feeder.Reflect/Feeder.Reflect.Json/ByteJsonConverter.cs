using System;
using System.Globalization;
using Feeder.Json;
using Feeder.Json.Serialization;

namespace Feeder.Reflect.Json
{
public class ByteJsonConverter : JsonConverter<object>
{
	public override bool CanConvert(Type typeToConvert)
	{
		return (Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert) == typeof(byte);
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
			return ConvertToByte(reader.GetDouble());
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
			return ParseByte(text);
		}
		throw new JsonException($"Expected string or number token but got {reader.TokenType} for type '{typeToConvert.GetTypeId()}'");
	}

	public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
	{
		writer.WriteNumberValue((byte)value);
	}

	private static byte ParseByte(string stringValue)
	{
		if (byte.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return result;
		}
		throw new JsonException("Unable to convert '" + stringValue + "' to " + typeof(byte).GetTypeId() + ".");
	}

	private static byte ConvertToByte(double value)
	{
		if (value >= 0.0 && value <= 255.0 && value == Math.Floor(value))
		{
			return (byte)value;
		}
		throw new JsonException($"Value {value} is out of range for {typeof(byte).GetTypeId()}.");
	}
}
}
