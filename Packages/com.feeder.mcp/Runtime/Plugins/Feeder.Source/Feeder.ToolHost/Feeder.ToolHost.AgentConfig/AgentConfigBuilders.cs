using Feeder.Json.Nodes;
using Feeder.ToolHost.Common;
using Feeder.Logging;

namespace Feeder.ToolHost.AgentConfig
{
internal static class AgentConfigBuilders
{
	public static JsonArray StdioArgs(AgentConfiguratorSettings s)
	{
		return new JsonArray
		{
			string.Format("{0}={1}", "port", s.Port),
			string.Format("{0}={1}", "plugin-timeout", s.TimeoutMs),
			string.Format("{0}={1}", "client-transport", Consts.MCP.Server.TransportMethod.stdio),
			string.Format("{0}={1}", "authorization", s.AuthOption)
		};
	}

	public static JsonAiAgentConfig JsonStdio(string name, string configPath, AgentConfiguratorSettings settings, ILogger? logger, string bodyPath = "mcpServers", bool? disabled = null)
	{
		JsonAiAgentConfig jsonAiAgentConfig = new JsonAiAgentConfig(name, configPath, bodyPath, logger).SetProperty("type", JsonValue.Create("stdio"), requiredForConfiguration: true).SetProperty("command", JsonValue.Create(settings.ExecutableFullPath.Replace('\\', '/')), requiredForConfiguration: true, ValueComparisonMode.Path).SetProperty("args", StdioArgs(settings), requiredForConfiguration: true)
			.SetPropertyToRemove("url");
		if (disabled.HasValue)
		{
			jsonAiAgentConfig.SetProperty("disabled", JsonValue.Create(disabled.Value), requiredForConfiguration: true);
		}
		return jsonAiAgentConfig;
	}

	public static JsonAiAgentConfig JsonHttp(string name, string configPath, AgentConfiguratorSettings settings, ILogger? logger, string bodyPath = "mcpServers", string type = "http", bool? disabled = null)
	{
		JsonAiAgentConfig jsonAiAgentConfig = new JsonAiAgentConfig(name, configPath, bodyPath, logger).SetProperty("type", JsonValue.Create(type), requiredForConfiguration: true).SetProperty("url", JsonValue.Create(settings.Host), requiredForConfiguration: true, ValueComparisonMode.Url).SetPropertyToRemove("command")
			.SetPropertyToRemove("args");
		if (disabled.HasValue)
		{
			jsonAiAgentConfig.SetProperty("disabled", JsonValue.Create(disabled.Value), requiredForConfiguration: true);
		}
		return jsonAiAgentConfig;
	}
}
}
