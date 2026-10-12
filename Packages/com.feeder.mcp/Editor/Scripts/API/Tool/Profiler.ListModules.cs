#nullable enable
using System.ComponentModel;
using System.Linq;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Profiler
    {
        public const string ProfilerListModulesToolId = "profiler-list-modules";
        [AiTool
        (
            ProfilerListModulesToolId,
            Title = "Profiler / List Modules",
            ReadOnlyHint = true,
            IdempotentHint = true,
            Enabled = false
        )]
        [AiSkillDescription("List all known profiler module names with their local 'enabled' bookkeeping flag.")]
        [AiSkillBody("Returns `Tool_Profiler.AvailableModules` projected into a `ProfilerModulesData` list, " +
            "with each entry's `Enabled` field reflecting the wrapper's local bookkeeping.\n\n" +
            "## Behavior\n\n" +
            "Uses only built-in Unity APIs and in-process state. No external Unity package is required. " +
            "Pair with `profiler-enable-module` to flip the bookkeeping flag.")]
        [Description("Lists all available profiler modules and whether the wrapper considers each enabled.")]
        public ProfilerModulesData ListModules(string? nothing = null)
        {
            return MainThread.Instance.Run(() => new ProfilerModulesData
            {
                Modules = AvailableModules
                    .Select(name => new ProfilerModuleInfo
                    {
                        Name = name,
                        Enabled = EnabledModules.Contains(name)
                    })
                    .ToList()
            });
        }
    }
}
