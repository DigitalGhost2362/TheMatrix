using System.Collections.Generic;
using System.IO;
using Feeder.ToolHost.Common;
using Feeder.Logging;

namespace Feeder.ToolHost.AgentConfig.Impl
{
public sealed class VisualStudioCodeCopilotConfigurator : AiAgentConfigurator
{
	public override string AgentName => "Visual Studio Code (Copilot)";

	public override string AgentId => "vscode-copilot";

	public override string DownloadUrl => "https://code.visualstudio.com/download";

	public override string? SkillsPath => ".claude/skills";

	public override string? IconName => "vs-code-64.png";

	private static string LocalConfigPath(AgentConfiguratorSettings s)
	{
		return Path.Combine(s.ProjectRootPath, ".vscode", "mcp.json");
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
		return AiAgentConfigurator.TroubleshootingSection("- '.vscode/mcp.json' file must have no json syntax errors.", "- Restart Visual Studio Code after configuration changes");
	}
}
}
