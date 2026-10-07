#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using AgentConfig = Feeder.ToolHost.AgentConfig;

namespace Feeder.MCP.Editor.UI
{
    /// <summary>
    /// Package-side extension of the shared <see cref="AgentConfig.AiAgentConfiguratorRegistry"/>.
    ///
    /// The shared registry (compiled into Feeder.ToolHost.dll) is a static, closed list with no
    /// registration API, so agents that are not part of the shared library — currently
    /// <see cref="DeepSeekAiAgentConfigurator"/> — are injected by this assembly. DeepSeek is
    /// inserted immediately after Claude Code in the list order.
    ///
    /// All window and startup paths that need the agent list go through this catalog so the
    /// injected agents behave identically to the shared ones (chips, MCP auto-configure,
    /// skills auto-generation).
    /// </summary>
    public static class AiAgentCatalog
    {
        /// <summary>Agent id of the injected DeepSeek configurator.</summary>
        public const string DeepSeekAgentId = DeepSeekAiAgentConfigurator.Id;

        /// <summary>
        /// The full agent list: every shared configurator from the registry plus the injected
        /// DeepSeek entry placed right after Claude Code.
        /// </summary>
        public static IReadOnlyList<AgentConfig.AiAgentConfigurator> All { get; } = Build();

        public static List<string> GetAgentNames() => All.Select(c => c.AgentName).ToList();

        public static int GetIndexByAgentId(string? agentId)
        {
            if (string.IsNullOrEmpty(agentId)) return -1;
            for (var i = 0; i < All.Count; i++)
                if (string.Equals(All[i].AgentId, agentId, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        public static AgentConfig.AiAgentConfigurator? GetByAgentId(string? agentId)
        {
            var index = GetIndexByAgentId(agentId);
            return index >= 0 ? All[index] : null;
        }

        static List<AgentConfig.AiAgentConfigurator> Build()
        {
            var list = new List<AgentConfig.AiAgentConfigurator>(AgentConfig.AiAgentConfiguratorRegistry.All);
            var claudeIndex = list.FindIndex(c => string.Equals(c.AgentId, "claude-code", StringComparison.OrdinalIgnoreCase));
            var insertAt = claudeIndex >= 0 ? claudeIndex + 1 : Math.Min(1, list.Count);
            list.Insert(insertAt, new DeepSeekAiAgentConfigurator());
            return list;
        }
    }
}
