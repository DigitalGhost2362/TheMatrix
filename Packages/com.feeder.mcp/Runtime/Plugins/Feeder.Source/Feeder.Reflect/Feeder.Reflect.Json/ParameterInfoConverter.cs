using System;
using System.Linq;
using System.Reflection;
using Feeder.Json;
using Feeder.Json.Serialization;

namespace Feeder.Reflect.Json
{
public class ParameterInfoConverter : JsonConverter<ParameterInfo>
{
	private static class Json
	{
		public const string Name = "name";

		public const string Member = "member";

		public const string MemberType = "memberType";
	}

	public override ParameterInfo? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return null;
		}
		using JsonDocument jsonDocument = JsonDocument.ParseValue(ref reader);
		JsonElement rootElement = jsonDocument.RootElement;
		if (!rootElement.TryGetProperty("name", out var value) || !rootElement.TryGetProperty("member", out var value2))
		{
			throw new JsonException("ParameterInfo JSON must contain 'name' and 'member'.");
		}
		string paramName = value.GetString();
		string rawText = value2.GetRawText();
		MethodBase methodBase = null;
		if (rootElement.TryGetProperty("memberType", out var value3))
		{
			string text = value3.GetString();
			if (text == "MethodInfo")
			{
				methodBase = JsonSerializer.Deserialize<MethodInfo>(rawText, options);
			}
			else if (text == "ConstructorInfo")
			{
				methodBase = JsonSerializer.Deserialize<ConstructorInfo>(rawText, options);
			}
		}
		if (methodBase == null)
		{
			throw new JsonException("Could not resolve member for ParameterInfo.");
		}
		return methodBase.GetParameters().FirstOrDefault((ParameterInfo p) => p.Name == paramName) ?? throw new JsonException("Could not find parameter: " + paramName + " on member: " + methodBase.Name);
	}

	public override void Write(Utf8JsonWriter writer, ParameterInfo? value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
			return;
		}
		writer.WriteStartObject();
		writer.WriteString("name", value.Name);
		writer.WriteString("memberType", value.Member.GetType().Name);
		writer.WritePropertyName("member");
		if (value.Member is MethodInfo value2)
		{
			JsonSerializer.Serialize(writer, value2, options);
		}
		else if (value.Member is ConstructorInfo value3)
		{
			JsonSerializer.Serialize(writer, value3, options);
		}
		else
		{
			writer.WriteNullValue();
		}
		writer.WriteEndObject();
	}
}
}
