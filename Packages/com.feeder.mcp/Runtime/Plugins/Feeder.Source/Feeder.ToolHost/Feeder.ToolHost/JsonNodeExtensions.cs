using System.Collections.Generic;
using System.Linq;
using Feeder.Json.Nodes;
using Feeder.ToolHost.Common.Model;

namespace Feeder.ToolHost
{
public static class JsonNodeExtensions
{
	public static List<ResponsePromptArgument>? ToResponsePromptArguments(this JsonNode? node)
	{
		if (node == null)
		{
			return null;
		}
		if (!(node is JsonObject jsonObject))
		{
			return null;
		}
		if (!jsonObject.TryGetPropertyValue("properties", out JsonNode jsonNode))
		{
			return null;
		}
		if (!(jsonNode is JsonObject source))
		{
			return null;
		}
		return (from arg in source.Select<KeyValuePair<string, JsonNode>, ResponsePromptArgument>(delegate(KeyValuePair<string, JsonNode> input)
			{
				if (!(input.Value is JsonObject jsonObject2))
				{
					return (ResponsePromptArgument)null;
				}
				jsonObject2.TryGetPropertyValue("description", out JsonNode jsonNode2);
				jsonObject2.TryGetPropertyValue("required", out JsonNode jsonNode3);
				HashSet<string> hashSet = ((jsonNode3 is JsonArray) ? (from v in jsonNode3.AsArray()
					select v?.GetValue<string>() into v
					where !string.IsNullOrEmpty(v)
					select (v)).ToHashSet() : null);
				return new ResponsePromptArgument
				{
					Name = input.Key,
					Description = jsonNode2?.GetValue<string>(),
					Required = (hashSet?.Contains(input.Key) ?? false)
				};
			})
			where arg != null
			select (arg)).ToList();
	}
}
}
