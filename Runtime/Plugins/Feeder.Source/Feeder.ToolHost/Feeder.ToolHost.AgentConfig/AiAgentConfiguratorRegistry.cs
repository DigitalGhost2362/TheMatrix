using System.Collections.Generic;
using System.Linq;
using Feeder.ToolHost.AgentConfig.Impl;

namespace Feeder.ToolHost.AgentConfig
{
public static class AiAgentConfiguratorRegistry
{
	private static readonly IReadOnlyList<AiAgentConfigurator> _configurators = new AiAgentConfigurator[15]
	{
		new ClaudeCodeConfigurator(),
		new ClaudeDesktopConfigurator(),
		new VisualStudioCodeCopilotConfigurator(),
		new VisualStudioCopilotConfigurator(),
		new RiderConfigurator(),
		new CursorConfigurator(),
		new GitHubCopilotCliConfigurator(),
		new GeminiConfigurator(),
		new AntigravityConfigurator(),
		new ClineConfigurator(),
		new OpenCodeConfigurator(),
		new CodexConfigurator(),
		new KiloCodeConfigurator(),
		new UnityAiConfigurator(),
		new ZooCodeConfigurator()
	}.OrderBy((AiAgentConfigurator c) => c.AgentName).Append(new CustomConfigurator()).ToList();

	public static IReadOnlyList<AiAgentConfigurator> All => _configurators;

	public static List<string> GetAgentNames()
	{
		return _configurators.Select((AiAgentConfigurator c) => c.AgentName).ToList();
	}

	public static List<string> GetAgentIds()
	{
		return _configurators.Select((AiAgentConfigurator c) => c.AgentId).ToList();
	}

	public static AiAgentConfigurator? GetByAgentId(string? agentId)
	{
		if (string.IsNullOrEmpty(agentId))
		{
			return null;
		}
		return _configurators.FirstOrDefault((AiAgentConfigurator c) => c.AgentId == agentId);
	}

	public static AiAgentConfigurator? GetByAgentName(string? agentName)
	{
		if (string.IsNullOrEmpty(agentName))
		{
			return null;
		}
		return _configurators.FirstOrDefault((AiAgentConfigurator c) => c.AgentName == agentName);
	}

	public static int GetIndexByAgentId(string? agentId)
	{
		if (string.IsNullOrEmpty(agentId))
		{
			return -1;
		}
		for (int i = 0; i < _configurators.Count; i++)
		{
			if (_configurators[i].AgentId == agentId)
			{
				return i;
			}
		}
		return -1;
	}

	public static int GetIndexByAgentName(string? agentName)
	{
		if (string.IsNullOrEmpty(agentName))
		{
			return -1;
		}
		for (int i = 0; i < _configurators.Count; i++)
		{
			if (_configurators[i].AgentName == agentName)
			{
				return i;
			}
		}
		return -1;
	}
}
}
