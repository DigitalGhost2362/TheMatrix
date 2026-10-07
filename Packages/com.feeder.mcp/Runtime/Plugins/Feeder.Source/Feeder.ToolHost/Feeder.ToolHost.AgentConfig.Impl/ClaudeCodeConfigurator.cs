using System.Collections.Generic;
using System.IO;
using Feeder.Json.Nodes;
using Feeder.ToolHost.Common;
using Feeder.Logging;

namespace Feeder.ToolHost.AgentConfig.Impl
{
public sealed class ClaudeCodeConfigurator : AiAgentConfigurator
{
	public override string AgentName => "Claude Code";

	public override string AgentId => "claude-code";

	public override string DownloadUrl => "https://docs.anthropic.com/en/docs/claude-code/overview";

	public override string? SkillsPath => ".claude/skills";

	public override string? IconName => "claude-64.png";

	private static string LocalConfigPath(AgentConfiguratorSettings s)
	{
		return Path.Combine(s.ProjectRootPath, ".mcp.json");
	}

	protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		return new JsonAiAgentConfig(AgentName, LocalConfigPath(settings), "mcpServers", logger).SetProperty("command", JsonValue.Create(settings.ExecutableFullPath.Replace('\\', '/')), requiredForConfiguration: true, ValueComparisonMode.Path).SetProperty("args", AgentConfigBuilders.StdioArgs(settings), requiredForConfiguration: true).SetPropertyToRemove("type")
			.SetPropertyToRemove("url");
	}

	protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		return AgentConfigBuilders.JsonHttp(AgentName, LocalConfigPath(settings), settings, logger);
	}

	protected override IReadOnlyList<ConfigurationSection> BuildSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		bool isHttpAuthRequired = settings.IsHttpAuthRequired;
		string text = ((!string.IsNullOrEmpty(settings.Token)) ? settings.Token : "<token>");
		if (transport == Consts.MCP.Server.TransportMethod.stdio)
		{
			string text2 = (settings.IsStdioAuthRequired ? string.Format(" {0}={1} {2}={3}", "authorization", Consts.MCP.Server.AuthOption.required, "token", text) : string.Empty);
			string value = string.Format("claude mcp add {0} \"{1}\" port={2} plugin-timeout={3} client-transport=stdio{4}", "Feeder-MCP", settings.ExecutableFullPath, settings.Port, settings.TimeoutMs, text2);
			return new ConfigurationSection[2]
			{
				new ConfigurationSection("Start", expandedFirst: true, new ConfigurationItem[4]
				{
					ConfigurationItem.Description("Navigate to project root"),
					ConfigurationItem.ReadOnlyField("cd \"" + settings.ProjectRootPath + "\""),
					ConfigurationItem.Description("Launch Claude Code"),
					ConfigurationItem.ReadOnlyField("claude")
				}),
				new ConfigurationSection("Manual Configuration Steps", expandedFirst: false, new ConfigurationItem[4]
				{
					ConfigurationItem.Description("Run the following command in the project folder to configure Claude Code"),
					ConfigurationItem.ReadOnlyField(value),
					ConfigurationItem.Description("Restart or start Claude Code to apply the configuration"),
					ConfigurationItem.ReadOnlyField("claude")
				})
			};
		}
		string text3 = (isHttpAuthRequired ? (" --header \"Authorization: Bearer " + text + "\"") : string.Empty);
		string value2 = "claude mcp add --transport http Feeder-MCP " + settings.Host + text3;
		return new ConfigurationSection[2]
		{
			new ConfigurationSection("Start", expandedFirst: true, new ConfigurationItem[4]
			{
				ConfigurationItem.Description("Navigate to project root"),
				ConfigurationItem.ReadOnlyField("cd \"" + settings.ProjectRootPath + "\""),
				ConfigurationItem.Description("Launch Claude Code"),
				ConfigurationItem.ReadOnlyField("claude")
			}),
			new ConfigurationSection("Manual Configuration Steps", expandedFirst: false, new ConfigurationItem[4]
			{
				ConfigurationItem.Description("Run the following command in the project folder to configure Claude Code"),
				ConfigurationItem.ReadOnlyField(value2),
				ConfigurationItem.Description("Restart or start Claude Code to apply the configuration"),
				ConfigurationItem.ReadOnlyField("claude")
			})
		};
	}

	protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		return AiAgentConfigurator.TroubleshootingSection("- Ensure Claude Code CLI is installed and accessible from terminal", "- Ensure Claude Code CLI is started in the same folder where the project is located. This folder must contain the Assets folder inside", "- Ensure Claude Code is configured with the same port as the plugin right now", "- Check that the configuration file .mcp.json exists", "- Restart Claude Code after configuration changes");
	}
}
}
