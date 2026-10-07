#nullable enable
using System.ComponentModel;

namespace Feeder.MCP.Data
{
    public class UiLayoutIssue
    {
        [Description("Short class name of the EditorWindow the element belongs to.")]
        public string? Window { get; set; }

        [Description("Hierarchy index path of the element (e.g. '0.2.1'). Pass it as 'elementQuery' to 'ui-inspect-element' for full styles.")]
        public string? IndexPath { get; set; }

        [Description("Element description: Type #name .classes — search these identifiers in the UXML/USS source files.")]
        public string? Element { get; set; }

        [Description("Issue description, e.g. TEXT-CLIPPED-X, TEXT-CLIPPED-Y, OVERFLOWS-PARENT, ZERO-SIZE — with measured pixel details.")]
        public string? Issue { get; set; }

        [Description("Truncated text content when the element is a text element.")]
        public string? Text { get; set; }

        [Description("World-space rect of the element inside the window (x, y, w, h).")]
        public string? Rect { get; set; }
    }
}
