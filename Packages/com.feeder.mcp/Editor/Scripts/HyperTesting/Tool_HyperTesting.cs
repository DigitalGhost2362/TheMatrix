#nullable enable
using System.ComponentModel;
using System.Threading.Tasks;
using Feeder.McpPlugin;
using Feeder.MCP.Editor.HyperTesting;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_HyperTesting
    {
        public const string HyperTestingToolId = "hyper-testing";

        [AiTool
        (
            HyperTestingToolId,
            Title = "Hyper Testing / Clone Pool (Experimental)"
        )]
        [AiSkillDescription("EXPERIMENTAL. Manage the Hyper Testing clone pool for parallel play testing: status, up, refresh, down, ram. " +
            "Works only after the user enabled Hyper Testing Mode by hand; agents can never enable it. Read the matrix-hyper-testing skill first.")]
        [AiSkillBody("Manages the clone editors used by Hyper Testing Mode (experimental).\n\n" +
            "## Inputs\n\n" +
            "- `action` - `status` (default), `up`, `refresh`, `down`, `purge`, `ram`.\n" +
            "- `count` - number of clones for `up` (capped by HyperTesting/config.json maxClones, the VRAM gate and the commit gate).\n\n" +
            "## Behavior\n\n" +
            "`up`, `refresh`, `down` and `purge` start a background job and return at once; poll `status` until the job finishes. " +
            "`purge` stops the clones and deletes their folders (junctions are unlinked first). `ram` takes one memory sample (RAM, commit, import workers) into " +
            "HyperTesting/Journal/ram-samples.csv. When the mode is off every action returns a refusal: stop and ask the user, " +
            "never try to enable the mode yourself.")]
        [Description("EXPERIMENTAL Hyper Testing clone pool: action = status | up | refresh | down | purge | ram. " +
            "Refuses unless the user enabled Hyper Testing Mode in Tools > Feeder > Hyper Testing (Experimental).")]
        public async Task<string> Run
        (
            [Description("status | up | refresh | down | purge | ram")]
            string action = "status",
            [Description("Clone count for 'up'.")]
            int count = 2
        )
        {
            var status = await HyperClonePool.StatusAsync();
            if (!await HyperClonePool.IsAvailableAsync())
                return status;

            switch ((action ?? "status").Trim().ToLowerInvariant())
            {
                case "status":
                    return status;
                case "up":
                    return HyperClonePool.StartJob($"up {count}", () => HyperClonePool.UpAsync(count));
                case "refresh":
                    return HyperClonePool.StartJob("refresh", HyperClonePool.RefreshAsync);
                case "down":
                    return HyperClonePool.StartJob("down", () => HyperClonePool.DownAsync(purge: false));
                case "purge":
                    return HyperClonePool.StartJob("purge", () => HyperClonePool.DownAsync(purge: true));
                case "ram":
                    return HyperRamMonitor.Sample();
                default:
                    return $"Unknown action '{action}'. Use status, up, refresh, down, purge or ram.";
            }
        }
    }
}
