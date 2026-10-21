#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Feeder.Json.Nodes;
using Feeder.MCP.Utils;
using Feeder.Logging;
using AgentConfig = Feeder.ToolHost.AgentConfig;
using ILogger = Feeder.Logging.ILogger;
using static Feeder.ToolHost.Common.Consts.MCP.Server;

namespace Feeder.MCP.Editor.UI
{
    internal enum AgentWiring
    {
        Unwired,
        Wired,
        Stale,
    }

    internal static class BridgeAgents
    {
        static readonly ILogger _logger = UnityLoggerFactory.LoggerFactory.CreateLogger(nameof(BridgeAgents));

        static readonly string[] _chipIds = { "claude-code", "codex", "cursor", "deepseek", "gemini" };
        const string SkippedId = "other-custom";

        public static IReadOnlyList<AgentConfig.AiAgentConfigurator> Chips()
        {
            var list = new List<AgentConfig.AiAgentConfigurator>(_chipIds.Length);
            foreach (var id in _chipIds)
            {
                var agent = AiAgentCatalog.GetByAgentId(id);
                if (agent == null)
                    _logger.LogError("Bridge chip agent '{id}' is not in the agent catalog.", id);
                else
                    list.Add(agent);
            }
            return list;
        }

        public static IReadOnlyList<AgentConfig.AiAgentConfigurator> Overflow() => AiAgentCatalog.All
            .Where(agent => agent.AgentId != SkippedId && Array.IndexOf(_chipIds, agent.AgentId) < 0)
            .ToList();

        public static string ShortName(AgentConfig.AiAgentConfigurator agent)
        {
            var name = agent.AgentName;
            var space = name.IndexOf(' ');
            return (space > 0 ? name.Substring(0, space) : name).ToLowerInvariant();
        }

        public static AgentWiring GetState(AgentConfig.AiAgentConfigurator agent)
        {
            var status = agent.GetStatus(AgentConfiguratorSettingsFactory.Create(), MatrixPluginEditor.TransportMethod);
            return status switch
            {
                AgentConfig.ConfiguratorStatus.Configured => AgentWiring.Wired,
                AgentConfig.ConfiguratorStatus.ReconfigureNeeded => AgentWiring.Stale,
                _ => AgentWiring.Unwired,
            };
        }

        public static string Describe(AgentConfig.AiAgentConfigurator agent)
        {
            var transport = MatrixPluginEditor.TransportMethod;
            var path = Config(agent, AgentConfiguratorSettingsFactory.Create(), transport).ConfigPath;
            return $"{agent.AgentName}\n{path}";
        }

        public static void Toggle(AgentConfig.AiAgentConfigurator agent)
        {
            if (GetState(agent) == AgentWiring.Wired)
                Unwire(agent);
            else
                Wire(agent);
        }

        static AgentConfig.AiAgentConfig Config(AgentConfig.AiAgentConfigurator agent, AgentConfig.AgentConfiguratorSettings settings, TransportMethod transport)
            => transport == TransportMethod.stdio
                ? agent.GetStdioConfig(settings)
                : agent.GetHttpConfig(settings);

        // The JSON writer replaces a config file it cannot parse (comments, a half-written file) with only our entry,
        // dropping every other server and setting in it, so such a file is left alone.
        internal static bool CanRewrite(AgentConfig.AiAgentConfig config)
        {
            if (config is not AgentConfig.JsonAiAgentConfig || !File.Exists(config.ConfigPath))
                return true;
            try
            {
                var text = File.ReadAllText(config.ConfigPath);
                return string.IsNullOrWhiteSpace(text) || JsonNode.Parse(text) is JsonObject;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static void Wire(AgentConfig.AiAgentConfigurator agent)
        {
            var settings = AgentConfiguratorSettingsFactory.Create();
            var config = Config(agent, settings, MatrixPluginEditor.TransportMethod);
            if (!CanRewrite(config))
            {
                _logger.LogError("Not wiring {agent}: {path} is not plain JSON (comments or a half-written file), and rewriting it would drop the other entries. Fix the file, then wire again.",
                    agent.AgentName, config.ConfigPath);
                return;
            }
            config.Configure();
            MatrixPluginEditor.SetAutoConfigureAgent(agent.AgentId, true);
            if (agent.SupportsSkills)
            {
                MatrixPluginEditor.SetAutoGenerateSkills(agent.AgentId, true);
                GenerateSkills(agent);
            }
            MatrixPluginEditor.Instance.Save();
        }

        public static bool GenerateSkills(AgentConfig.AiAgentConfigurator agent)
        {
            var previous = MatrixPluginEditor.SkillsPath;
            try
            {
                MatrixPluginEditor.SkillsPath = agent.SkillsPath!;
                return MatrixPluginEditor.Instance.ToolHostInstance?.GenerateSkillFiles(MatrixPluginEditor.ProjectRootPath) == true;
            }
            finally
            {
                MatrixPluginEditor.SkillsPath = previous;
            }
        }

        static string SkillsStampKey => $"Feeder_Matrix_SkillsStamp_{UnityEngine.Application.dataPath}";

        // the skills only change with the package version or the set of enabled tools
        static string? SkillsStamp()
        {
            var tools = MatrixPluginEditor.Instance.Tools;
            var all = tools?.GetAllTools();
            if (all == null)
                return null;

            var enabled = all
                .Where(tool => tools!.IsToolEnabled(tool.Name))
                .Select(tool => tool.Name)
                .OrderBy(name => name, StringComparer.Ordinal);
            return $"{MatrixPlugin.Version}|{string.Join(",", enabled)}";
        }

        public static void RegenerateSkillsIfChanged()
        {
            var stamp = SkillsStamp();
            if (stamp == null || UnityEditor.EditorPrefs.GetString(SkillsStampKey, string.Empty) != stamp)
                RegenerateSkills();
        }

        public static int RegenerateSkills()
        {
            var stamp = SkillsStamp();
            var generated = true;
            var count = 0;
            foreach (var agentId in MatrixPluginEditor.AutoConfigureAgentIds)
            {
                var agent = AiAgentCatalog.GetByAgentId(agentId);
                if (agent?.SupportsSkills != true || !MatrixPluginEditor.IsAutoGenerateSkills(agent.AgentId))
                    continue;

                generated &= GenerateSkills(agent);
                count++;
            }
            if (stamp != null && generated)
                UnityEditor.EditorPrefs.SetString(SkillsStampKey, stamp);
            return count;
        }

        static void Unwire(AgentConfig.AiAgentConfigurator agent)
        {
            var settings = AgentConfiguratorSettingsFactory.Create();
            Config(agent, settings, MatrixPluginEditor.TransportMethod).Unconfigure();
            MatrixPluginEditor.SetAutoConfigureAgent(agent.AgentId, false);
            if (agent.SupportsSkills)
                MatrixPluginEditor.SetAutoGenerateSkills(agent.AgentId, false);
            MatrixPluginEditor.Instance.Save();
        }
    }
}
