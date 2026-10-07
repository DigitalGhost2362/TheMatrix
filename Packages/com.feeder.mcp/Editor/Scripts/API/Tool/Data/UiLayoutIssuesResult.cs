#nullable enable
using System.ComponentModel;

namespace Feeder.MCP.Data
{
    public class UiLayoutIssuesResult
    {
        [Description("Detected layout issues, capped at 200 entries.")]
        public UiLayoutIssue[]? Issues { get; set; }

        [Description("Short class names of the windows that were scanned.")]
        public string[]? WindowsScanned { get; set; }

        [Description("Total number of visual elements visited during the scan.")]
        public int ElementsScanned { get; set; }

        [Description("Extra notes about the scan (caps hit, windows skipped, etc.).")]
        public string? Note { get; set; }
    }
}
