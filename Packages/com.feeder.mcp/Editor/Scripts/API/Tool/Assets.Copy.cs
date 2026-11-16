#nullable enable
using System.Collections.Generic;
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.Utils;
using Feeder.MCP.Data;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Assets
    {
        public const string AssetsCopyToolId = "assets-copy";
        [AiTool
        (
            AssetsCopyToolId,
            Title = "Assets / Copy",
            Enabled = false
        )]
        [AiSkillDescription("Copy assets to new paths under Assets/. Each pair succeeds or fails on its own.")]
        [AiSkillBody("- `sourcePaths` - assets to copy (under `Assets/` or `Packages/`).\n" +
            "- `destinationPaths` - new paths under `Assets/`, one per source; an existing file is never overwritten.\n\n" +
            "A bad pair is reported in `Errors` and the others still run.")]
        [Description("Copy assets to new paths under Assets/ (pairs by index). Returns the copies and per-pair errors.")]
        public CopyAssetsResponse Copy
        (
            [Description("The paths of the assets to copy.")]
            string[] sourcePaths,
            [Description("The new paths, one per source, under Assets/.")]
            string[] destinationPaths
        )
        {
            CheckPairs(sourcePaths, destinationPaths);
            return MainThread.Instance.Run(() =>
            {
                var response = new CopyAssetsResponse();
                for (var i = 0; i < sourcePaths.Length; i++)
                {
                    try
                    {
                        var source = MxGuard.ReadableAssetPath(sourcePaths[i]);
                        var destination = MxGuard.NotExisting(MxGuard.AssetPath(destinationPaths[i]));
                        if (!AssetDatabase.CopyAsset(source, destination))
                            throw MxGuard.Fail($"Unity failed to copy '{source}' to '{destination}'. Check that the source exists.");
                        (response.CopiedAssets ??= new List<AssetObjectRef>()).Add(new AssetObjectRef(AssetDatabase.LoadMainAssetAtPath(destination)));
                    }
                    catch (ToolCallerException e)
                    {
                        (response.Errors ??= new List<string>()).Add(e.Message);
                    }
                }
                EditorUtils.RepaintAllEditorWindows();
                return response;
            });
        }

        static void CheckPairs(string[]? sources, string[]? destinations)
        {
            if (sources == null || sources.Length == 0)
                throw MxGuard.Fail("'sourcePaths' is empty. Pass at least one asset path, e.g. ['Assets/A.mat'].");
            if (destinations == null || destinations.Length != sources.Length)
                throw MxGuard.Fail($"'destinationPaths' must have one path per source ({sources.Length}); got {destinations?.Length ?? 0}.");
        }
    }
}
