using System.Collections.Generic;
using System.IO;
using Feeder.ToolHost.Common;
using Feeder.Logging;

namespace Feeder.ToolHost.AgentConfig.Impl
{
public sealed class GeminiConfigurator : AiAgentConfigurator
{
	public override string AgentName => "Gemini";

	public override string AgentId => "gemini";

	public override string DownloadUrl => "https://geminicli.com/docs/get-started/installation/";

	public override string? SkillsPath => ".gemini/skills";

	public override string? IconName => "gemini-64.png";

	private static string LocalConfigPath(AgentConfiguratorSettings s)
	{
		return Path.Combine(s.ProjectRootPath, ".gemini", "settings.json");
	}

	protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		return AgentConfigBuilders.JsonStdio(AgentName, LocalConfigPath(settings), settings, logger);
	}

	protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		return AgentConfigBuilders.JsonHttp(AgentName, LocalConfigPath(settings), settings, logger);
	}

	protected override IReadOnlyList<ConfigurationSection> BuildSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		return DefaultConfigurationSections(settings, transport, logger);
	}

	protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		if (transport != Consts.MCP.Server.TransportMethod.stdio)
		{
			return AiAgentConfigurator.TroubleshootingSection("- Ensure Gemini CLI is installed and accessible from terminal", "- Ensure MCP configuration file doesn't have syntax errors", "- Restart Gemini after configuration changes");
		}
		return AiAgentConfigurator.TroubleshootingSection("- Ensure Gemini CLI is installed and accessible from terminal", "- Start Gemini with --debug flag, it helps MCP server to work properly with Gemini in stdio transport mode.", "- Ensure MCP configuration file doesn't have syntax errors", "- Restart Gemini after configuration changes");
	}
}
}
