#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_UI
    {
        public const string UiInspectTreeToolId = "ui-inspect-tree";

        [AiTool
        (
            UiInspectTreeToolId,
            Title = "UI / Inspect Tree",
            ReadOnlyHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Dump the UI Toolkit element tree of an open EditorWindow as indented text — one line per " +
            "element with index path, type, #name, .classes, world rect, text preview, and inline layout-issue flags " +
            "(TEXT-CLIPPED, OVERFLOWS-PARENT, ZERO-SIZE). With an empty `windowName` it lists the open windows; " +
            "with `onlyIssues` it returns just the layout problems. The DOM-inspector replacement for screenshots.")]
        [AiSkillBody("Dumps the live element tree of an EditorWindow, similar to a web DevTools DOM view.\n\n" +
            "## Inputs\n\n" +
            "- `windowName` — EditorWindow class name, full type name, or tab title. Empty (without `onlyIssues`) lists every open window: type, title, rect, focus, dock state and `RootChildCount` (0 = pure IMGUI, no tree to read).\n" +
            "- `startElement` — optional subtree start: '#name', '.class', or index path '0.2.1'. Empty = window root.\n" +
            "- `maxDepth` — recursion cap, default 12.\n" +
            "- `includeHidden` — when true also recurses into display:none subtrees.\n" +
            "- `maxLines` — output cap, default 400, hard limit 3000.\n" +
            "- `onlyIssues` — when true returns only layout defects (TEXT-CLIPPED-X/Y, OVERFLOWS-PARENT, ZERO-SIZE; display:none subtrees skipped, capped at 200) for the window, or for every window with a UI Toolkit tree when `windowName` is empty.\n\n" +
            "## Output format\n\n" +
            "`[indexPath] Type #name .classes (x= y= w= h=) \"text\" !ISSUE-FLAGS` — pass the index path (or '#name') " +
            "to '" + UiInspectElementToolId + "' for full resolved styles.")]
        [Description("Dumps the UI Toolkit element tree of an open EditorWindow as indented text, one element per line: " +
            "[indexPath] Type #name .classes (world rect) \"text preview\" plus automatic issue flags " +
            "(TEXT-CLIPPED-X/Y, OVERFLOWS-PARENT, ZERO-SIZE, display:none). " +
            "Empty windowName lists the open windows; onlyIssues returns just the layout problems. " +
            "Element names/classes map directly to the UXML/USS source files.")]
        public string InspectTree
        (
            [Description("EditorWindow class name, full type name, or tab title. Empty lists the open windows (or, with onlyIssues, scans all windows).")]
            string? windowName = null,
            [Description("Optional subtree start: '#elementName', '.className', or hierarchy index path like '0.2.1'. Empty starts at the window root.")]
            string? startElement = null,
            [Description("Maximum recursion depth below the start element. Default 12.")]
            int maxDepth = 12,
            [Description("When true, also recurses into display:none subtrees (hidden tabs/panes). Default false.")]
            bool includeHidden = false,
            [Description("Maximum output lines. Default 400, hard limit 3000.")]
            int maxLines = DefaultTreeLines,
            [Description("When true, return only layout issues instead of the tree. Default false.")]
            bool onlyIssues = false
        )
        {
            return MainThread.Instance.Run(() =>
            {
                if (onlyIssues)
                    return ScanIssues(windowName);
                if (string.IsNullOrWhiteSpace(windowName))
                    return ListWindows();

                var window = FindWindowByName(windowName!);
                var root = window.rootVisualElement;
                if (root == null || root.panel == null)
                    return $"[Error] Window '{window.GetType().Name}' has no live UI Toolkit panel. "
                        + "If it is a pure-IMGUI window its content cannot be traversed.";

                var start = ResolveElement(root, startElement);
                var limit = Math.Max(1, Math.Min(maxLines, MaxTreeLines));

                var sb = new StringBuilder();
                sb.Append("Window: ").Append(window.GetType().FullName)
                    .Append(" (\"").Append(window.titleContent?.text).Append("\") ")
                    .Append(FormatRect(window.position)).AppendLine();
                sb.AppendLine("Format: [indexPath] Type #name .classes (world rect) \"text\" !ISSUES");
                sb.AppendLine("Use '" + UiInspectElementToolId + "' with elementQuery='<indexPath>' or '#name' for full styles.");
                sb.AppendLine();

                var lines = 0;
                AppendTree(sb, start, root, 0, maxDepth, includeHidden, limit, ref lines);
                if (lines >= limit)
                    sb.AppendLine($"… output truncated at {limit} lines — narrow with 'startElement' or lower 'maxDepth', or raise 'maxLines' (max {MaxTreeLines}).");

                return sb.ToString();
            });
        }

        static string ListWindows()
        {
            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>()
                .Where(w => w != null)
                .OrderByDescending(w => w.rootVisualElement?.hierarchy.childCount ?? 0)
                .ToArray();

            var sb = new StringBuilder();
            sb.Append("Open windows: ").Append(windows.Length)
                .AppendLine(" (pass a type name or title as 'windowName'; RootChildCount 0 = pure IMGUI)");
            foreach (var w in windows)
                sb.Append(w.GetType().FullName).Append(" \"").Append(w.titleContent?.text).Append("\" ")
                    .Append(FormatRect(w.position))
                    .Append(" focused=").Append(w == EditorWindow.focusedWindow)
                    .Append(" docked=").Append(w.docked)
                    .Append(" RootChildCount=").Append(w.rootVisualElement?.hierarchy.childCount ?? 0).AppendLine();
            return sb.ToString();
        }

        static string ScanIssues(string? windowName)
        {
            var targets = new List<EditorWindow>();
            var notes = new List<string>();

            if (string.IsNullOrWhiteSpace(windowName))
            {
                targets.AddRange(Resources.FindObjectsOfTypeAll<EditorWindow>()
                    .Where(w => w != null
                        && w.rootVisualElement != null
                        && w.rootVisualElement.panel != null
                        && w.rootVisualElement.hierarchy.childCount > 0));
                if (targets.Count == 0)
                    notes.Add("No open windows with a UI Toolkit tree were found.");
            }
            else
            {
                var window = FindWindowByName(windowName!);
                if (window.rootVisualElement == null || window.rootVisualElement.panel == null)
                    notes.Add($"Window '{window.GetType().Name}' has no live UI Toolkit panel.");
                else
                    targets.Add(window);
            }

            var issues = new List<string>();
            var scanned = 0;
            foreach (var window in targets)
            {
                var root = window.rootVisualElement;
                Scan(root, root, window.GetType().Name, issues, ref scanned);
                if (issues.Count >= MaxIssues)
                {
                    notes.Add($"Issue cap of {MaxIssues} reached — remaining windows/elements were not fully scanned.");
                    break;
                }
            }

            var sb = new StringBuilder();
            sb.Append("Issues: ").Append(issues.Count).Append(" in ").Append(scanned).Append(" elements of ")
                .Append(targets.Count == 0 ? "no windows" : string.Join(", ", targets.Select(w => w.GetType().Name))).AppendLine();
            foreach (var note in notes)
                sb.AppendLine(note);
            foreach (var issue in issues)
                sb.AppendLine(issue);
            return sb.ToString();
        }

        static void Scan(VisualElement element, VisualElement root, string windowLabel, List<string> issues, ref int scanned)
        {
            if (issues.Count >= MaxIssues || element.resolvedStyle.display == DisplayStyle.None)
                return;

            scanned++;

            foreach (var issue in DetectIssues(element))
            {
                var text = element is TextElement te && !string.IsNullOrEmpty(te.text)
                    ? $" \"{TruncateText(te.text, TextPreviewLength)}\""
                    : string.Empty;
                issues.Add($"{windowLabel} [{GetIndexPath(element, root)}] {DescribeShort(element)} ({FormatRect(element.worldBound)}){text} !{issue}");
                if (issues.Count >= MaxIssues)
                    return;
            }

            for (var i = 0; i < element.hierarchy.childCount; i++)
                Scan(element.hierarchy[i], root, windowLabel, issues, ref scanned);
        }
    }
}
