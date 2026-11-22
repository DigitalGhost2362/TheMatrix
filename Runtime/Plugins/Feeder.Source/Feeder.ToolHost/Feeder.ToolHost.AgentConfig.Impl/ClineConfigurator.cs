using System;
using System.Collections.Generic;
using System.IO;
using Feeder.Json.Nodes;
using Feeder.ToolHost.Common;
using Feeder.Logging;

namespace Feeder.ToolHost.AgentConfig.Impl
{
public sealed class ClineConfigurator : AiAgentConfigurator
{
	public override string AgentName => "Cline";

	public override string AgentId => "cline";

	public override string DownloadUrl => "https://cline.bot/";

	public override string? SkillsPath => ".cline/skills";

	public override string? IconName => "cline-64.png";

	private static string GlobalConfigPath(AgentConfiguratorSettings s)
	{
		return s.OperatingSystem switch
		{
			OperatingSystemKind.Windows => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming", "Code", "User", "globalStorage", "saoudrizwan.claude-dev", "settings", "cline_mcp_settings.json"), 
			OperatingSystemKind.MacOS => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Code", "User", "globalStorage", "saoudrizwan.claude-dev", "settings", "cline_mcp_settings.json"), 
			_ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "Code", "User", "globalStorage", "saoudrizwan.claude-dev", "settings", "cline_mcp_settings.json"), 
		};
	}

	protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		return AgentConfigBuilders.JsonStdio(AgentName, GlobalConfigPath(settings), settings, logger);
	}

	protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		return new JsonAiAgentConfig(AgentName, GlobalConfigPath(settings), "mcpServers", logger).SetProperty("type", JsonValue.Create($"{Consts.MCP.Server.TransportMethod.streamableHttp}"), requiredForConfiguration: true).SetProperty("url", JsonValue.Create(settings.Host), requiredForConfiguration: true, ValueComparisonMode.Url).SetPropertyToRemove("command")
			.SetPropertyToRemove("args");
	}

	protected override IReadOnlyList<ConfigurationSection> BuildSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		return DefaultConfigurationSections(settings, transport, logger);
	}

	protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		return AiAgentConfigurator.TroubleshootingSection("- Ensure the configuration file has no JSON syntax errors.", "- Open Cline settings in VS Code, go to 'MCP Servers' to check server status.", "- The configuration is global and shared across all VS Code projects.", "- Restart VS Code after configuration changes.");
	}
}
}
