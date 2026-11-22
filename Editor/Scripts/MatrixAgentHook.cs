#nullable enable
using System;
using System.IO;
using System.Linq;
using Feeder.Json;
using Feeder.Json.Nodes;
using Feeder.MCP.Editor.UI;
using Feeder.ToolHost.AgentConfig;

namespace Feeder.MCP.Editor
{
    /// <summary>
    /// The Claude Code hook that tells the Matrix server which agent of a session makes a tool call, so a subagent
    /// claims the editor for itself instead of sharing its parent's claim. It lives in the project's
    /// .claude/settings.local.json, because the port differs per machine.
    /// </summary>
    internal static class MatrixAgentHook
    {
        const string HookPath = "/hooks/claude";
        const string SpawnTools = "Agent|Task";
        const int TimeoutSeconds = 5;

        static string SettingsPath => Path.Combine(MatrixPluginEditor.ProjectRootPath, ".claude", "settings.local.json");

        // Claude Code only posts hooks to a loopback address when it is written as one
        static string Url => $"http://127.0.0.1:{MatrixPluginEditor.Port}{HookPath}";

        public static bool IsInstalled => Read()?.Contains(HookPath) == true;

        static bool ClaudeCodeIsWired
        {
            get
            {
                var path = Path.Combine(MatrixPluginEditor.ProjectRootPath, ".mcp.json");
                return File.Exists(path) && File.ReadAllText(path).Contains($"\"{AiAgentConfig.DefaultMcpServerName}\"");
            }
        }

        /// <summary>
        /// Keeps the hook in step with the linked server: there while that server tells subagents apart and Claude Code
        /// is wired to this project, gone while the server would answer every hook with an error. Null = not linked, no change.
        /// </summary>
        public static string Sync(bool? serverTellsAgentsApart)
        {
            if (serverTellsAgentsApart == null)
                return "Agents: the Claude Code hook is checked when the server is linked";
            if (serverTellsAgentsApart == false)
                return IsInstalled ? Remove() : "Agents: this server does not tell subagents apart, so no Claude Code hook";
            return IsInstalled || ClaudeCodeIsWired ? Install() : "Agents: Claude Code is not wired to this project, so no hook";
        }

        public static string Install()
        {
            var settings = AgentConfiguratorSettingsFactory.Create();
            var token = settings.IsHttpAuthRequired ? settings.Token : null;
            Write(Apply(Read(), Url, AiAgentConfig.DefaultMcpServerName, token));
            return "Agents: Claude Code reports its subagents to Matrix (.claude/settings.local.json); new sessions pick it up";
        }

        public static string Remove()
        {
            if (!IsInstalled)
                return "Agents: no Claude Code hook to remove";
            Write(Apply(Read(), null, AiAgentConfig.DefaultMcpServerName, null));
            return "Agents: removed the Claude Code hook from .claude/settings.local.json";
        }

        /// <summary>Adds the hook to settings JSON, or takes it out when <paramref name="url"/> is null. Everything else in the file stays.</summary>
        internal static string Apply(string? settings, string? url, string mcpEntry, string? token)
        {
            var root = string.IsNullOrWhiteSpace(settings) ? new JsonObject() : JsonNode.Parse(settings!) as JsonObject
                ?? throw new InvalidOperationException(".claude/settings.local.json is not a JSON object");
            var hooks = root["hooks"] as JsonObject;
            if (hooks != null)
            {
                foreach (var name in hooks.Select(pair => pair.Key).ToArray())
                {
                    if (hooks[name] is not JsonArray groups)
                        continue;
                    for (var i = groups.Count - 1; i >= 0; i--)
                        if (IsOurs(groups[i]))
                            groups.RemoveAt(i);
                    if (groups.Count == 0)
                        hooks.Remove(name);
                }
            }

            if (url != null)
            {
                if (hooks == null)
                    root["hooks"] = hooks = new JsonObject();
                Add(hooks, "PreToolUse", $"mcp__{mcpEntry}__.*|{SpawnTools}", url, token);
                Add(hooks, "PostToolUse", SpawnTools, url, token);
                Add(hooks, "PostToolUseFailure", SpawnTools, url, token);
                Add(hooks, "SubagentStart", null, url, token);
                Add(hooks, "SubagentStop", null, url, token);
            }
            else if (hooks is { Count: 0 })
            {
                root.Remove("hooks");
            }
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        }

        static void Add(JsonObject hooks, string name, string? matcher, string url, string? token)
        {
            var hook = new JsonObject { ["type"] = "http", ["url"] = url, ["timeout"] = TimeoutSeconds };
            if (!string.IsNullOrEmpty(token))
                hook["headers"] = new JsonObject { ["Authorization"] = "Bearer " + token };
            var group = new JsonObject();
            if (matcher != null)
                group["matcher"] = matcher;
            group["hooks"] = new JsonArray(hook);
            if (hooks[name] is JsonArray groups)
                groups.Add(group);
            else
                hooks[name] = new JsonArray(group);
        }

        static bool IsOurs(JsonNode? group)
            => group?["hooks"] is JsonArray list && list.Any(hook =>
                hook?["url"] is JsonValue value && value.TryGetValue<string>(out var url)
                && url.EndsWith(HookPath, StringComparison.Ordinal)
                && (url.StartsWith("http://127.0.0.1:", StringComparison.Ordinal) || url.StartsWith("http://localhost:", StringComparison.Ordinal)));

        static string? Read()
        {
            var path = SettingsPath;
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        static void Write(string text)
        {
            var path = SettingsPath;
            if (File.Exists(path) && File.ReadAllText(path) == text)
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
    }
}
