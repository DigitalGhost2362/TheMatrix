using System;
using Feeder.Json.Nodes;

namespace Feeder.Reflect.Json
{
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = true)]
public sealed class JsonStringOrObjectAttribute : Attribute
{
	public static JsonNode Schema => new JsonObject { ["anyOf"] = new JsonArray
	{
		new JsonObject { ["type"] = "string" },
		new JsonObject
		{
			["type"] = "object",
			["additionalProperties"] = true
		}
	} };
}
}
