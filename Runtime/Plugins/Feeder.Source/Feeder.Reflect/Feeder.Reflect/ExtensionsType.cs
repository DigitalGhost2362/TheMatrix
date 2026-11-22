using System;
using Feeder.Json.Nodes;
using Feeder.Reflect.Utils;

namespace Feeder.Reflect
{
public static class ExtensionsType
{
	public static JsonNode? GetSchema(this Type type, Reflector reflector)
	{
		return reflector.GetSchema(type);
	}

	public static JsonNode? GetSchemaRef(this Type type, Reflector reflector)
	{
		return reflector.GetSchemaRef(type);
	}

	public static string GetTypeShortName(this Type? type)
	{
		return TypeUtils.GetTypeShortName(type);
	}

	public static string Sanitize(this Type? type)
	{
		return TypeUtils.Sanitize(type);
	}

	public static string GetTypeId(this Type type)
	{
		return TypeUtils.GetTypeId(type);
	}

	public static string GetSchemaTypeId(this Type type)
	{
		return TypeUtils.GetSchemaTypeId(type);
	}

	public static bool IsMatch(this Type? type, string? typeName)
	{
		return TypeUtils.IsNameMatch(type, typeName);
	}
}
}
