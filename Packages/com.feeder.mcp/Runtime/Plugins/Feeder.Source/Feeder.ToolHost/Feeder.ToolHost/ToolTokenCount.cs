using System;
using Feeder.Json.Nodes;

namespace Feeder.ToolHost
{
internal static class ToolTokenCount
{
	public static int Calculate(string? name, string? title, string? description, JsonNode? inputSchema, JsonNode? outputSchema)
	{
		JsonObject jsonObject = new JsonObject();
		if (!string.IsNullOrEmpty(name))
		{
			jsonObject["name"] = name;
		}
		if (!string.IsNullOrEmpty(title))
		{
			jsonObject["title"] = title;
		}
		if (!string.IsNullOrEmpty(description))
		{
			jsonObject["description"] = description;
		}
		if (inputSchema != null)
		{
			jsonObject["inputSchema"] = JsonNode.Parse(inputSchema.ToJsonString());
		}
		if (outputSchema != null)
		{
			jsonObject["outputSchema"] = JsonNode.Parse(outputSchema.ToJsonString());
		}
		return (int)Math.Ceiling((double)jsonObject.ToJsonString().Length / 4.0);
	}
}
}
