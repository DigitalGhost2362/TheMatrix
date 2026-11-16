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
        public const string AssetsMoveToolId = "assets-move";
        [AiTool
        (
            AssetsMoveToolId,
            Title = "Assets / Move",
            Enabled = false
        )]
        [AiSkillDescription("Move or rename assets inside Assets/, keeping their GUIDs so references stay intact.")]
        [AiSkillBody("- `sourcePaths` - assets to move, under `Assets/`.\n" +
            "- `destinationPaths` - new paths under `Assets/`, one per source; the folder must exist.\n\n" +
            "A bad pair is reported in `Errors` and the others still run.")]
        [Description("Move or rename assets inside Assets/ (pairs by index), keeping GUIDs. Returns moved paths and per-pair errors.")]
        public MoveAssetsResponse Move
        (
            [Description("The paths of the assets to move.")]
            string[] sourcePaths,
            [Description("The new paths, one per source, under Assets/.")]
            string[] destinationPaths
        )
        {
            CheckPairs(sourcePaths, destinationPaths);
            return MainThread.Instance.Run(() =>
            {
                var response = new MoveAssetsResponse();
                for (var i = 0; i < sourcePaths.Length; i++)
                {
                    try
                    {
                        var source = MxGuard.AssetPath(sourcePaths[i]);
                        var destination = MxGuard.AssetPath(destinationPaths[i]);
                        var error = AssetDatabase.MoveAsset(source, destination);
                        if (!string.IsNullOrEmpty(error))
                            throw MxGuard.Fail($"Unity refused to move '{source}' to '{destination}': {error}");
                        (response.MovedPaths ??= new List<string>()).Add(destination);
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
    }
}
