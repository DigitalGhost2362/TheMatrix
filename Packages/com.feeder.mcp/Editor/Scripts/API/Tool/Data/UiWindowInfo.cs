#nullable enable
using System.ComponentModel;

namespace Feeder.MCP.Data
{
    public class UiWindowInfo
    {
        [Description("Full type name of the EditorWindow class. Use it (or the short class name) as 'windowName' in the other ui-inspect tools.")]
        public string? TypeName { get; set; }

        [Description("Window title (tab text).")]
        public string? Title { get; set; }

        [Description("Window position X on the desktop.")]
        public float X { get; set; }

        [Description("Window position Y on the desktop.")]
        public float Y { get; set; }

        [Description("Window width in pixels.")]
        public float Width { get; set; }

        [Description("Window height in pixels.")]
        public float Height { get; set; }

        [Description("True when this window currently has keyboard focus.")]
        public bool Focused { get; set; }

        [Description("True when the window is docked in the editor layout.")]
        public bool Docked { get; set; }

        [Description("Number of direct children under rootVisualElement. 0 usually means a pure-IMGUI window whose content the UI inspector cannot traverse.")]
        public int RootChildCount { get; set; }
    }
}
