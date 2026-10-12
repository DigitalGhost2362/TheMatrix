#nullable enable
using System.ComponentModel;
using System.Linq;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Profiler
    {
        public const string ProfilerEnableModuleToolId = "profiler-enable-module";
        [AiTool
        (
            ProfilerEnableModuleToolId,
            Title = "Profiler / Enable Module",
            Enabled = false
        )]
        [AiSkillDescription("Toggle the wrapper's local 'enabled' flag for a named profiler module. Bookkeeping only — Unity's runtime API does not expose direct module control; for real module visibility use the Profiler window.")]
        [AiSkillBody("Adds or removes the given module name from the wrapper's `EnabledModules` set. " +
            "This is local bookkeeping consumed by `profiler-get-status` and `profiler-list-modules`; Unity's " +
            "runtime API does not allow programmatic toggling of Profiler-window modules from a built-in " +
            "namespace, so this tool intentionally does not pretend to.\n\n" +
            "## Inputs\n\n" +
            "- `moduleName` (required) — one of the names returned by `profiler-list-modules`.\n" +
            "- `enabled` (default `true`) — set to `false` to mark the module disabled.\n\n" +
            "## Errors\n\n" +
            "- Returns an `[Error]` string when `moduleName` is empty or unknown.")]
        [Description("Enables or disables a profiler module name in the wrapper's local bookkeeping set.")]
        public string EnableModule
        (
            [Description("Profiler module name (e.g. 'CPU', 'GPU', 'Memory').")]
            string moduleName,
            [Description("True to mark the module enabled in local bookkeeping; false to mark disabled.")]
            bool enabled = true
        )
        {
            return MainThread.Instance.Run(() =>
            {
                if (string.IsNullOrEmpty(moduleName))
                    return Error.ModuleNameIsRequired();

                if (!AvailableModules.Contains(moduleName))
                    return Error.UnknownModule(moduleName);

                if (enabled)
                    EnabledModules.Add(moduleName);
                else
                    EnabledModules.Remove(moduleName);

                var status = enabled ? "enabled" : "disabled";
                return $"[Success] Profiler module '{moduleName}' marked {status} in local bookkeeping. " +
                    "Use Unity's Profiler window for actual module visibility control.";
            });
        }
    }
}
