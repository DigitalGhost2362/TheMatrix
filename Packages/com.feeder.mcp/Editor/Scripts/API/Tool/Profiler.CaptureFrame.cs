#nullable enable
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Profiler
    {
        public const string ProfilerCaptureFrameToolId = "profiler-capture-frame";
        [AiTool
        (
            ProfilerCaptureFrameToolId,
            Title = "Profiler / Capture Frame",
            ReadOnlyHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Capture the current frame's timing info (delta time, FPS, frame counts, runtime). Snapshot only — historical frames live in Unity's Profiler window.")]
        [AiSkillBody("Reads `UnityEngine.Time` fields and returns them in a single struct. " +
            "This tool is intentionally a single-frame snapshot — Unity's runtime API does not expose historical " +
            "frame-data outside the Profiler window.\n\n" +
            "## Fields\n\n" +
            "- `FrameTimeMs` — `Time.deltaTime * 1000f`.\n" +
            "- `Fps` — `1 / Time.deltaTime` (0 when delta is zero).\n" +
            "- `TotalFrameCount` — `Time.frameCount` (includes skipped renders).\n" +
            "- `RealtimeSinceStartup` — `Time.realtimeSinceStartup`.\n" +
            "- `RenderedFrameCount` — `Time.renderedFrameCount`.\n\n" +
            "## Behavior\n\n" +
            "Uses only built-in Unity APIs (`UnityEngine.Time`). No external Unity package is required.")]
        [Description("Captures current frame timing data (delta time, FPS, total + rendered frame counts, runtime).")]
        public FrameCaptureData CaptureFrame(string? nothing = null)
        {
            return MainThread.Instance.Run(() =>
            {
                var delta = Time.deltaTime;
                return new FrameCaptureData
                {
                    FrameTimeMs = delta * 1000f,
                    Fps = delta > 0f ? 1f / delta : 0f,
                    TotalFrameCount = Time.frameCount,
                    RealtimeSinceStartup = Time.realtimeSinceStartup,
                    RenderedFrameCount = Time.renderedFrameCount
                };
            });
        }
    }
}
