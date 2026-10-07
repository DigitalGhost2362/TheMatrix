#nullable enable
using System;
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityEngine;
using UnityProfiler = UnityEngine.Profiling.Profiler;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Profiler
    {
        public const string ProfilerGetScriptStatsToolId = "profiler-get-script-stats";
        [AiTool
        (
            ProfilerGetScriptStatsToolId,
            Title = "Profiler / Get Script Stats",
            ReadOnlyHint = true,
            IdempotentHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Return script execution timing (frame time, fixed dt, time scale, frame count, runtime) plus Mono / GC memory usage in MB.")]
        [AiSkillBody("Snapshots fields from `UnityEngine.Time`, `UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong()` and " +
            "`System.GC.GetTotalMemory(false)`. All values are produced by built-in Unity APIs.\n\n" +
            "## Fields\n\n" +
            "- `FrameTimeMs` — `Time.deltaTime * 1000f`.\n" +
            "- `FixedDeltaTimeMs` — `Time.fixedDeltaTime * 1000f`.\n" +
            "- `TimeScale` — `Time.timeScale`.\n" +
            "- `TotalFrameCount` — `Time.frameCount`.\n" +
            "- `RealtimeSinceStartup` — `Time.realtimeSinceStartup`.\n" +
            "- `MonoMemoryUsageMB` — `Profiler.GetMonoUsedSizeLong() / 1048576f`.\n" +
            "- `GCMemoryUsageMB` — `GC.GetTotalMemory(false) / 1048576f`.")]
        [Description("Returns script execution statistics including timing and Mono / GC memory usage.")]
        public ScriptStatsData GetScriptStats(string? nothing = null)
        {
            // Divide in double precision then cast — see GetMemoryStats for the rationale.
            return MainThread.Instance.Run(() => new ScriptStatsData
            {
                FrameTimeMs = Time.deltaTime * 1000f,
                FixedDeltaTimeMs = Time.fixedDeltaTime * 1000f,
                TimeScale = Time.timeScale,
                TotalFrameCount = Time.frameCount,
                RealtimeSinceStartup = Time.realtimeSinceStartup,
                MonoMemoryUsageMB = (float)(UnityProfiler.GetMonoUsedSizeLong() / 1048576.0),
                GCMemoryUsageMB = (float)(GC.GetTotalMemory(false) / 1048576.0)
            });
        }
    }
}
