#nullable enable
using System.ComponentModel;
using Feeder.ToolHost;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_Ping
    {
        public const string PingToolId = "ping";
        [AiTool
        (
            PingToolId,
            Title = "Ping",
            ReadOnlyHint = true,
            IdempotentHint = true,
            ToolType = McpToolType.System
        )]
        [AiSkillDescription("Lightweight readiness probe. Returns the input `message` echoed back, or `'pong'` " +
            "when omitted. Useful for CLI health checks and SignalR connectivity smoke tests.")]
        [AiSkillBody("Lightweight readiness probe. Returns the input message or 'pong' if omitted.\n\n" +
            "## Inputs\n\n" +
            "- `message` (optional) — when present, echoed back verbatim.\n\n" +
            "## Behavior\n\n" +
            "No I/O, no Unity API calls — pure echo. Ideal for measuring round-trip latency or confirming the MCP " +
            "transport is alive before invoking a heavier tool.")]
        [Description("Lightweight readiness probe. Returns the input message or 'pong' if omitted.")]
        public string Ping
        (
            [Description("Optional message to echo back.")]
            string? message = null
        )
        {
            return message ?? "pong";
        }
    }
}
