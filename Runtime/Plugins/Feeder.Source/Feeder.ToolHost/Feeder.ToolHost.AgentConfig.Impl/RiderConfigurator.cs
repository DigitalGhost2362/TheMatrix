using System;
using System.Collections.Generic;
using System.IO;
using Feeder.Json.Nodes;
using Feeder.ToolHost.Common;
using Feeder.Logging;

namespace Feeder.ToolHost.AgentConfig.Impl
{
public sealed class RiderConfigurator : AiAgentConfigurator
{
	public override string AgentName => "Rider (Junie)";

	public override string AgentId => "rider-junie";

	public override string DownloadUrl => "https://www.jetbrains.com/rider/download/";

	public override string? SkillsPath => ".junie/skills";

	public override string? IconName => "rider-64.png";

	private static string JunieConfigPath(AgentConfiguratorSettings s)
	{
		return Path.Combine(s.ProjectRootPath, ".junie", "mcp", "mcp.json");
	}

	protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		return new JsonAiAgentConfig(AgentName, JunieConfigPath(settings), "mcpServers", logger).SetProperty("enabled", JsonValue.Create(value: true), requiredForConfiguration: true).SetPropertyToRemove("disabled").SetProperty("type", JsonValue.Create("stdio"), requiredForConfiguration: true)
			.SetProperty("command", JsonValue.Create(settings.ExecutableFullPath.Replace('\\', '/')), requiredForConfiguration: true, ValueComparisonMode.Path)
			.SetProperty("args", AgentConfigBuilders.StdioArgs(settings), requiredForConfiguration: true)
			.SetPropertyToRemove("url");
	}

	protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		return new JsonAiAgentConfig(AgentName, JunieConfigPath(settings), "mcpServers", logger).SetProperty("enabled", JsonValue.Create(value: true), requiredForConfiguration: true).SetPropertyToRemove("disabled").SetProperty("type", JsonValue.Create("http"), requiredForConfiguration: true)
			.SetProperty("url", JsonValue.Create(settings.Host), requiredForConfiguration: true, ValueComparisonMode.Url)
			.SetPropertyToRemove("command")
			.SetPropertyToRemove("args");
	}

	protected override IReadOnlyList<ConfigurationSection> BuildSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		if (transport == Consts.MCP.Server.TransportMethod.stdio)
		{
			string text = Path.Combine(".junie", "mcp", "mcp.json");
			string expectedFileContent = GetStdioConfig(settings, logger).ExpectedFileContent;
			string text2;
			string value;
			if (settings.IsWindows)
			{
				text2 = "Option 1: Run this command in PowerShell from the project folder";
				value = "New-Item -ItemType Directory -Force -Path .junie\\mcp | Out-Null; Set-Content -Path " + text.Replace('/', '\\') + " -Value '" + expectedFileContent.Replace("'", "''") + "'";
			}
			else
			{
				text2 = "Option 1: Run this command in a terminal from the project folder";
				value = "mkdir -p .junie/mcp && printf '%s\\n' '" + expectedFileContent.Replace("'", "'\\''") + "' > " + text.Replace('\\', '/');
			}
			return new ConfigurationSection[1]
			{
				new ConfigurationSection("Manual Configuration Steps", expandedFirst: true, new ConfigurationItem[6]
				{
					ConfigurationItem.Warning("After configuring, open Rider Settings / Tools / Junie / MCP Settings and enable the server to connect the AI agent."),
					ConfigurationItem.Description(text2),
					ConfigurationItem.ReadOnlyField(value),
					ConfigurationItem.Description("Option 2: Create or open the file '" + text.Replace('\\', '/') + "' and paste the JSON below."),
					ConfigurationItem.ReadOnlyField(expectedFileContent),
					ConfigurationItem.Description("Option 3: Open Rider Settings / Tools / Junie / MCP Settings and add a new server manually.")
				})
			};
		}
		return new ConfigurationSection[1]
		{
			new ConfigurationSection("Configuration", expandedFirst: true, new ConfigurationItem[2]
			{
				ConfigurationItem.Warning("Rider (Junie) connects via stdio. Switch the transport method to 'stdio' to configure this agent."),
				ConfigurationItem.ReadOnlyField(GetHttpConfig(settings, logger).ExpectedFileContent)
			})
		};
	}

	protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		if (transport != Consts.MCP.Server.TransportMethod.stdio)
		{
			return Array.Empty<ConfigurationSection>();
		}
		return AiAgentConfigurator.TroubleshootingSection("- Ensure MCP configuration file doesn't have syntax errors", "- Restart Rider after configuration changes", "- If using Terminal, ensure you are in your project root folder.");
	}
}
}
