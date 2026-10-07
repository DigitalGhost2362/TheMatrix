#nullable enable
using System;
using Feeder.MCP.Editor.UI;
using Feeder.MCP.Runtime.Utils;
using Feeder.Logging;
using UnityEditor;
using UnityEngine;
using static Feeder.ToolHost.Common.Consts.MCP.Server;

namespace Feeder.MCP.Editor
{
    public static partial class Startup
    {
        // keyed by exe path so a package update that moves or renames the exe rewrites stdio agent configs in the same session
        static string AutoConfigureSessionKey => $"Feeder.MCP.AutoConfigured.{McpServerManager.ExecutableFullPath}";

        /// <summary>
        /// Writes the MCP config file for every agent with the auto-configure flag enabled
        /// (default: Claude Code's project-root <c>.mcp.json</c>) once Matrix has been turned
        /// on for the project, so the file keeps following the directory-derived port without
        /// opening the Matrix Bridge window. Runs once per editor session
        /// (SessionState survives domain reloads); Configure() is idempotent so the
        /// unconditional write self-heals stale ports.
        /// </summary>
        static void AutoConfigureAgents()
        {
            try
            {
                if (Application.isBatchMode || EnvironmentUtils.IsCi())
                    return;

                if (SessionState.GetBool(AutoConfigureSessionKey, false))
                    return;
                SessionState.SetBool(AutoConfigureSessionKey, true);
                ConfigureAgents();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "MCP agent auto-configure failed; editor load is unaffected. Wire the agent manually in Tools/Feeder/Bridge.");
            }
        }

        /// <summary>Rewrites the MCP config of every auto-configured agent with the current host/port.</summary>
        internal static void ConfigureAgents()
        {
            try
            {
                var settings = AgentConfiguratorSettingsFactory.Create();
                var transport = MatrixPluginEditor.TransportMethod;

                foreach (var agentId in MatrixPluginEditor.AutoConfigureAgentIds)
                {
                    var configurator = AiAgentCatalog.GetByAgentId(agentId);
                    if (configurator == null)
                        continue;

                    var config = transport == TransportMethod.stdio
                        ? configurator.GetStdioConfig(settings)
                        : configurator.GetHttpConfig(settings);
                    if (!BridgeAgents.CanRewrite(config))
                    {
                        _logger.LogWarning("Skipped MCP auto-configure for agent '{agentId}': '{path}' is not plain JSON, and rewriting it would drop its other entries.", agentId, config.ConfigPath);
                        continue;
                    }
                    config.Configure();
                    _logger.LogInformation("Auto-configured MCP for agent '{agentId}' at '{path}'.", agentId, config.ConfigPath);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "MCP agent auto-configure failed; editor load is unaffected. Wire the agent manually in Tools/Feeder/Bridge.");
            }
        }
    }
}
