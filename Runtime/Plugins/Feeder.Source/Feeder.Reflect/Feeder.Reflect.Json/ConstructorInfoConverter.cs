using System;
using System.Collections.Generic;
using System.Reflection;
using Feeder.Json;
using Feeder.Json.Serialization;
using Feeder.Reflect.Utils;

namespace Feeder.Reflect.Json
{
public class ConstructorInfoConverter : JsonConverter<ConstructorInfo>
{
	private static class Json
	{
		public const string DeclaringType = "declaringType";

		public const string Parameters = "parameters";

		public const string Type = "type";
	}

	public override ConstructorInfo? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return null;
		}
		using JsonDocument jsonDocument = JsonDocument.ParseValue(ref reader);
		JsonElement rootElement = jsonDocument.RootElement;
		if (!rootElement.TryGetProperty("declaringType", out var value))
		{
			throw new JsonException("ConstructorInfo JSON must contain 'declaringType'.");
		}
		string text = value.GetString();
		List<Type> list = new List<Type>();
		if (rootElement.TryGetProperty("parameters", out var value2))
		{
			foreach (JsonElement item in value2.EnumerateArray())
			{
				Type type = TypeUtils.GetType(item.GetProperty("type").GetString());
				if (type != null)
				{
					list.Add(type);
				}
			}
		}
		Type? type2 = TypeUtils.GetType(text);
		if (type2 == null)
		{
			throw new JsonException("Could not find type: " + text);
		}
		ConstructorInfo? constructor = type2.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, list.ToArray(), null);
		if (constructor == null)
		{
			throw new JsonException("Could not find constructor on type: " + text + " with specified parameters.");
		}
		return constructor;
	}

	public override void Write(Utf8JsonWriter writer, ConstructorInfo? value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
			return;
		}
		writer.WriteStartObject();
		writer.WriteString("declaringType", value.DeclaringType?.GetTypeId());
		writer.WritePropertyName("parameters");
		writer.WriteStartArray();
		ParameterInfo[] parameters = value.GetParameters();
		foreach (ParameterInfo parameterInfo in parameters)
		{
			writer.WriteStartObject();
			writer.WriteString("type", parameterInfo.ParameterType.GetTypeId());
			writer.WriteEndObject();
		}
		writer.WriteEndArray();
		writer.WriteEndObject();
	}
}
}
