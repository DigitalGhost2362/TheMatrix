#nullable enable
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityProfiler = UnityEngine.Profiling.Profiler;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Profiler
    {
        public const string ProfilerStopToolId = "profiler-stop";
        [AiTool
        (
            ProfilerStopToolId,
            Title = "Profiler / Stop",
            IdempotentHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Disable Unity's runtime profiler. Idempotent — calling when already disabled returns the current disabled state.")]
        [AiSkillBody("Sets `UnityEngine.Profiling.Profiler.enabled = false`. Returns the post-call value of `Profiler.enabled` " +
            "(expected `false`).\n\n" +
            "## Behavior\n\n" +
            "Uses only built-in Unity APIs (`UnityEngine.Profiling`). No external Unity package is required.")]
        [Description("Disable the Unity Profiler. Returns false once disabled.")]
        public bool Stop(string? nothing = null)
        {
            return MainThread.Instance.Run(() =>
            {
                UnityProfiler.enabled = false;
                return UnityProfiler.enabled;
            });
        }
    }
}
