#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Feeder.Json.Nodes;
using Feeder.MCP.Editor.UI;
using Feeder.ToolHost;
using Feeder.ToolHost.AgentConfig;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Feeder.MCP.Editor
{
    public static class MatrixSetup
    {
        const string Title = "Matrix Setup";
        const string LogPrefix = "[Matrix Setup]";
        const string IgnoreHeader = "# Matrix MCP: per-machine files (the port derives from the project path)";

        static string ConfirmMessage =>
            "Turn Matrix MCP on for this project and prepare this machine:\n\n" +
            "- Start the local server and connect the editor (Matrix does nothing until this runs)\n" +
            "- Ignore .mcp.json and .codex/config.toml in .gitignore (their port differs per machine)\n" +
            "- Keep only the core MCP tools enabled to save tokens; tool-set-enabled-state turns more on when needed\n" +
            "- Regenerate the agent skills\n\n" +
            "Change these editor settings so agents can share the editor:\n- " +
            string.Join("\n- ", MatrixEditorSettings.Prefs.Select(pref => pref.label).Append(MatrixEditorSettings.RunInBackgroundLabel)) + "\n\n" +
            "The Preferences apply to every Unity project on this machine. With Auto Refresh off, press Ctrl+R after editing files outside Unity.\n\n" +
            "The values from before the first Setup are kept: Tools > Feeder > Matrix Bridge > Revert Setups... puts them back.";

        static string RevertMessage =>
            "Undo Matrix Setup on this machine and project:\n\n" +
            "- Put back the values these settings had before the first Setup:\n  " +
            string.Join("\n  ", MatrixEditorSettings.Prefs.Select(pref => pref.label.Split(':')[0]).Append("Player Settings > Run In Background")) + "\n" +
            "- Remove the lines Setup added to .gitignore\n" +
            "- Restore which MCP tools were enabled\n" +
            "- Turn Matrix off: stop the local server, disconnect the editor, turn Hyper Testing Mode off\n" +
            "- Close every Matrix window\n\n" +
            "Client configs (.mcp.json and similar) and generated skill files stay.";

        static readonly string[] MachineSpecificFiles = { ".mcp.json", ".codex/config.toml" };

        internal static readonly string[] CoreTools =
        {
            "unity-tool-list",
            "tool-set-enabled-state",
            "rql-query",
            "rql-schema",
            "console-get-logs",
            "editor-application-get-state",
            "screenshot-game-view",
            "assets-refresh",
        };

        public static void Run()
        {
            if (!EditorUtility.DisplayDialog(Title, ConfirmMessage, "Run", "Cancel"))
                return;

            MatrixActivation.Enable();
            MatrixEditorSettings.CaptureIfMissing();

            var message = string.Join("\n\n", new[]
            {
                Step("Editor settings", MatrixEditorSettings.Apply),
                Step("Git", IgnoreMachineSpecificFiles),
                Step("Tools", () => ApplyToolSet(all: false)),
                Step("Skills", () => $"Skills: regenerated for {BridgeAgents.RegenerateSkills()} client(s)"),
                $"Next: ask your agent to run /{API.Skill_SetupChecklist.SkillId} (faster play mode, Windows Defender exclusion).",
            });

            Debug.Log($"{LogPrefix}\n{message}");
            NotificationPopupWindow.Show(
                windowTitle: Title,
                title: "Setup finished",
                message: message,
                width: 480,
                minWidth: 480,
                height: 460,
                minHeight: 460);
        }

        public static void Revert()
        {
            if (!EditorUtility.DisplayDialog(Title, RevertMessage, "Revert", "Cancel"))
                return;

            var message = string.Join("\n\n", new[]
            {
                Step("Settings", MatrixEditorSettings.Revert),
                Step("Matrix", TurnOff),
            });

            Debug.Log($"{LogPrefix} reverted\n{message}");
            CloseMatrixWindows();
            EditorUtility.DisplayDialog(Title, "Setup reverted\n\n" + message, "OK");
        }

        static string TurnOff()
        {
            if (HyperTesting.HyperTestingActivation.IsEnabled)
                HyperTesting.HyperTestingActivation.Disable();
            MatrixActivation.Disable();
            return "Matrix: off, server stopped, editor disconnected";
        }

        static void CloseMatrixWindows()
        {
            var assembly = typeof(MatrixSetup).Assembly;
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>().Where(window => window.GetType().Assembly == assembly))
                window.Close();
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
            {
                File.AppendAllText(path, tail);
                MatrixEditorSettings.RecordGitignoreAppend(tail);
            }

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
            try
            {
                using var git = Process.Start(new ProcessStartInfo("git", $"-C \"{root}\" ls-files --error-unmatch -- \"{file}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
                if (git == null)
                    return false;
                git.StandardOutput.ReadToEnd();
                git.StandardError.ReadToEnd();
                return git.WaitForExit(10000) && git.ExitCode == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static string ApplyToolSet(bool all)
        {
            var manager = MatrixPluginEditor.Instance.Tools;
            if (manager == null)
                return "Tools: the MCP plugin is not running - open Tools > Feeder > Matrix Bridge > Bridge, then run Setup again";

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
            => tools.Where(tool => manager.IsToolEnabled(tool.Name!)).Sum(tool => tool.TokenCount);    }
}
