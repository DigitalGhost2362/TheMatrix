#nullable enable
using System;
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.Utils;
using Feeder.MCP.Data;
using Feeder.MCP.Runtime.Utils;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Assets_Prefab
    {
        // test hook: runs right after the instance is created
        internal static Action<GameObject>? OnInstantiated;

        public const string AssetsPrefabInstantiateToolId = "assets-prefab-instantiate";
        [AiTool
        (
            AssetsPrefabInstantiateToolId,
            Title = "Assets / Prefab / Instantiate",
            Enabled = false
        )]
        [AiSkillDescription("Instantiate a prefab into the currently active scene at an optional position/rotation/scale, " +
            "parented under an optional scene GameObject path. " +
            "Use '" + Tool_Rql.QueryToolId + "' to locate the prefab asset first.")]
        [AiSkillBody("Instantiates prefab in the current active scene. " +
            "Use '" + Tool_Rql.QueryToolId + "' tool to find prefab assets in the project.\n\n" +
            "## Inputs\n\n" +
            "- `prefabAssetPath` — project asset path of the prefab to instantiate.\n" +
            "- `gameObjectPath` — destination path in the scene; the last segment becomes the new GameObject's name, " +
            "any prefix is looked up as the parent (must already exist).\n" +
            "- `position` / `rotation` / `scale` — optional transform; default to zero / zero / one.\n" +
            "- `isLocalSpace` — when `true`, applies the transform in local space relative to the parent.")]
        [Description("Instantiates prefab in the current active scene. " +
            "Use '" + Tool_Rql.QueryToolId + "' tool to find prefab assets in the project.")]
        public GameObjectRef Instantiate
        (
            [Description("Prefab asset path.")]
            string prefabAssetPath,
            [Description("GameObject path in the current active scene.")]
            string gameObjectPath,
            [Description("Transform position of the GameObject.")]
            Vector3? position = default,
            [Description("Transform rotation of the GameObject. Euler angles in degrees.")]
            Vector3? rotation = default,
            [Description("Transform scale of the GameObject.")]
            Vector3? scale = default,
            [Description("World or Local space of transform.")]
            bool isLocalSpace = false
        )
        {
            var path = MxGuard.ReadableAssetPath(prefabAssetPath, ".prefab");
            return MainThread.Instance.Run(() =>
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path)
                    ?? throw MxGuard.Fail(Error.NotFoundPrefabAtPath(path));

                var parentGo = default(GameObject);
                if (StringUtils.Path_ParseParent(gameObjectPath, out var parentPath, out var name))
                    parentGo = GameObjectUtils.FindByPath(parentPath)
                        ?? throw MxGuard.Fail($"No GameObject at '{parentPath}' in the open scenes. Pass an existing parent path, or just a name.");

                var undoName = $"Instantiate {prefab.name}";
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName(undoName);
                var undoGroup = Undo.GetCurrentGroup();
                try
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    Undo.RegisterCreatedObjectUndo(go, undoName);
                    OnInstantiated?.Invoke(go);
                    go.name = name ?? prefab.name;
                    if (parentGo != null)
                        Undo.SetTransformParent(go.transform, parentGo.transform, undoName);
                    go.SetTransform(position ?? Vector3.zero, rotation ?? Vector3.zero, scale ?? Vector3.one, isLocalSpace);
                    Undo.CollapseUndoOperations(undoGroup);
                    EditorUtility.SetDirty(go);
                    EditorUtils.RepaintAllEditorWindows();
                    return new GameObjectRef(go);
                }
                catch
                {
                    // a half-made instance must not stay in the scene, nor an open group swallow the next edits
                    Undo.RevertAllDownToGroup(undoGroup);
                    throw;
                }
            });
        }
    }
}
