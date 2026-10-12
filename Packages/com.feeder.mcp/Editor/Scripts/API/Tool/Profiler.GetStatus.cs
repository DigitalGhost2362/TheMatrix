#nullable enable
using System.ComponentModel;
using System.Linq;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityProfiler = UnityEngine.Profiling.Profiler;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Profiler
    {
        public const string ProfilerGetStatusToolId = "profiler-get-status";
        [AiTool
        (
            ProfilerGetStatusToolId,
            Title = "Profiler / Get Status",
            ReadOnlyHint = true,
            IdempotentHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Return the Unity profiler's current enabled state, active modules, max-used memory, and platform support flag. Read-only.")]
        [AiSkillBody("Snapshots `UnityEngine.Profiling.Profiler` state and returns it in a single response.\n\n" +
            "## Fields\n\n" +
            "- `ProfilerEnabled` — `Profiler.enabled`.\n" +
            "- `ActiveModules` — names of modules this wrapper considers enabled (local bookkeeping).\n" +
            "- `MaxUsedMemoryMB` — `Profiler.maxUsedMemory / 1048576f`.\n" +
            "- `Supported` — `Profiler.supported`.\n\n" +
            "## Behavior\n\n" +
            "Uses only built-in Unity APIs. No external Unity package is required.")]
        [Description("Returns the current state of the Unity Profiler (enabled flag, active modules, max-used memory, platform support).")]
        public ProfilerStatusData GetStatus(string? nothing = null)
        {
            // Divide in double precision then cast — see GetMemoryStats for the rationale.
            return MainThread.Instance.Run(() => new ProfilerStatusData
            {
                ProfilerEnabled = UnityProfiler.enabled,
                ActiveModules = EnabledModules.OrderBy(name => name).ToList(),
                MaxUsedMemoryMB = (float)(UnityProfiler.maxUsedMemory / 1048576.0),
                Supported = UnityProfiler.supported
            });
        }
    }
}
