using System.Collections.Generic;
using System.IO;
using Feeder.Json.Nodes;
using Feeder.ToolHost.Common;
using Feeder.Logging;

namespace Feeder.ToolHost.AgentConfig.Impl
{
public sealed class GitHubCopilotCliConfigurator : AiAgentConfigurator
{
	public override string AgentName => "GitHub Copilot CLI";

	public override string AgentId => "github-copilot-cli";

	public override string DownloadUrl => "https://github.com/features/copilot/cli";

	public override string? SkillsPath => ".claude/skills";

	public override string? IconName => "github-copilot-64.png";

	private static string LocalConfigPath(AgentConfiguratorSettings s)
	{
		return Path.Combine(s.ProjectRootPath, ".mcp.json");
	}

	protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		return new JsonAiAgentConfig(AgentName, LocalConfigPath(settings), "mcpServers", logger).SetProperty("command", JsonValue.Create(settings.ExecutableFullPath.Replace('\\', '/')), requiredForConfiguration: true, ValueComparisonMode.Path).SetProperty("args", AgentConfigBuilders.StdioArgs(settings), requiredForConfiguration: true).SetProperty("tools", new JsonArray { "*" })
			.SetPropertyToRemove("url")
			.SetPropertyToRemove("type");
	}

	protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		return AgentConfigBuilders.JsonHttp(AgentName, LocalConfigPath(settings), settings, logger).SetProperty("tools", new JsonArray { "*" });
	}

	protected override IReadOnlyList<ConfigurationSection> BuildSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		return DefaultConfigurationSections(settings, transport, logger);
	}

	protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		return AiAgentConfigurator.TroubleshootingSection("- Ensure Copilot CLI is launched from the project root (the folder containing '.mcp.json')", "- Requires GitHub Copilot CLI v1.0.12+ which discovers '.mcp.json' at project level", "- Ensure MCP configuration file doesn't have syntax errors", "- Restart Copilot CLI after configuration changes");
	}
}
}
