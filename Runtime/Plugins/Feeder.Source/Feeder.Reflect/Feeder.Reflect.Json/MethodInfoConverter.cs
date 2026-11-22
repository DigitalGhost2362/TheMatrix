using System;
using System.Collections.Generic;
using System.Reflection;
using Feeder.Json;
using Feeder.Json.Serialization;
using Feeder.Reflect.Utils;

namespace Feeder.Reflect.Json
{
public class MethodInfoConverter : JsonConverter<MethodInfo>
{
	private static class Json
	{
		public const string Name = "name";

		public const string DeclaringType = "declaringType";

		public const string Parameters = "parameters";

		public const string Type = "type";
	}

	public override MethodInfo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using JsonDocument jsonDocument = JsonDocument.ParseValue(ref reader);
		JsonElement rootElement = jsonDocument.RootElement;
		string text = rootElement.GetProperty("declaringType").GetString();
		string text2 = rootElement.GetProperty("name").GetString();
		List<Type> list = new List<Type>();
		if (rootElement.TryGetProperty("parameters", out var value))
		{
			foreach (JsonElement item in value.EnumerateArray())
			{
				Type type = TypeUtils.GetType(item.GetProperty("type").GetString());
				if (type != null)
				{
					list.Add(type);
				}
			}
		}
		Type type2 = TypeUtils.GetType(text);
		if (type2 == null)
		{
			throw new JsonException("Could not find type: " + text);
		}
		MethodInfo? obj = (string.IsNullOrEmpty(text2) ? null : type2.GetMethod(text2, list.ToArray()));
		if (obj == null)
		{
			throw new JsonException("Could not find method: " + text2 + " on type: " + text);
		}
		return obj;
	}

	public override void Write(Utf8JsonWriter writer, MethodInfo? value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
			return;
		}
		writer.WriteStartObject();
		writer.WriteString("name", value.Name);
		writer.WriteString("declaringType", value.DeclaringType?.GetTypeId());
		writer.WritePropertyName("parameters");
		writer.WriteStartArray();
		ParameterInfo[] parameters = value.GetParameters();
		foreach (ParameterInfo parameterInfo in parameters)
		{
			writer.WriteStartObject();
			writer.WriteString("name", parameterInfo.Name);
			writer.WriteString("type", parameterInfo.ParameterType.GetTypeId());
			writer.WriteEndObject();
		}
		writer.WriteEndArray();
		writer.WriteEndObject();
	}
}
}
