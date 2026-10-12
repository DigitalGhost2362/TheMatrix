#nullable enable
using System;
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.Utils;
using Feeder.MCP.Data;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Assets_Prefab
    {
        public const string AssetsPrefabSaveToolId = "assets-prefab-save";
        [AiTool
        (
            AssetsPrefabSaveToolId,
            Title = "Assets / Prefab / Save",
            IdempotentHint = true
        )]
        [AiSkillDescription("Save the currently opened prefab edit stage back to its prefab asset without exiting the stage. " +
            "Pair with '" + AssetsPrefabOpenToolId + "' to enter the edit mode first.")]
        [AiSkillBody("Save a prefab. " +
            "Use it when you are in prefab editing mode in Unity Editor. " +
            "Use '" + AssetsPrefabOpenToolId + "' tool to open a prefab first.\n\n" +
            "## Behavior\n\n" +
            "Calls `PrefabUtility.SaveAsPrefabAsset` on the current prefab stage's contents root, clears the stage's " +
            "dirtiness flag, repaints editor windows, and returns an `AssetObjectRef` to the saved prefab. " +
            "Throws when no prefab stage is currently open.")]
        [Description("Save a prefab. " +
            "Use it when you are in prefab editing mode in Unity Editor. " +
            "Use '" + AssetsPrefabOpenToolId + "' tool to open a prefab first.")]
        public AssetObjectRef Save(string? nothing = null) => MainThread.Instance.Run(() =>
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage == null)
                throw new InvalidOperationException(Error.PrefabStageIsNotOpened());

            var prefabGo = prefabStage.prefabContentsRoot;
            if (prefabGo == null)
                throw new InvalidOperationException(Error.PrefabStageIsNotOpened());

            var assetPath = prefabStage.assetPath;
            var goName = prefabGo.name;

            PrefabUtility.SaveAsPrefabAsset(prefabGo, assetPath);
            prefabStage.ClearDirtiness();

            EditorUtils.RepaintAllEditorWindows();

            var assetPrefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(assetPath);
            return new AssetObjectRef(assetPrefab);
        });
    }
}