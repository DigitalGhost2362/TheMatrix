using System.Collections.Generic;
using System.IO;
using Feeder.ToolHost.Common;
using Feeder.Logging;

namespace Feeder.ToolHost.AgentConfig.Impl
{
public sealed class ZooCodeConfigurator : AiAgentConfigurator
{
	public override string AgentName => "Zoo Code";

	public override string AgentId => "zoo-code";

	public override string DownloadUrl => "https://www.zoocode.dev/";

	public override string? SkillsPath => ".roo/skills";

	public override string? IconName => "zoo-code-64.png";

	private static string LocalConfigPath(AgentConfiguratorSettings s)
	{
		return Path.Combine(s.ProjectRootPath, ".roo", "mcp.json");
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
		return AiAgentConfigurator.TroubleshootingSection("- Ensure the JSON file has no syntax errors.", "- Verify Zoo Code has MCP support enabled.", "- The configuration file should be in your project root, next to Assets folder.", "- Restart Zoo Code after configuration changes.");
	}
}
}
