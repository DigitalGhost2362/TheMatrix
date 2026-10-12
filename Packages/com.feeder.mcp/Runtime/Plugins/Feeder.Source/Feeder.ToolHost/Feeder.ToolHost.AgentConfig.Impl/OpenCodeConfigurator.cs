using System.Collections.Generic;
using System.IO;
using Feeder.Json.Nodes;
using Feeder.ToolHost.Common;
using Feeder.Logging;

namespace Feeder.ToolHost.AgentConfig.Impl
{
public sealed class OpenCodeConfigurator : AiAgentConfigurator
{
	public override string AgentName => "Open Code";

	public override string AgentId => "open-code";

	public override string DownloadUrl => "https://opencode.ai/download";

	public override string? SkillsPath => ".opencode/skills";

	public override string? IconName => "open-code-64.png";

	private static string LocalConfigPath(AgentConfiguratorSettings s)
	{
		return Path.Combine(s.ProjectRootPath, "opencode.json");
	}

	protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		JsonArray jsonArray = new JsonArray
		{
			settings.ExecutableFullPath.Replace('\\', '/'),
			string.Format("{0}={1}", "port", settings.Port),
			string.Format("{0}={1}", "plugin-timeout", settings.TimeoutMs),
			string.Format("{0}={1}", "client-transport", Consts.MCP.Server.TransportMethod.stdio),
			string.Format("{0}={1}", "authorization", settings.AuthOption)
		};
		if (settings.IsStdioAuthRequired && !string.IsNullOrEmpty(settings.Token))
		{
			jsonArray.Add("token=" + settings.Token);
		}
		return new JsonAiAgentConfig(AgentName, LocalConfigPath(settings), "mcp", logger).SetProperty("type", JsonValue.Create("local"), requiredForConfiguration: true).SetProperty("enabled", JsonValue.Create(value: true), requiredForConfiguration: true).SetProperty("command", jsonArray, requiredForConfiguration: true)
			.SetPropertyToRemove("url")
			.SetPropertyToRemove("args");
	}

	protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		return new JsonAiAgentConfig(AgentName, LocalConfigPath(settings), "mcp", logger).SetProperty("type", JsonValue.Create("remote"), requiredForConfiguration: true).SetProperty("enabled", JsonValue.Create(value: true), requiredForConfiguration: true).SetProperty("url", JsonValue.Create(settings.Host), requiredForConfiguration: true, ValueComparisonMode.Url)
			.SetPropertyToRemove("command")
			.SetPropertyToRemove("args");
	}

	protected override void ApplyStdioAuthorization(AiAgentConfig config, AgentConfiguratorSettings settings)
	{
	}

	protected override IReadOnlyList<ConfigurationSection> BuildSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		return DefaultConfigurationSections(settings, transport, logger);
	}

	protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		return AiAgentConfigurator.TroubleshootingSection("- Ensure Open Code CLI is installed and accessible from terminal", "- Ensure Open Code CLI is launched from the project root folder (the folder must contain the Assets folder inside)", "- Restart Open Code after configuration changes");
	}
}
}
