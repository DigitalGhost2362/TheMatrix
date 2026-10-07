#nullable enable
using System;
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Console
    {
        public const string ConsoleClearLogsToolId = "console-clear-logs";
        [AiTool
        (
            ConsoleClearLogsToolId,
            Title = "Console / Clear Logs",
            Enabled = false,
            DestructiveHint = true,
            IdempotentHint = true
        )]
        [AiSkillDescription("Clear the MCP log cache (used by '" + ConsoleGetLogsToolId + "') and the Unity Editor Console window. " +
            "Useful for isolating logs to a specific action by clearing the slate first.")]
        [AiSkillBody("Clears the MCP log cache (used by console-get-logs) and the Unity Editor Console window. " +
            "Useful for isolating errors related to a specific action by clearing logs before performing the action.\n\n" +
            "## Behavior\n\n" +
            "Calls `Debug.ClearDeveloperConsole()` to wipe the Editor Console, then clears the MCP-side `LogCollector` " +
            "cache so subsequent '" + ConsoleGetLogsToolId + "' calls only see new entries.")]
        [Description("Clears the MCP log cache (used by console-get-logs) and the Unity Editor Console window. " +
            "Useful for isolating errors related to a specific action by clearing logs before performing the action.")]
        public void ClearLogs(string? nothing = null)
        {
            MainThread.Instance.Run(() =>
            {
                Debug.ClearDeveloperConsole();

                if (!MatrixPluginEditor.HasInstance)
                    throw new InvalidOperationException("MatrixPluginEditor is not initialized.");

                var logCollector = MatrixPluginEditor.Instance.LogCollector;
                if (logCollector == null)
                    throw new InvalidOperationException("LogCollector is not initialized.");

                logCollector.Clear();
            });
        }
    }
}
