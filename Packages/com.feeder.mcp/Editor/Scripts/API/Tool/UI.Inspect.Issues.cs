#nullable enable
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Feeder.MCP.Data;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_UI
    {
        public const string UiInspectIssuesToolId = "ui-inspect-issues";

        [AiTool
        (
            UiInspectIssuesToolId,
            Title = "UI / Inspect Issues",
            ReadOnlyHint = true
        )]
        [AiSkillDescription("Automatically scan the UI Toolkit tree of one or all open EditorWindows for layout defects: " +
            "clipped text (measured vs available size), children overflowing their parent, zero-size elements with " +
            "content. Returns a structured issue list with index paths ready for '" + UiInspectElementToolId + "'.")]
        [AiSkillBody("One-shot layout linter for editor windows — finds the problems a human would spot on a screenshot.\n\n" +
            "## Detections\n\n" +
            "- `TEXT-CLIPPED-X/Y` — text measured size exceeds the element's content rect (wrap and nowrap aware, notes ellipsis).\n" +
            "- `OVERFLOWS-PARENT` — element's world rect extends past its parent (ScrollView content is excluded); reports " +
            "whether the parent clips it or it spills over siblings.\n" +
            "- `ZERO-SIZE` — visible element with children or text but ~0 width/height.\n\n" +
            "## Inputs\n\n" +
            "- `windowName` — optional; empty scans every open window that has a UI Toolkit tree.\n\n" +
            "display:none subtrees are skipped (their layout is not meaningful). Issue count is capped at 200.")]
        [Description("Scans the UI Toolkit element tree of one EditorWindow (or all open windows when windowName is empty) " +
            "for layout problems: text clipped horizontally/vertically vs its measured size, elements overflowing their " +
            "parent rect (ScrollView content excluded), and zero-size elements that have content. " +
            "Returns a structured list with element index paths usable in 'ui-inspect-element' and 'ui-inspect-tree'.")]
        public UiLayoutIssuesResult InspectIssues
        (
            [Description("Optional EditorWindow class name, full type name, or tab title. Empty scans all open windows with a UI Toolkit tree.")]
            string? windowName = null
        )
        {
            return MainThread.Instance.Run(() =>
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
                    var window = FindWindowByName(windowName);
                    if (window.rootVisualElement == null || window.rootVisualElement.panel == null)
                        notes.Add($"Window '{window.GetType().Name}' has no live UI Toolkit panel.");
                    else
                        targets.Add(window);
                }

                var issues = new List<UiLayoutIssue>();
                var scanned = 0;

                foreach (var window in targets)
                {
                    var root = window.rootVisualElement;
                    var label = window.GetType().Name;
                    Scan(root, root, label, issues, ref scanned);
                    if (issues.Count >= MaxIssues)
                    {
                        notes.Add($"Issue cap of {MaxIssues} reached — remaining windows/elements were not fully scanned.");
                        break;
                    }
                }

                return new UiLayoutIssuesResult
                {
                    Issues = issues.ToArray(),
                    WindowsScanned = targets.Select(w => w.GetType().Name).ToArray(),
                    ElementsScanned = scanned,
                    Note = notes.Count == 0 ? null : string.Join(" ", notes)
                };
            });
        }

        private static void Scan(VisualElement element, VisualElement root, string windowLabel,
            List<UiLayoutIssue> issues, ref int scanned)
        {
            if (issues.Count >= MaxIssues)
                return;
            if (element.resolvedStyle.display == DisplayStyle.None)
                return;

            scanned++;

            foreach (var issue in DetectIssues(element))
            {
                issues.Add(new UiLayoutIssue
                {
                    Window = windowLabel,
                    IndexPath = GetIndexPath(element, root),
                    Element = DescribeShort(element),
                    Issue = issue,
                    Text = element is TextElement te && !string.IsNullOrEmpty(te.text)
                        ? TruncateText(te.text, TextPreviewLength)
                        : null,
                    Rect = FormatRect(element.worldBound)
                });
                if (issues.Count >= MaxIssues)
                    return;
            }

            for (var i = 0; i < element.hierarchy.childCount; i++)
                Scan(element.hierarchy[i], root, windowLabel, issues, ref scanned);
        }
    }
}
