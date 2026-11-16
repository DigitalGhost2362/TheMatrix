#nullable enable
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.Utils;
using Feeder.MCP.Data;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Assets
    {
        public const string AssetsDeleteToolId = "assets-delete";
        [AiTool
        (
            AssetsDeleteToolId,
            Title = "Assets / Delete",
            DestructiveHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Delete assets under Assets/: to the OS trash by default, or permanently with trash=false.")]
        [AiSkillBody("- `paths` - assets or folders under `Assets/`; the `Assets` root and `Packages/` are refused.\n" +
            "- `trash` (default `true`) - move to the OS trash so the user can restore it; `false` deletes for good.\n\n" +
            "Not undoable in Unity. Each path succeeds or fails on its own; `DeletedPaths` lists only paths that are gone.")]
        [Description("Delete assets under Assets/ (to the OS trash unless trash=false). Returns deleted paths and per-path errors.")]
        public DeleteAssetsResponse Delete
        (
            [Description("The asset paths to delete, under Assets/.")]
            string[] paths,
            [Description("Move to the OS trash (default true); false deletes permanently.")]
            bool trash = true
        )
        {
            if (paths == null || paths.Length == 0)
                throw MxGuard.Fail("'paths' is empty. Pass at least one asset path, e.g. ['Assets/Old.mat'].");
            return MainThread.Instance.Run(() =>
            {
                var response = new DeleteAssetsResponse();
                foreach (var raw in paths)
                {
                    try
                    {
                        var path = MxGuard.AssetPath(raw);
                        if (trash)
                            Mx.TrashAsset(path);
                        else if (!File.Exists(path) && !AssetDatabase.IsValidFolder(path))
                            throw MxGuard.Fail($"Nothing at '{path}'.");
                        else if (!AssetDatabase.DeleteAsset(path))
                            throw MxGuard.Fail($"Unity failed to delete '{path}'.");
                        if (File.Exists(path) || Directory.Exists(path))
                            throw MxGuard.Fail($"'{path}' is still on disk after the delete.");
                        (response.DeletedPaths ??= new List<string>()).Add(path);
                    }
                    catch (System.Exception e) when (e is ToolCallerException || e is System.InvalidOperationException)
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
