using System;
using Feeder.Json;
using Feeder.Json.Nodes;
using Feeder.Reflect.Utils;
using Feeder.Logging;

namespace Feeder.Reflect
{
public static class ExtensionsJson
{
	public static JsonElement? ToJsonElement(this JsonNode? node)
	{
		if (node == null)
		{
			return null;
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(node.ToJsonString());
		return jsonDocument.RootElement.Clone();
	}

	public static JsonElement ToJsonElement(this object data, Reflector? reflector, JsonSerializerOptions? options = null, int depth = 0, ILogger? logger = null)
	{
		if (logger != null && logger.IsEnabled(LogLevel.Trace))
		{
			logger.LogTrace("{padding}Converting object of type '{type}' to JsonElement.", StringUtils.GetPadding(depth), data?.GetType().GetTypeId().ValueOrNull());
		}
		return Feeder.Json.JsonSerializer.SerializeToElement(data, options ?? reflector?.JsonSerializerOptions);
	}

	public static string? ToJson(this object? value, Reflector? reflector, JsonSerializerOptions? options = null, int depth = 0, ILogger? logger = null)
	{
		return value.ToJson(null, reflector, options, depth, logger);
	}

	public static string? ToJson(this object? value, string? defaultValue, Reflector? reflector, JsonSerializerOptions? options = null, int depth = 0, ILogger? logger = null)
	{
		if (value == null)
		{
			return defaultValue;
		}
		if (value is Feeder.Reflect.Utils.JsonSerializer)
		{
			throw new ArgumentException("Cannot serialize JsonSerializer instance.", "value");
		}
		if (logger != null && logger.IsEnabled(LogLevel.Trace))
		{
			logger.LogTrace("{padding}Serializing object of type '{type}' to JSON string.", StringUtils.GetPadding(depth), value.GetType().GetTypeId().ValueOrNull());
		}
		return Feeder.Json.JsonSerializer.Serialize(value, options ?? reflector?.JsonSerializerOptions);
	}
}
}
