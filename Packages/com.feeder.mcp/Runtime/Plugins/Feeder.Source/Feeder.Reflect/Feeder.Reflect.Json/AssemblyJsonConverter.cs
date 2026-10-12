using System;
using System.Linq;
using System.Reflection;
using Feeder.Json;
using Feeder.Json.Serialization;

namespace Feeder.Reflect.Json
{
public class AssemblyJsonConverter : JsonConverter<Assembly>
{
	public override Assembly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return null;
		}
		if (reader.TokenType != JsonTokenType.String)
		{
			throw new JsonException($"Expected string token for Assembly, but got {reader.TokenType}");
		}
		string assemblyName = reader.GetString();
		if (string.IsNullOrWhiteSpace(assemblyName))
		{
			return null;
		}
		return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly a) => a.FullName == assemblyName || a.GetName().Name == assemblyName) ?? throw new JsonException("Assembly '" + assemblyName + "' is not loaded. For security reasons, only already-loaded assemblies can be resolved.");
	}

	public override void Write(Utf8JsonWriter writer, Assembly? value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
		}
		else
		{
			writer.WriteStringValue(value.FullName);
		}
	}
}
}
