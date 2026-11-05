#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;
using Feeder.Reflect.Utils;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Menu
    {
        public const string MenuExecuteToolId = "menu-execute";
        static readonly string[] RootMenus = { "File", "Edit", "Assets", "GameObject", "Component", "Window", "Help", "Tools" };

        [AiTool
        (
            MenuExecuteToolId,
            Title = "Menu / Execute",
            Enabled = false
        )]
        [AiSkillDescription("Run an Editor menu item by its path (for example 'Assets/Refresh' or 'Window/General/Console'), " +
            "or list the menu paths that contain a text. Fails with a clear error when the path does not exist or the item is disabled.")]
        [AiSkillBody("Runs `EditorApplication.ExecuteMenuItem` for a menu path, or lists menu paths.\n\n" +
            "## Inputs\n\n" +
            "- `path` — full menu path, e.g. `Window/General/Console`. When set, the item is executed.\n" +
            "- `filter` — used when `path` is empty: returns the menu paths that contain this text (case-insensitive). " +
            "Empty filter lists every path, capped by `maxResults`.\n" +
            "- `maxResults` — cap for the list (default 200).\n\n" +
            "## Notes\n\n" +
            "- Unity returns `false` for a missing path and for an item that is disabled in the current context; the error says which one it can tell.\n" +
            "- Items that open a modal dialog block the Editor until a person closes it; avoid them.\n" +
            "- The list comes from Unity's internal menu table and may be empty on a Unity version that does not expose it; " +
            "execution still works.")]
        [Description("Run an Editor menu item by path, or list menu paths containing a filter text.")]
        public ResponseCallTool Execute
        (
            [Description("Menu path to execute, e.g. 'Window/General/Console'. Leave empty to list instead.")]
            string? path = null,
            [Description("List mode (when path is empty): menu paths containing this text, case-insensitive.")]
            string? filter = null,
            [Description("Maximum number of paths returned in list mode.")]
            int maxResults = 200
        )
        {
            try { return ResponseCallTool.Success(Run(path, filter, maxResults)); }
            catch (Exception e) { return ResponseCallTool.Error((e is AggregateException a ? a.InnerException ?? e : e).Message); }
        }

        internal static string Run(string? path, string? filter = null, int maxResults = 200)
        {
            return MainThread.Instance.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(path))
                    return List(filter, maxResults);

                var trimmed = path!.Trim();
                if (Exists(trimmed) == false)
                    throw new InvalidOperationException($"Menu item '{trimmed}' does not exist. Use this tool with 'filter' to list paths.");
                if (EditorApplication.ExecuteMenuItem(trimmed))
                    return $"Executed menu item '{trimmed}'.";

                throw new InvalidOperationException(Exists(trimmed) switch
                {
                    false => $"Menu item '{trimmed}' does not exist. Use this tool with 'filter' to list paths.",
                    true => $"Menu item '{trimmed}' exists but was not executed: it is disabled in the current context.",
                    _ => $"Menu item '{trimmed}' was not executed: the path does not exist or the item is disabled."
                });
            });
        }

        static readonly MethodInfo? ExistsMethod = typeof(Menu).GetMethod("MenuItemExists", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        static readonly MethodInfo? ItemsMethod = typeof(Menu).GetMethod("GetMenuItems", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        static bool? Exists(string path)
        {
            try { return ExistsMethod?.Invoke(null, new object[] { path }) as bool?; }
            catch { return null; }
        }

        internal static string List(string? filter, int maxResults)
        {
            var paths = MenuPaths();
            if (paths == null)
                throw new NotSupportedException("This Unity version does not expose the menu table; pass 'path' to execute an item.");

            var matches = Filter(paths, filter);
            var shown = matches.Take(Math.Max(1, maxResults)).ToList();
            return $"{matches.Count} menu path(s){(string.IsNullOrWhiteSpace(filter) ? string.Empty : $" containing '{filter}'")}" +
                (shown.Count < matches.Count ? $", showing {shown.Count}" : string.Empty) + ":\n" + string.Join("\n", shown);
        }

        internal static List<string> Filter(IEnumerable<string> paths, string? filter)
            => paths.Where(p => !string.IsNullOrWhiteSpace(p) && (string.IsNullOrWhiteSpace(filter) || p.IndexOf(filter!.Trim(), StringComparison.OrdinalIgnoreCase) >= 0))
                .Distinct().OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

        static List<string>? MenuPaths()
        {
            if (ItemsMethod == null)
                return null;
            try
            {
                var result = new List<string>();
                foreach (var root in new[] { string.Empty }.Concat(RootMenus))
                {
                    if (ItemsMethod.Invoke(null, new object[] { root, false, false }) is not Array items)
                        continue;
                    foreach (var item in items)
                    {
                        var property = item?.GetType().GetProperty("path", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (property?.GetValue(item) is string itemPath)
                            result.Add(itemPath);
                    }
                }
                return result;
            }
            catch
            {
                return null;
            }
        }
    }
}
