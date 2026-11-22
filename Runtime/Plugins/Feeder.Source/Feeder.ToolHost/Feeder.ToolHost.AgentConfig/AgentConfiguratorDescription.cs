using System;
using System.Collections.Generic;

namespace Feeder.ToolHost.AgentConfig
{
public sealed class AgentConfiguratorDescription
{
	public string AgentName { get; }

	public string AgentId { get; }

	public string? IconName { get; }

	public bool IsConfigured { get; }

	public ConfiguratorStatus Status { get; }

	public IReadOnlyList<ConfigurationItem> Links { get; }

	public bool IsInstalled { get; }

	public IReadOnlyList<ConfigurationSection> Sections { get; }

	public AgentConfiguratorDescription(string agentName, string agentId, string? iconName, bool isConfigured, bool isInstalled, IReadOnlyList<ConfigurationSection> sections, ConfiguratorStatus status = ConfiguratorStatus.NotConfigured, IReadOnlyList<ConfigurationItem>? links = null)
	{
		AgentName = agentName;
		AgentId = agentId;
		IconName = iconName;
		IsConfigured = isConfigured;
		IsInstalled = isInstalled;
		Sections = sections;
		Status = status;
		Links = links ?? Array.Empty<ConfigurationItem>();
	}
}
}
