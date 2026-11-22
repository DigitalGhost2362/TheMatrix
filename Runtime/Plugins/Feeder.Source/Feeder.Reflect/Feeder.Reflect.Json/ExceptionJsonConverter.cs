using System;
using Feeder.Json;
using Feeder.Json.Serialization;
using Feeder.Reflect.Utils;

namespace Feeder.Reflect.Json
{
public class ExceptionJsonConverter : JsonConverter<Exception>
{
	private static class Json
	{
		public const string Message = "message";

		public const string Type = "type";

		public const string StackTrace = "stackTrace";

		public const string InnerException = "innerException";
	}

	public override Exception? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return null;
		}
		using JsonDocument jsonDocument = JsonDocument.ParseValue(ref reader);
		JsonElement rootElement = jsonDocument.RootElement;
		string text = (rootElement.TryGetProperty("message", out var value) ? value.GetString() : string.Empty);
		string? typeName = (rootElement.TryGetProperty("type", out var value2) ? value2.GetString() : typeof(Exception).FullName);
		Exception ex = null;
		if (rootElement.TryGetProperty("innerException", out var value3) && value3.ValueKind != JsonValueKind.Null)
		{
			ex = Feeder.Json.JsonSerializer.Deserialize<Exception>(value3.GetRawText(), options);
		}
		Type type = TypeUtils.GetType(typeName);
		if (type != null && typeof(Exception).IsAssignableFrom(type))
		{
			try
			{
				return ((Exception)Activator.CreateInstance(type, text, ex)) ?? new Exception(text, ex);
			}
			catch
			{
				return new Exception(text, ex);
			}
		}
		return new Exception(text, ex);
	}

	public override void Write(Utf8JsonWriter writer, Exception? value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
			return;
		}
		writer.WriteStartObject();
		writer.WriteString("type", value.GetType().GetTypeId());
		writer.WriteString("message", value.Message);
		writer.WriteString("stackTrace", value.StackTrace);
		if (value.InnerException != null)
		{
			writer.WritePropertyName("innerException");
			Feeder.Json.JsonSerializer.Serialize(writer, value.InnerException, options);
		}
		writer.WriteEndObject();
	}
}
}
