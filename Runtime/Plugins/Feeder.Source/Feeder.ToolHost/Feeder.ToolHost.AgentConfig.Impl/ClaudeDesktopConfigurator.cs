using System;
using System.Collections.Generic;
using System.IO;
using Feeder.ToolHost.Common;
using Feeder.Logging;

namespace Feeder.ToolHost.AgentConfig.Impl
{
public sealed class ClaudeDesktopConfigurator : AiAgentConfigurator
{
	public override string AgentName => "Claude Desktop";

	public override string AgentId => "claude-desktop";

	public override string DownloadUrl => "https://code.claude.com/docs/en/desktop";

	public override string? IconName => "claude-64.png";

	private static string ConfigPath(AgentConfiguratorSettings s)
	{
		if (!s.IsWindows)
		{
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Claude", "claude_desktop_config.json");
		}
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming", "Claude", "claude_desktop_config.json");
	}

	protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		return AgentConfigBuilders.JsonStdio(AgentName, ConfigPath(settings), settings, logger);
	}

	protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		return AgentConfigBuilders.JsonHttp(AgentName, ConfigPath(settings), settings, logger);
	}

	protected override IReadOnlyList<ConfigurationSection> BuildSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		return DefaultConfigurationSections(settings, transport, logger);
	}

	protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		if (transport != Consts.MCP.Server.TransportMethod.stdio)
		{
			return Array.Empty<ConfigurationSection>();
		}
		return AiAgentConfigurator.TroubleshootingSection("- Claude Desktop may launch two MCP server instances instead of one. If you must use Claude Desktop, manually terminate one of the instances. This behavior is unreliable — consider switching to Claude Code.", "- Claude Desktop may not detect runtime updates to MCP tools. Ensure Claude Desktop reads the MCP tools on startup.", "- Start the plugin first; the connection status should read 'Connecting...'", "- Restart Claude Desktop after configuration changes");
	}
}
}
