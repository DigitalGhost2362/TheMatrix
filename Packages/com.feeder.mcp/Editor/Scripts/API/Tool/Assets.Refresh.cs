#nullable enable
using System.ComponentModel;
using System.Threading.Tasks;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.Utils;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Assets
    {
        public const string AssetsRefreshToolId = "assets-refresh";
        const int CompileStartChecks = 10;
        const int CompileStartCheckMs = 50;
        internal const string NothingToCompileAnswer = "[Success] AssetDatabase refreshed, nothing to compile.";
        [AiTool
        (
            AssetsRefreshToolId,
            Title = "Assets / Refresh",
            IdempotentHint = true,
            Enabled = true
        )]
        [AiSkillDescription("Refresh the Unity AssetDatabase. " +
            "Use after files were added or updated outside of the Unity API, or to force script recompilation " +
            "when a '.cs' file changed. Returns a processing/success response and waits for compilation when triggered.")]
        [AiSkillBody("Refreshes the AssetDatabase. " +
            "Use it if any file was added or updated in the project outside of Unity API. " +
            "Use it if need to force scripts recompilation when '.cs' file changed.\n\n" +
            "## Inputs\n\n" +
            "- `options` — `ImportAssetOptions` flag (default `ForceSynchronousImport`).\n" +
            "- `requestId` — required for processing-mode responses; the tool returns `Processing` immediately and " +
            "delivers a follow-up completion when compilation finishes.\n\n" +
            "## Behavior\n\n" +
            "Runs `AssetDatabase.Refresh(options)`. In play mode it returns at once: Unity holds script changes until play mode ends. Otherwise it watches for up to half a second for a compile to start. If one starts, " +
            "schedules a post-compilation notification and returns a `Processing` response. If compilation already " +
            "failed (`EditorUtility.scriptCompilationFailed`), returns `Success` with the compilation error details. " +
            "Otherwise returns a plain `Success`.\n\n" +
            "The final answer already lists every compile error with its file and line: after `[Success]` there is no need to read the console.")]
        [Description("Refreshes the AssetDatabase. " +
            "Use it if any file was added or updated in the project outside of Unity API. " +
            "Use it if need to force scripts recompilation when '.cs' file changed. " +
            "The final answer lists compile errors by file and line; after [Success] skip console-get-logs.")]
        public async Task<ResponseCallTool> Refresh
        (
            [Description("Asset import options.")]
            ImportAssetOptions? options = ImportAssetOptions.ForceSynchronousImport,
            [RequestID]
            string? requestId = null
        )
        {
            if (requestId == null || string.IsNullOrWhiteSpace(requestId))
                return ResponseCallTool.Error("Original request with valid RequestID must be provided.");

            var refresh = await MainThread.Instance.RunAsync(async () =>
            {
                AssetDatabase.Refresh(options ?? ImportAssetOptions.ForceSynchronousImport);

                // Unity holds the compile until play mode ends, so waiting for it would never return
                if (MxGuard.IsPlaying())
                    return ResponseCallTool.Success("AssetDatabase refreshed. Script changes are held until play mode ends: " +
                        "exit play mode (editor-application-set-state isPlaying=false), then refresh again to compile them.").SetRequestID(requestId);

                // Unity may start the compile a few ticks after Refresh returns
                for (var i = 0; i < CompileStartChecks && !EditorApplication.isCompiling; i++)
                    await Task.Delay(CompileStartCheckMs);

                if (EditorApplication.isCompiling)
                {
                    ScriptUtils.SchedulePostCompilationNotification(requestId, "AssetDatabase", "Assets refresh");
                    return ResponseCallTool.Processing("AssetDatabase refreshed. Compilation in progress, waiting for completion...").SetRequestID(requestId);
                }

                if (EditorUtility.scriptCompilationFailed)
                {
                    var errorDetails = ScriptUtils.GetCompilationErrorDetails();
                    return ResponseCallTool.Success($"[Warning] AssetDatabase refreshed, but compilation errors exist:\n\n{errorDetails}").SetRequestID(requestId);
                }

                // same marker as the post-compile answer: the description tells agents to stop at [Success]
                return ResponseCallTool.Success(NothingToCompileAnswer).SetRequestID(requestId);
            });
            return await refresh;
        }
    }
}
