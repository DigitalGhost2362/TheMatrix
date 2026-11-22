using System;
using System.Collections.Generic;
using Feeder.ToolHost.Common;
using Feeder.Logging;

namespace Feeder.ToolHost.AgentConfig.Impl
{
public sealed class CustomConfigurator : AiAgentConfigurator
{
	public string EditableSkillsPath { get; set; } = ".claude/skills";

	public override string AgentName => "Other - Custom";

	public override string AgentId => "other-custom";

	public override string DownloadUrl => "NA";

	public override string? SkillsPath => EditableSkillsPath;

	public override string? IconName => null;

	protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		throw new NotImplementedException("CustomConfigurator has no auto-detectable config; use the generated snippet from the description instead.");
	}

	protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		throw new NotImplementedException("CustomConfigurator has no auto-detectable config; use the generated snippet from the description instead.");
	}

	public override bool IsConfigured(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger = null)
	{
		return false;
	}

	public override ConfiguratorStatus GetStatus(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger = null)
	{
		return ConfiguratorStatus.NotConfigured;
	}

	public override IReadOnlyList<ConfigurationItem> BuildLinks()
	{
		return Array.Empty<ConfigurationItem>();
	}

	protected override IReadOnlyList<ConfigurationSection> BuildSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		List<ConfigurationItem> list = new List<ConfigurationItem>
		{
			ConfigurationItem.Description("Skills output path (editable):"),
			ConfigurationItem.EditableField(EditableSkillsPath)
		};
		if (transport == Consts.MCP.Server.TransportMethod.stdio)
		{
			string value = Consts.MCP.Server.Config(settings.ExecutableFullPath.Replace('\\', '/'), "Feeder-MCP", "mcpServers", settings.Port, settings.TimeoutMs).ToString();
			list.Add(ConfigurationItem.Description("Copy paste the json into your MCP Client to configure it."));
			list.Add(ConfigurationItem.ReadOnlyField(value));
		}
		else
		{
			list.Add(ConfigurationItem.Description("Copy paste the json into your MCP Client to configure it."));
			list.Add(ConfigurationItem.ReadOnlyField("{\"mcpServers\":{\"Feeder-MCP\":{\"type\":\"http\",\"url\":\"" + settings.Host + "\"}}}"));
		}
		return new ConfigurationSection[1]
		{
			new ConfigurationSection("Configuration", expandedFirst: true, list)
		};
	}
}
}
