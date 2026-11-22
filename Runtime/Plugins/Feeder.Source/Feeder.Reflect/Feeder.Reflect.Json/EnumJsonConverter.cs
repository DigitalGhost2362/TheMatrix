using System;
using Feeder.Json;
using Feeder.Json.Serialization;

namespace Feeder.Reflect.Json
{
public class EnumJsonConverter : JsonConverter<object>
{
	public override bool CanConvert(Type typeToConvert)
	{
		return (Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert).IsEnum;
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
		Type type = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
		if (reader.TokenType == JsonTokenType.Number)
		{
			long @int = reader.GetInt64();
			Type underlyingType = Enum.GetUnderlyingType(type);
			object value = Convert.ChangeType(@int, underlyingType);
			if (IsValid(type, value))
			{
				return Enum.ToObject(type, value);
			}
			throw new JsonException(string.Format("Value '{0}' is not defined for enum {1}. Valid values are: {2}", @int, type.Name, string.Join(", ", Enum.GetNames(type))));
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
			if (!Enum.TryParse(type, text, ignoreCase: true, out object result))
			{
				throw new JsonException("Unable to convert '" + text + "' to enum " + type.Name + ". Valid values are: " + string.Join(", ", Enum.GetNames(type)));
			}
			if (IsValid(type, result))
			{
				return result;
			}
			throw new JsonException("Unable to convert '" + text + "' to enum " + type.Name + ". Valid values are: " + string.Join(", ", Enum.GetNames(type)));
		}
		throw new JsonException($"Expected string or number token but got {reader.TokenType} for enum type '{typeToConvert.GetTypeId()}'");
	}

	// a [Flags] enum also accepts any combination of its defined bits ("North, South" or 5)
	static bool IsValid(Type type, object value)
	{
		if (Enum.IsDefined(type, value))
		{
			return true;
		}
		if (!type.IsDefined(typeof(FlagsAttribute), false))
		{
			return false;
		}
		ulong bits = unchecked((ulong)Convert.ToInt64(value));
		ulong defined = 0;
		foreach (object item in Enum.GetValues(type))
		{
			defined |= unchecked((ulong)Convert.ToInt64(item));
		}
		return (bits & ~defined) == 0;
	}

	public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
		}
		else
		{
			writer.WriteStringValue(value.ToString());
		}
	}
}
}
