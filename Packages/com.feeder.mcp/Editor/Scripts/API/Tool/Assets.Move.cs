#nullable enable
using Feeder.MCP.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.Utils;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Assets
    {
        public const string AssetsMoveToolId = "assets-move";
        [AiTool
        (
            AssetsMoveToolId,
            Title = "Assets / Move",
            Enabled = false
        )]
        [AiSkillDescription("Move or rename assets at the given project paths. " +
            "Refreshes the AssetDatabase at the end. " +
            "Moving/renaming can break references in scenes, prefabs and scripts — verify with '" +
            AssetsFindToolId + "' afterwards. Use '" + AssetsFindToolId + "' to locate the assets first.")]
        [AiSkillBody("Move the assets at paths in the project. " +
            "Should be used for asset rename. " +
            "Does AssetDatabase.Refresh() at the end. " +
            "Use '" + AssetsFindToolId + "' tool to find assets before moving.\n\n" +
            "## Warning\n\n" +
            "Moving or renaming an asset can break references to it from scenes, prefabs, materials and scripts. " +
            "Unity usually retargets references for moves inside `Assets/`, but verify the result with '" +
            AssetsFindToolId + "' and check the Console for reference errors afterwards.\n\n" +
            "## Inputs\n\n" +
            "- `sourcePaths` — paths of the assets to move.\n" +
            "- `destinationPaths` — target paths (must match `sourcePaths` length).\n\n" +
            "## Behavior\n\n" +
            "Each pair is moved independently via `AssetDatabase.MoveAsset`. " +
            "Per-pair failures (Unity's `MoveAsset` returns a non-empty error string) are surfaced in " +
            "`response.Errors`; successful moves accumulate into `response.MovedPaths`.")]
        [Description("Move the assets at paths in the project. " +
            "Should be used for asset rename. " +
            "Does AssetDatabase.Refresh() at the end. " +
            "Use '" + AssetsFindToolId + "' tool to find assets before moving.")]
        public MoveAssetsResponse Move
        (
            [Description("The paths of the assets to move.")]
            string[] sourcePaths,
            [Description("The paths of moved assets.")]
            string[] destinationPaths
        )
        {
            return MainThread.Instance.Run(() =>
            {
                if (sourcePaths.Length == 0)
                    throw new ArgumentException(Error.SourcePathsArrayIsEmpty(), nameof(sourcePaths));

                if (sourcePaths.Length != destinationPaths.Length)
                    throw new ArgumentException(Error.SourceAndDestinationPathsArrayMustBeOfTheSameLength());

                var response = new MoveAssetsResponse();

                for (int i = 0; i < sourcePaths.Length; i++)
                {
                    var error = AssetDatabase.MoveAsset(sourcePaths[i], destinationPaths[i]);
                    if (string.IsNullOrEmpty(error))
                    {
                        response.MovedPaths ??= new();
                        response.MovedPaths.Add(destinationPaths[i]);
                    }
                    else
                    {
                        response.Errors ??= new();
                        response.Errors.Add($"Failed to move asset from {sourcePaths[i]} to {destinationPaths[i]}: {error}.");
                    }
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                EditorUtils.RepaintAllEditorWindows();
                return response;
            });
        }

    }
}
