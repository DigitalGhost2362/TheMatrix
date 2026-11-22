using System.Collections.Generic;
using System.IO;
using Feeder.ToolHost.Common;
using Feeder.Logging;

namespace Feeder.ToolHost.AgentConfig.Impl
{
public sealed class KiloCodeConfigurator : AiAgentConfigurator
{
	public override string AgentName => "Kilo Code";

	public override string AgentId => "kilo-code";

	public override string DownloadUrl => "https://app.kilo.ai/get-started";

	public override string? SkillsPath => ".kilocode/skills";

	public override string? IconName => "kilo-code-64.png";

	private static string LocalConfigPath(AgentConfiguratorSettings s)
	{
		return Path.Combine(s.ProjectRootPath, ".kilocode", "mcp.json");
	}

	protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		return AgentConfigBuilders.JsonStdio(AgentName, LocalConfigPath(settings), settings, logger, "mcpServers", false);
	}

	protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		return AgentConfigBuilders.JsonHttp(AgentName, LocalConfigPath(settings), settings, logger, "mcpServers", "streamable-http", false);
	}

	protected override IReadOnlyList<ConfigurationSection> BuildSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		return DefaultConfigurationSections(settings, transport, logger);
	}

	protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		if (transport != Consts.MCP.Server.TransportMethod.stdio)
		{
			return AiAgentConfigurator.TroubleshootingSection("- Ensure the JSON file has no syntax errors.", "- Verify Kilo Code has MCP support enabled.", "- The configuration file should be in your project root, next to Assets folder.", "- Restart Kilo Code after configuration changes");
		}
		return AiAgentConfigurator.TroubleshootingSection("- Ensure the JSON file has no syntax errors.", "- Check that the executable path is correct.", "- Verify Kilo Code has MCP support enabled.", "- The configuration file should be in your project root, next to Assets folder.", "- Restart Kilo Code after configuration changes");
	}
}
}
