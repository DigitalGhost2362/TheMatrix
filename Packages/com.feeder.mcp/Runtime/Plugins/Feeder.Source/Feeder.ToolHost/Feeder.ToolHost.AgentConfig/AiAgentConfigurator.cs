using System;
using System.Collections.Generic;
using System.IO;
using Feeder.ToolHost.Common;
using Feeder.Logging;

namespace Feeder.ToolHost.AgentConfig
{
public abstract class AiAgentConfigurator
{
	public abstract string AgentName { get; }

	public abstract string AgentId { get; }

	public abstract string DownloadUrl { get; }

	public virtual string? SkillsPath => null;

	public bool SupportsSkills => SkillsPath != null;

	public virtual string TutorialUrl => string.Empty;

	public virtual string TutorialLinkLabel => "Tutorial";

	public virtual string DownloadLinkLabel => "Download";

	public abstract string? IconName { get; }

	protected abstract AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger);

	protected abstract AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger);

	public AiAgentConfig GetStdioConfig(AgentConfiguratorSettings settings, ILogger? logger = null)
	{
		AiAgentConfig aiAgentConfig = CreateStdioConfig(settings, logger);
		ApplyStdioAuthorization(aiAgentConfig, settings);
		return aiAgentConfig;
	}

	public AiAgentConfig GetHttpConfig(AgentConfiguratorSettings settings, ILogger? logger = null)
	{
		AiAgentConfig aiAgentConfig = CreateHttpConfig(settings, logger);
		ApplyHttpAuthorization(aiAgentConfig, settings);
		return aiAgentConfig;
	}

	protected virtual void ApplyStdioAuthorization(AiAgentConfig config, AgentConfiguratorSettings settings)
	{
		config.ApplyStdioAuthorization(settings.IsStdioAuthRequired, settings.Token);
	}

	protected virtual void ApplyHttpAuthorization(AiAgentConfig config, AgentConfiguratorSettings settings)
	{
		config.ApplyHttpAuthorization(settings.IsHttpAuthRequired, settings.Token);
	}

	public bool IsDetected(AgentConfiguratorSettings settings, ILogger? logger = null)
	{
		if (!GetStdioConfig(settings, logger).IsDetected())
		{
			return GetHttpConfig(settings, logger).IsDetected();
		}
		return true;
	}

	public virtual bool IsConfigured(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger = null)
	{
		return ((transport == Consts.MCP.Server.TransportMethod.stdio) ? GetStdioConfig(settings, logger) : GetHttpConfig(settings, logger)).IsConfigured();
	}

	public virtual ConfiguratorStatus GetStatus(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger = null)
	{
		if (IsConfigured(settings, transport, logger))
		{
			return ConfiguratorStatus.Configured;
		}
		if (!((transport == Consts.MCP.Server.TransportMethod.stdio) ? GetStdioConfig(settings, logger) : GetHttpConfig(settings, logger)).IsDetected())
		{
			return ConfiguratorStatus.NotConfigured;
		}
		return ConfiguratorStatus.ReconfigureNeeded;
	}

	public virtual IReadOnlyList<ConfigurationItem> BuildLinks()
	{
		List<ConfigurationItem> list = new List<ConfigurationItem>();
		if (!string.IsNullOrEmpty(DownloadUrl))
		{
			list.Add(ConfigurationItem.Link(DownloadLinkLabel, DownloadUrl));
		}
		if (!string.IsNullOrEmpty(TutorialUrl))
		{
			list.Add(ConfigurationItem.Link(TutorialLinkLabel, TutorialUrl));
		}
		return list;
	}

	public AgentConfiguratorDescription Describe(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger = null)
	{
		IReadOnlyList<ConfigurationSection> readOnlyList = BuildSections(settings, transport, logger);
		ConfiguratorStatus status = GetStatus(settings, transport, logger);
		IReadOnlyList<ConfigurationSection> readOnlyList2 = BuildTroubleshootingSections(settings, transport, logger);
		if (readOnlyList2.Count > 0)
		{
			List<ConfigurationSection> list = new List<ConfigurationSection>(readOnlyList);
			list.AddRange(readOnlyList2);
			readOnlyList = list;
		}
		if (status == ConfiguratorStatus.ReconfigureNeeded)
		{
			List<ConfigurationSection> list2 = new List<ConfigurationSection>();
			list2.Add(new ConfigurationSection("Reconfiguration Required", expandedFirst: true, new ConfigurationItem[1] { ConfigurationItem.Alert("Connection settings have changed. The existing MCP configuration is outdated and needs to be updated.") }));
			list2.AddRange(readOnlyList);
			readOnlyList = list2;
		}
		return new AgentConfiguratorDescription(AgentName, AgentId, IconName, status == ConfiguratorStatus.Configured, isInstalled: false, readOnlyList, status, BuildLinks());
	}

	protected abstract IReadOnlyList<ConfigurationSection> BuildSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger);

	protected virtual IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		return Array.Empty<ConfigurationSection>();
	}

	protected static IReadOnlyList<ConfigurationSection> TroubleshootingSection(params string[] lines)
	{
		ConfigurationItem[] array = new ConfigurationItem[lines.Length];
		for (int i = 0; i < lines.Length; i++)
		{
			array[i] = ConfigurationItem.Description(lines[i]);
		}
		return new ConfigurationSection[1]
		{
			new ConfigurationSection("Troubleshooting", expandedFirst: false, array)
		};
	}

	protected IReadOnlyList<ConfigurationSection> DefaultConfigurationSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		AiAgentConfig aiAgentConfig = ((transport == Consts.MCP.Server.TransportMethod.stdio) ? GetStdioConfig(settings, logger) : GetHttpConfig(settings, logger));
		return new ConfigurationSection[1]
		{
			new ConfigurationSection("Configuration", expandedFirst: true, new ConfigurationItem[2]
			{
				ConfigurationItem.Description("Use the Configure button to write the MCP entry into " + AgentName + "'s config file."),
				ConfigurationItem.ReadOnlyField(aiAgentConfig.ExpectedFileContent)
			})
		};
	}

	protected static string ResolveAbsoluteSkillsPath(string projectRootPath, string folder)
	{
		if (string.IsNullOrEmpty(folder))
		{
			return folder;
		}
		if (!Path.IsPathRooted(folder))
		{
			return Path.GetFullPath(Path.Combine(projectRootPath, folder));
		}
		return folder;
	}
}
}
