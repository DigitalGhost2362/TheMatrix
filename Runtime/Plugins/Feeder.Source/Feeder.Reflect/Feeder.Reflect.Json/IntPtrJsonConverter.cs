using System;
using Feeder.Json;
using Feeder.Json.Serialization;

namespace Feeder.Reflect.Json
{
public class IntPtrJsonConverter : JsonConverter<object>
{
	public override bool CanConvert(Type typeToConvert)
	{
		return (Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert) == typeof(IntPtr);
	}

	public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			if (Nullable.GetUnderlyingType(typeToConvert) != null)
			{
				return null;
			}
			return IntPtr.Zero;
		}
		long result;
		if (reader.TokenType == JsonTokenType.Number)
		{
			result = reader.GetInt64();
		}
		else
		{
			if (reader.TokenType != JsonTokenType.String)
			{
				throw new JsonException($"Expected number or string token for IntPtr, but got {reader.TokenType}");
			}
			string text = reader.GetString();
			if (!long.TryParse(text, out result))
			{
				throw new JsonException("Unable to parse '" + text + "' as IntPtr.");
			}
		}
		if (IntPtr.Size == 4)
		{
			if (result < int.MinValue || result > int.MaxValue)
			{
				throw new JsonException($"Value {result} is outside the range of IntPtr on this 32-bit platform.");
			}
			return new IntPtr((int)result);
		}
		return new IntPtr(result);
	}

	public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
		}
		else
		{
			writer.WriteNumberValue(((IntPtr)value).ToInt64());
		}
	}
}
}
