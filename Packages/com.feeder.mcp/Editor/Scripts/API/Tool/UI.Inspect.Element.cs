#nullable enable
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_UI
    {
        public const string UiInspectElementToolId = "ui-inspect-element";

        [AiTool
        (
            UiInspectElementToolId,
            Title = "UI / Inspect Element",
            ReadOnlyHint = true
        )]
        [AiSkillDescription("Full DevTools-style detail of one visual element in an open EditorWindow: rects, measured " +
            "text size, resolved styles (flex, size, margin/padding, overflow, whiteSpace, fontSize…), detected layout " +
            "issues, ancestor chain, children, and the style sheets in scope for USS source mapping.")]
        [AiSkillBody("Returns everything about a single element that a web 'Inspect element' panel would show.\n\n" +
            "## Inputs\n\n" +
            "- `windowName` — EditorWindow class name, full type name, or tab title.\n" +
            "- `elementQuery` — '#name', '.class' (first match), or hierarchy index path like '0.2.1' " +
            "(as printed by '" + UiInspectTreeToolId + "'). Empty inspects the window root.\n\n" +
            "## Output\n\n" +
            "Text block with world/content rects, unconstrained measured text size, detected issues, the full resolved " +
            "style set relevant to layout, the ancestor chain with rects, direct children, and the StyleSheet assets in " +
            "scope — element names/classes map directly to the UXML/USS files under the package UI folder.")]
        [Description("Returns full detail for one visual element of an open EditorWindow (like web 'Inspect element'): " +
            "world/content rect, measured text size, detected layout issues, resolved styles " +
            "(display, position, size, flex, margin/padding/border, overflow, whiteSpace, textOverflow, fontSize), " +
            "ancestor chain, children, and style sheets in scope. " +
            "elementQuery accepts '#name', '.class', or an index path like '0.2.1' from 'ui-inspect-tree'.")]
        public string InspectElement
        (
            [Description("EditorWindow class name, full type name, or tab title. Use 'ui-inspect-windows' to list them.")]
            string windowName,
            [Description("Element to inspect: '#elementName', '.className', or hierarchy index path like '0.2.1'. Empty inspects the window root.")]
            string? elementQuery = null
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var window = FindWindowByName(windowName);
                var root = window.rootVisualElement;
                if (root == null || root.panel == null)
                    return $"[Error] Window '{window.GetType().Name}' has no live UI Toolkit panel.";

                var element = ResolveElement(root, elementQuery);
                return BuildElementDetail(window, element, root);
            });
        }
    }
}
