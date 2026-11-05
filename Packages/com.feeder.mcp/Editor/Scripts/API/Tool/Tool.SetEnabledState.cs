#nullable enable
using Feeder.MCP.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.Reflect.Model;
using Feeder.Reflect.Utils;
using Feeder.MCP.Utils;
using Feeder.MCP.Editor.UI;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Tool
    {
        public const string ToolSetEnabledStateId = "tool-set-enabled-state";
        const string GroupPrefix = "group:";

        static List<string> GroupMembers(IToolManager manager, string group) => (manager.GetAllTools() ?? Enumerable.Empty<IRunTool>())
            .Where(tool => string.Equals(SkillCatalog.GroupName(tool.Title, tool.Name), group, StringComparison.OrdinalIgnoreCase))
            .Select(tool => tool.Name)
            .ToList();

        static IEnumerable<string> GroupNames(IToolManager manager) => (manager.GetAllTools() ?? Enumerable.Empty<IRunTool>())
            .Select(tool => SkillCatalog.GroupName(tool.Title, tool.Name))
            .Distinct();

        [AiTool
        (
            ToolSetEnabledStateId,
            Title = "Tool / Set Enabled State",
            Enabled = false
        )]
        [AiSkillDescription("Enable or disable MCP tools by name in batch. " +
            "Persists the change via `MatrixPluginEditor.Instance.Save()` only when at least one tool actually flipped. " +
            "Returns per-input success flags plus optional operation logs.")]
        [AiSkillBody("Enable or disable MCP tools by name. " +
            "Allows controlling which tools are available for the AI agent.\n\n" +
            "## Inputs\n\n" +
            "- `tools` — array of `ToolToggleInput { Name, Enabled }`. Non-empty. A `Name` of `group:Vision` (or just `Vision` when no tool has that name) switches every tool of that group; `unity-tool-list` shows each tool's `group`.\n" +
            "- `includeLogs` (default `false`) — when true, returns per-step operation logs alongside the success map.\n\n" +
            "## Behavior\n\n" +
            "Each entry is resolved against the tool manager's exact-name and case-insensitive lookups. " +
            "Already-correct state short-circuits as success without writing. The plugin's config is saved once at the " +
            "end iff at least one tool actually changed state.\n\n" +
            "## After enabling\n\n" +
            "The server tells the client its tool list changed, and the client re-lists. If a tool you just enabled " +
            "is still not in your tool list, call it through the server tool `tool-call` with `name` and `arguments`.")]
        [Description("Enable or disable MCP tools by name. " +
            "Allows controlling which tools are available for the AI agent.")]
        public ToolToggleResult SetEnabledState
        (
            [Description("Array of tools with their desired enabled state. Name is a tool name or 'group:<GroupName>' (see 'group' in unity-tool-list).")]
            ToolToggleInput[] tools,

            [Description("Include operation logs in the result. Default: false")]
            bool? includeLogs = false
        )
        {
            if (tools == null || tools.Length == 0)
                throw new ArgumentException(Error.ToolsArrayIsNullOrEmpty());

            return MainThread.Instance.Run(() =>
            {
                var toolManager = MatrixPluginEditor.Instance.Tools
                    ?? throw new InvalidOperationException(Error.ToolManagerNotAvailable());

                var logs = includeLogs == true ? new Logs() : null;
                var success = new Dictionary<string, bool>();
                var changed = false;
                var (exactLookup, caseInsensitiveLookup) = BuildToolLookup(toolManager);

                foreach (var input in tools)
                {
                    if (string.IsNullOrWhiteSpace(input.Name))
                    {
                        var key = input.Name ?? string.Empty;
                        success[key] = false;
                        logs?.Error("Tool name is null or empty.");
                        continue;
                    }

                    var resolvedName = ResolveToolName(exactLookup, caseInsensitiveLookup, input.Name!, null);
                    if (resolvedName == null)
                    {
                        // 'group:Vision', or a bare group name that is not a tool name
                        var groupName = input.Name!.StartsWith(GroupPrefix, StringComparison.OrdinalIgnoreCase) ? input.Name.Substring(GroupPrefix.Length).Trim() : input.Name.Trim();
                        var members = GroupMembers(toolManager, groupName);
                        if (members.Count > 0)
                        {
                            var flipped = 0;
                            foreach (var member in members)
                            {
                                if (toolManager.IsToolEnabled(member) == input.Enabled)
                                    continue;
                                toolManager.SetToolEnabled(member, input.Enabled);
                                flipped++;
                                changed = true;
                            }
                            success[input.Name!] = true;
                            logs?.Info($"Group '{groupName}': {flipped} of {members.Count} tools {(input.Enabled ? "enabled" : "disabled")}.");
                            continue;
                        }

                        ResolveToolName(exactLookup, caseInsensitiveLookup, input.Name!, logs);
                        logs?.Info($"Groups: {string.Join(", ", GroupNames(toolManager))}.");
                        success[input.Name!] = false;
                        continue;
                    }

                    var currentState = toolManager.IsToolEnabled(resolvedName);
                    if (currentState == input.Enabled)
                    {
                        success[input.Name!] = true;
                        logs?.Info($"Tool '{resolvedName}' is already {(input.Enabled ? "enabled" : "disabled")}.");
                        continue;
                    }

                    toolManager.SetToolEnabled(resolvedName, input.Enabled);
                    success[input.Name!] = true;
                    changed = true;
                    logs?.Info($"Tool '{resolvedName}' has been {(input.Enabled ? "enabled" : "disabled")}.");
                }

                if (changed)
                    MatrixPluginEditor.Instance.Save();

                return new ToolToggleResult
                {
                    Logs = logs,
                    Success = success
                };
            });
        }
    }
}
