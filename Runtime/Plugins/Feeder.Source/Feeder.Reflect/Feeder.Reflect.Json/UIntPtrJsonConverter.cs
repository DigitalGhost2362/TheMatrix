using System;
using Feeder.Json;
using Feeder.Json.Serialization;

namespace Feeder.Reflect.Json
{
public class UIntPtrJsonConverter : JsonConverter<object>
{
	public override bool CanConvert(Type typeToConvert)
	{
		return (Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert) == typeof(UIntPtr);
	}

	public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			if (Nullable.GetUnderlyingType(typeToConvert) != null)
			{
				return null;
			}
			return UIntPtr.Zero;
		}
		ulong result;
		if (reader.TokenType == JsonTokenType.Number)
		{
			result = reader.GetUInt64();
		}
		else
		{
			if (reader.TokenType != JsonTokenType.String)
			{
				throw new JsonException($"Expected number or string token for UIntPtr, but got {reader.TokenType}");
			}
			string text = reader.GetString();
			if (!ulong.TryParse(text, out result))
			{
				throw new JsonException("Unable to parse '" + text + "' as UIntPtr.");
			}
		}
		if (UIntPtr.Size == 4)
		{
			if (result > uint.MaxValue)
			{
				throw new JsonException($"Value {result} is outside the range of UIntPtr on this 32-bit platform.");
			}
			return new UIntPtr((uint)result);
		}
		return new UIntPtr(result);
	}

	public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
		}
		else
		{
			writer.WriteNumberValue(((UIntPtr)value).ToUInt64());
		}
	}
}
}
