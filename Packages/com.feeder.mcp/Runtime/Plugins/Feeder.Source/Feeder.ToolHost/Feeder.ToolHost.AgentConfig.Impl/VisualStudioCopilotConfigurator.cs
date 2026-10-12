using System.Collections.Generic;
using System.IO;
using Feeder.ToolHost.Common;
using Feeder.Logging;

namespace Feeder.ToolHost.AgentConfig.Impl
{
public sealed class VisualStudioCopilotConfigurator : AiAgentConfigurator
{
	public override string AgentName => "Visual Studio (Copilot)";

	public override string AgentId => "vs-copilot";

	public override string DownloadUrl => "https://visualstudio.microsoft.com/downloads/";

	public override string? SkillsPath => ".github/skills";

	public override string? IconName => "visual-studio-64.png";

	private static string LocalConfigPath(AgentConfiguratorSettings s)
	{
		return Path.Combine(s.ProjectRootPath, ".vs", "mcp.json");
	}

	protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		return AgentConfigBuilders.JsonStdio(AgentName, LocalConfigPath(settings), settings, logger, "servers");
	}

	protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		return AgentConfigBuilders.JsonHttp(AgentName, LocalConfigPath(settings), settings, logger, "servers");
	}

	protected override IReadOnlyList<ConfigurationSection> BuildSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		return DefaultConfigurationSections(settings, transport, logger);
	}

	protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		return AiAgentConfigurator.TroubleshootingSection("- '.vs/mcp.json' file must have no json syntax errors.", "- Unity may stay 'Connecting...' until the first prompt sent is processed.", "- Restart Visual Studio after configuration changes");
	}
}
}
