using System;
using System.Reflection;
using Feeder.Json;
using Feeder.Json.Serialization;
using Feeder.Reflect.Utils;

namespace Feeder.Reflect.Json
{
public class PropertyInfoConverter : JsonConverter<PropertyInfo>
{
	private static class Json
	{
		public const string Name = "name";

		public const string DeclaringType = "declaringType";
	}

	public override PropertyInfo? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return null;
		}
		using JsonDocument jsonDocument = JsonDocument.ParseValue(ref reader);
		JsonElement rootElement = jsonDocument.RootElement;
		if (!rootElement.TryGetProperty("declaringType", out var value) || !rootElement.TryGetProperty("name", out var value2))
		{
			throw new JsonException("PropertyInfo JSON must contain 'declaringType' and 'name'.");
		}
		string text = value.GetString();
		string text2 = value2.GetString();
		Type? type = TypeUtils.GetType(text);
		if (type == null)
		{
			throw new JsonException("Could not find type: " + text);
		}
		PropertyInfo? property = type.GetProperty(text2, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		if (property == null)
		{
			throw new JsonException("Could not find property: " + text2 + " on type: " + text);
		}
		return property;
	}

	public override void Write(Utf8JsonWriter writer, PropertyInfo? value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
			return;
		}
		writer.WriteStartObject();
		writer.WriteString("name", value.Name);
		writer.WriteString("declaringType", value.DeclaringType?.GetTypeId());
		writer.WriteEndObject();
	}
}
}
