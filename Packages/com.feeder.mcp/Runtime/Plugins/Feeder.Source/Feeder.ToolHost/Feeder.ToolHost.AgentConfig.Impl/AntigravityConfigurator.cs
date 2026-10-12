using System;
using System.Collections.Generic;
using System.IO;
using Feeder.Json.Nodes;
using Feeder.ToolHost.Common;
using Feeder.Logging;

namespace Feeder.ToolHost.AgentConfig.Impl
{
public sealed class AntigravityConfigurator : AiAgentConfigurator
{
	public override string AgentName => "Antigravity";

	public override string AgentId => "antigravity";

	public override string DownloadUrl => "https://antigravity.google/download";

	public override string? SkillsPath => ".agent/skills";

	public override string? IconName => "antigravity-64.png";

	private static string GlobalConfigPath(AgentConfiguratorSettings s)
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "config", "mcp_config.json");
	}

	protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		return new JsonAiAgentConfig(AgentName, GlobalConfigPath(settings), "mcpServers", logger).SetProperty("disabled", JsonValue.Create(value: false), requiredForConfiguration: true).SetProperty("command", JsonValue.Create(settings.ExecutableFullPath.Replace('\\', '/')), requiredForConfiguration: true, ValueComparisonMode.Path)
			.SetProperty("args", AgentConfigBuilders.StdioArgs(settings), requiredForConfiguration: true)
			.SetPropertyToRemove("url")
			.SetPropertyToRemove("serverUrl")
			.SetPropertyToRemove("type");
	}

	protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		return new JsonAiAgentConfig(AgentName, GlobalConfigPath(settings), "mcpServers", logger).SetProperty("disabled", JsonValue.Create(value: false), requiredForConfiguration: true).SetProperty("serverUrl", JsonValue.Create(settings.Host), requiredForConfiguration: true, ValueComparisonMode.Url)
			.SetPropertyToRemove("command")
			.SetPropertyToRemove("args")
			.SetPropertyToRemove("url")
			.SetPropertyToRemove("type");
	}

	protected override IReadOnlyList<ConfigurationSection> BuildSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		return DefaultConfigurationSections(settings, transport, logger);
	}

	protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		return AiAgentConfigurator.TroubleshootingSection("- Ensure MCP configuration file doesn't have syntax errors", "- Restart Antigravity after configuration changes");
	}
}
}
