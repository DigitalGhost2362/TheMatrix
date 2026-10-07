#nullable enable
using System.ComponentModel;
using System.Text;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_UI
    {
        public const string UiInspectTreeToolId = "ui-inspect-tree";

        [AiTool
        (
            UiInspectTreeToolId,
            Title = "UI / Inspect Tree",
            ReadOnlyHint = true
        )]
        [AiSkillDescription("Dump the UI Toolkit element tree of an open EditorWindow as indented text — one line per " +
            "element with index path, type, #name, .classes, world rect, text preview, and inline layout-issue flags " +
            "(TEXT-CLIPPED, OVERFLOWS-PARENT, ZERO-SIZE). The DOM-inspector replacement for screenshots.")]
        [AiSkillBody("Dumps the live element tree of an EditorWindow, similar to a web DevTools DOM view.\n\n" +
            "## Inputs\n\n" +
            "- `windowName` — EditorWindow class name, full type name, or tab title. Use '" + UiInspectWindowsToolId + "' to discover.\n" +
            "- `startElement` — optional subtree start: '#name', '.class', or index path '0.2.1'. Empty = window root.\n" +
            "- `maxDepth` — recursion cap, default 12.\n" +
            "- `includeHidden` — when true also recurses into display:none subtrees.\n\n" +
            "## Output format\n\n" +
            "`[indexPath] Type #name .classes (x= y= w= h=) \"text\" !ISSUE-FLAGS` — pass the index path (or '#name') " +
            "to '" + UiInspectElementToolId + "' for full resolved styles.")]
        [Description("Dumps the UI Toolkit element tree of an open EditorWindow as indented text, one element per line: " +
            "[indexPath] Type #name .classes (world rect) \"text preview\" plus automatic issue flags " +
            "(TEXT-CLIPPED-X/Y, OVERFLOWS-PARENT, ZERO-SIZE, display:none). " +
            "Element names/classes map directly to the UXML/USS source files.")]
        public string InspectTree
        (
            [Description("EditorWindow class name, full type name, or tab title. Use 'ui-inspect-windows' to list them.")]
            string windowName,
            [Description("Optional subtree start: '#elementName', '.className', or hierarchy index path like '0.2.1'. Empty starts at the window root.")]
            string? startElement = null,
            [Description("Maximum recursion depth below the start element. Default 12.")]
            int maxDepth = 12,
            [Description("When true, also recurses into display:none subtrees (hidden tabs/panes). Default false.")]
            bool includeHidden = false
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var window = FindWindowByName(windowName);
                var root = window.rootVisualElement;
                if (root == null || root.panel == null)
                    return $"[Error] Window '{window.GetType().Name}' has no live UI Toolkit panel. "
                        + "If it is a pure-IMGUI window its content cannot be traversed.";

                var start = ResolveElement(root, startElement);

                var sb = new StringBuilder();
                sb.Append("Window: ").Append(window.GetType().FullName)
                    .Append(" (\"").Append(window.titleContent?.text).Append("\") ")
                    .Append(FormatRect(window.position)).AppendLine();
                sb.AppendLine("Format: [indexPath] Type #name .classes (world rect) \"text\" !ISSUES");
                sb.AppendLine("Use '" + UiInspectElementToolId + "' with elementQuery='<indexPath>' or '#name' for full styles.");
                sb.AppendLine();

                var lines = 0;
                AppendTree(sb, start, root, 0, maxDepth, includeHidden, ref lines);
                if (lines >= MaxTreeLines)
                    sb.AppendLine($"… output truncated at {MaxTreeLines} lines — narrow with 'startElement' or lower 'maxDepth'.");

                return sb.ToString();
            });
        }
    }
}
