using System;
using System.Globalization;
using Feeder.Json;
using Feeder.Json.Serialization;

namespace Feeder.Reflect.Json
{
public class SingleJsonConverter : JsonConverter<object>
{
	public override bool CanConvert(Type typeToConvert)
	{
		return (Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert) == typeof(float);
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
			return ConvertToFloat(reader.GetDouble());
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
			return ParseSingle(text);
		}
		throw new JsonException($"Expected string or number token but got {reader.TokenType} for type '{typeToConvert.GetTypeId()}'");
	}

	public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
	{
		writer.WriteNumberValue((float)value);
	}

	private static float ParseSingle(string stringValue)
	{
		if (float.TryParse(stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return result;
		}
		throw new JsonException("Unable to convert '" + stringValue + "' to " + typeof(float).GetTypeId() + ".");
	}

	private static float ConvertToFloat(double value)
	{
		if (value >= -3.4028234663852886E+38 && value <= 3.4028234663852886E+38)
		{
			return (float)value;
		}
		throw new JsonException($"Value {value} is out of range for {typeof(float).GetTypeId()}.");
	}
}
}
