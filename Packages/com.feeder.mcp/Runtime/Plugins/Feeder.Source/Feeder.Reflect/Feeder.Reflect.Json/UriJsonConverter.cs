using System;
using Feeder.Json;
using Feeder.Json.Serialization;

namespace Feeder.Reflect.Json
{
public class UriJsonConverter : JsonConverter<Uri>
{
	public override Uri? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return null;
		}
		if (reader.TokenType != JsonTokenType.String)
		{
			throw new JsonException($"Expected string token for Uri, but got {reader.TokenType}");
		}
		string text = reader.GetString();
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		if (Uri.TryCreate(text, UriKind.RelativeOrAbsolute, out Uri result))
		{
			return result;
		}
		throw new JsonException("Unable to parse '" + text + "' as a Uri.");
	}

	public override void Write(Utf8JsonWriter writer, Uri? value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
		}
		else
		{
			writer.WriteStringValue(value.OriginalString);
		}
	}
}
}
