#nullable enable
using System.ComponentModel;

namespace Feeder.MCP.Data
{
    public class UiWindowsResult
    {
        [Description("All EditorWindow instances currently alive in the editor.")]
        public UiWindowInfo[]? Windows { get; set; }

        [Description("Total number of windows returned.")]
        public int Count { get; set; }
    }
}
