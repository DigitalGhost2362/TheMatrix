#nullable enable
using System.ComponentModel;
using System.Globalization;
using Feeder.Reflect.Utils;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;
using UnityEditor;
using UnityEngine;
using UnityProfiler = UnityEngine.Profiling.Profiler;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Profiler
    {
        public const string ProfilerStatsToolId = "profiler-stats";

        [AiTool(ProfilerStatsToolId, Title = "Profiler / Stats", IdempotentHint = true, Enabled = false)]
        [AiSkillDescription("One compact report of the profiler state, frame timing, memory counters and the Editor's rendering counters. Optionally turns the runtime profiler on or off.")]
        [AiSkillBody("# Profiler / Stats\n\nReturns `UnityEngine.Profiling.Profiler` enabled/supported, frame timing (`Time.deltaTime`, fixed step, time scale, frame counts), memory counters in MB (reserved, allocated, unused reserved, mono heap/used, GC heap, graphics driver, max used) and, from `UnityEditor.UnityStats`, draw calls, batches, set-pass calls and triangles of the last rendered frame (they only update while a Game or Scene view renders). Frame timing outside Play mode is the Editor loop, not the game. `recording=true|false` sets `Profiler.enabled` before the report is built; omit it to leave the state unchanged. Historical frame data is only in the Profiler window; for object-level memory use `profiler-memory`.")]
        [Description("Profiler state + frame timing + memory + rendering counters. recording=true|false toggles Profiler.enabled.")]
        public ResponseCallTool Stats
        (
            [Description("true / false sets Profiler.enabled; omit to only read.")]
            bool? recording = null
        )
        {
            return MainThread.Instance.Run(() =>
            {
                if (recording.HasValue) UnityProfiler.enabled = recording.Value;
                var delta = Time.deltaTime;
                var fps = delta > 0f ? 1f / delta : 0f;
                return ResponseCallTool.Success(string.Join("\n", new[]
                {
                    $"profiler enabled={UnityProfiler.enabled} supported={UnityProfiler.supported} playing={Application.isPlaying}",
                    $"frame {F(delta * 1000f)}ms ({F(fps)}fps) fixed={F(Time.fixedDeltaTime * 1000f)}ms timeScale={F(Time.timeScale)} frames={Time.frameCount} rendered={Time.renderedFrameCount} t={F(Time.realtimeSinceStartup)}s",
                    $"memoryMB reserved={MB(UnityProfiler.GetTotalReservedMemoryLong())} allocated={MB(UnityProfiler.GetTotalAllocatedMemoryLong())} unusedReserved={MB(UnityProfiler.GetTotalUnusedReservedMemoryLong())} monoHeap={MB(UnityProfiler.GetMonoHeapSizeLong())} monoUsed={MB(UnityProfiler.GetMonoUsedSizeLong())} gc={MB(System.GC.GetTotalMemory(false))} graphics={MB(UnityProfiler.GetAllocatedMemoryForGraphicsDriver())} maxUsed={MB(UnityProfiler.maxUsedMemory)}",
                    $"rendering drawCalls={UnityStats.drawCalls} setPassCalls={UnityStats.setPassCalls} triangles={UnityStats.triangles}"
                }));
            });
        }

        static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        static string MB(long bytes) => (bytes / 1048576.0).ToString("0.#", CultureInfo.InvariantCulture);
    }
}
