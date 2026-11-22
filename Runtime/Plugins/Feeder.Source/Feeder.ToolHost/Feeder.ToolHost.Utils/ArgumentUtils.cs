using System.Linq;
using System.Reflection;
using Feeder.Json.Nodes;

namespace Feeder.ToolHost.Utils
{
public static class ArgumentUtils
{
	public static void RemoveRequestIDParameters(JsonNode schema, MethodInfo methodInfo)
	{
		JsonObject jsonObject = schema["properties"]?.AsObject();
		JsonArray jsonArray = schema["required"]?.AsArray();
		if (jsonObject == null && jsonArray == null)
		{
			return;
		}
		ParameterInfo[] parameters = methodInfo.GetParameters();
		foreach (ParameterInfo parameterInfo in parameters)
		{
			if (!parameterInfo.IsDefined(typeof(RequestIDAttribute), inherit: false))
			{
				continue;
			}
			string name = parameterInfo.Name;
			if (string.IsNullOrEmpty(name))
			{
				continue;
			}
			jsonObject?.Remove(name);
			if (jsonArray != null)
			{
				JsonNode jsonNode = jsonArray.FirstOrDefault((JsonNode x) => x?.GetValue<string>() == name);
				if (jsonNode != null)
				{
					jsonArray.Remove(jsonNode);
				}
			}
		}
	}
}
}
