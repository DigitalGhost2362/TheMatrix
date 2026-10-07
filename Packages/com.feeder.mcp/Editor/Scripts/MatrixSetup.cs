#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Feeder.Json.Nodes;
using Feeder.MCP.Editor.MatrixSpace;
using Feeder.MCP.Editor.MatrixSpace.Setup;
using Feeder.MCP.Editor.UI;
using Feeder.ToolHost;
using Feeder.ToolHost.AgentConfig;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor
{
    public static class MatrixSetup
    {
        const string Title = "Matrix Setup";
        const string LogPrefix = "[Matrix Setup]";
        const string ClaudePresetId = "claude";
        const string IgnoreHeader = "# Matrix MCP: per-machine files (the port derives from the project path)";

        const string ConfirmMessage =
            "Turn Matrix MCP on for this project and prepare this machine:\n\n" +
            "- Start the local server and connect the editor (Matrix does nothing until this runs)\n" +
            "- Ignore .mcp.json and .codex/config.toml in .gitignore (their port differs per machine)\n" +
            "- Keep only the core MCP tools enabled to save tokens; tool-set-enabled-state turns more on when needed\n" +
            "- Write the Claude Code MCP config, then install or verify the Claude Code CLI (Node.js is installed with winget if missing)";

        static readonly string[] MachineSpecificFiles = { ".mcp.json", ".codex/config.toml" };

        internal static readonly string[] CoreTools =
        {
            "unity-tool-list",
            "tool-set-enabled-state",
            "gameobject-find",
            "gameobject-component-get",
            "scene-list-opened",
            "scene-get-data",
            "console-get-logs",
            "assets-find",
            "editor-application-get-state",
            "screenshot-game-view",
        };

        public static void Run()
        {
            if (!EditorUtility.DisplayDialog(Title, ConfirmMessage, "Run", "Cancel"))
                return;

            MatrixActivation.Enable();

            var message = string.Join("\n\n", new[]
            {
                Step("Git", IgnoreMachineSpecificFiles),
                Step("Tools", () => ApplyToolSet(all: false)),
                Step("Claude Code", StartClaudeSetup),
            });

            Debug.Log($"{LogPrefix}\n{message}");
            NotificationPopupWindow.Show(
                windowTitle: Title,
                title: "Setup finished",
                message: message,
                width: 480,
                minWidth: 480,
                height: 340,
                minHeight: 340);
        }

        internal static string BuildIgnoreAppend(string existing, IEnumerable<string> files, out int added)
        {
            var present = new HashSet<string>(existing.Split('\n').Select(line => line.Trim()));
            var missing = files.Where(file => !present.Contains(file) && !present.Contains("/" + file)).ToList();
            added = missing.Count;
            if (added == 0)
                return string.Empty;

            var eol = existing.Contains("\r\n") ? "\r\n" : "\n";
            var text = new StringBuilder();
            if (existing.Length > 0)
            {
                if (!existing.EndsWith("\n"))
                    text.Append(eol);
                text.Append(eol);
            }

            text.Append(IgnoreHeader).Append(eol);
            foreach (var file in missing)
                text.Append('/').Append(file).Append(eol);
            return text.ToString();
        }

        static string Step(string name, Func<string> run)
        {
            try
            {
                return run();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return $"{name}: FAILED - {ex.Message}";
            }
        }

        static string IgnoreMachineSpecificFiles()
        {
            var root = MatrixPluginEditor.ProjectRootPath;
            var path = Path.Combine(root, ".gitignore");
            var existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            var tail = BuildIgnoreAppend(existing, MachineSpecificFiles, out var added);
            if (added > 0)
                File.AppendAllText(path, tail);

            var lines = new List<string>
            {
                added > 0
                    ? $"Git: added {added} line(s) to .gitignore"
                    : "Git: .gitignore already ignores the machine-specific files",
            };
            lines.AddRange(MachineSpecificFiles
                .Where(file => IsTracked(root, file))
                .Select(file => TrackedAdvice(root, file)));
            return string.Join("\n", lines);
        }

        // removing a shared file from the index would delete it from every teammate's checkout on their next pull
        static string TrackedAdvice(string root, string file)
            => file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && HoldsOnlyOurServer(Path.Combine(root, file))
                ? $"Git: {file} is still tracked - run: git rm --cached {file}"
                : $"Git: {file} is tracked and may hold other tools' entries - keep it tracked and run: git update-index --skip-worktree {file}";

        static bool HoldsOnlyOurServer(string path)
        {
            try
            {
                var servers = JsonNode.Parse(File.ReadAllText(path))?["mcpServers"]?.AsObject();
                return servers != null && servers.All(entry => entry.Key == AiAgentConfig.DefaultMcpServerName);
            }
            catch (Exception)
            {
                return false;
            }
        }

        static bool IsTracked(string root, string file)
        {
            var git = SetupCommandRunner.Find("git");
            return git != null
                && SetupCommandRunner.Run(git, $"-C \"{root}\" ls-files --error-unmatch -- \"{file}\"", _ => { }, 10000) == 0;
        }

        internal static string ApplyToolSet(bool all)
        {
            var manager = MatrixPluginEditor.Instance.Tools;
            if (manager == null)
                return "Tools: the MCP plugin is not running - open Tools > Feeder > Bridge, then run Setup again";

            var tools = manager.GetAllTools().Where(tool => tool.Name != null).ToList();
            var registered = new HashSet<string>(tools.Select(tool => tool.Name!), StringComparer.OrdinalIgnoreCase);
            foreach (var name in CoreTools.Where(name => !registered.Contains(name)))
                Debug.LogError($"{LogPrefix} core tool '{name}' is not registered");

            var tokensBefore = EnabledTokens(manager, tools);
            var changed = 0;
            foreach (var tool in tools)
            {
                var enable = all || CoreTools.Contains(tool.Name!, StringComparer.OrdinalIgnoreCase);
                if (manager.IsToolEnabled(tool.Name!) == enable)
                    continue;

                manager.SetToolEnabled(tool.Name!, enable);
                changed++;
            }

            if (changed > 0)
                MatrixPluginEditor.Instance.Save();

            var label = all ? $"all {tools.Count} tools" : $"{CoreTools.Length} core tools";
            return $"Tools: {label} enabled, {changed} changed, about {tokensBefore} -> {EnabledTokens(manager, tools)} tokens per request";
        }

        static int EnabledTokens(IToolManager manager, IEnumerable<IRunTool> tools)
            => tools.Where(tool => manager.IsToolEnabled(tool.Name!)).Sum(tool => tool.TokenCount);

        static string StartClaudeSetup()
        {
            if (AgentSetupService.IsAnyRunning)
                return "Claude Code: another agent setup is running - run Setup again when it finishes";

            AgentSetupService.RunSetup(AgentBackendCatalog.Get(ClaudePresetId));
            return "Claude Code: MCP config, CLI check and verify are running - progress is in the Console and Matrix Space > CONFIG > AGENTS";
        }
    }
}
