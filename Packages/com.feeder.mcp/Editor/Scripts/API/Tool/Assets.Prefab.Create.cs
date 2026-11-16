#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.Utils;
using Feeder.MCP.Data;
using Feeder.MCP.Runtime.Extensions;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Assets_Prefab
    {
        public const string AssetsPrefabCreateToolId = "assets-prefab-create";
        [AiTool
        (
            AssetsPrefabCreateToolId,
            Title = "Assets / Prefab / Create",
            Enabled = false
        )]
        [AiSkillDescription("Create a Prefab from a scene GameObject, or a Prefab Variant of an existing prefab, at a new path under Assets/.")]
        [AiSkillBody("Sources (pass exactly one):\n\n" +
            "- `gameObjectRef` - a scene GameObject. With `connectGameObjectToPrefab` (default `true`) it becomes an instance of the new prefab; " +
            "when it already is a prefab instance the result is a Prefab Variant.\n" +
            "- `sourcePrefabAssetPath` - an existing prefab; the result is always a Prefab Variant of it.\n\n" +
            "`prefabAssetPath` is a new path under `Assets/` ending in `.prefab`; missing folders are created, an existing file is never overwritten.")]
        [Description("Create a Prefab (or Prefab Variant) at a new path under Assets/ from a scene GameObject or an existing prefab.")]
        public AssetObjectRef Create
        (
            [Description("New prefab path, e.g. 'Assets/Prefabs/Hero.prefab'.")]
            string prefabAssetPath,
            [Description("Scene GameObject to create the prefab from. Optional when 'sourcePrefabAssetPath' is given.")]
            GameObjectRef? gameObjectRef = null,
            [Description("Existing prefab to make a Prefab Variant of. Optional when 'gameObjectRef' is given.")]
            string? sourcePrefabAssetPath = null,
            [Description("Connect the scene GameObject to the new prefab (default true). Ignored with 'sourcePrefabAssetPath'.")]
            bool connectGameObjectToPrefab = true
        )
        {
            var path = MxGuard.AssetPath(prefabAssetPath, ".prefab");
            if ((gameObjectRef == null) == string.IsNullOrEmpty(sourcePrefabAssetPath))
                throw MxGuard.Fail("Pass exactly one source: 'gameObjectRef' (a scene object) or 'sourcePrefabAssetPath' (an existing prefab).");

            return MainThread.Instance.Run(() =>
            {
                MxGuard.NotExisting(path);
                UnityEngine.GameObject? prefabGo;
                if (!string.IsNullOrEmpty(sourcePrefabAssetPath))
                {
                    var sourcePath = MxGuard.ReadableAssetPath(sourcePrefabAssetPath, ".prefab");
                    var sourcePrefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(sourcePath)
                        ?? throw MxGuard.Fail(Error.NotFoundPrefabAtPath(sourcePath));
                    Mx.EnsureFolder(Path.GetDirectoryName(path)!.Replace('\\', '/'));
                    var tempInstance = (UnityEngine.GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
                    try
                    {
                        // saving an instance of a prefab writes a Prefab Variant
                        prefabGo = PrefabUtility.SaveAsPrefabAssetAndConnect(tempInstance, path, InteractionMode.UserAction, out _);
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(tempInstance);
                    }
                }
                else
                {
                    var go = gameObjectRef!.FindGameObject(out var error) ?? throw MxGuard.Fail(error ?? "The GameObject was not found.");
                    Mx.EnsureFolder(Path.GetDirectoryName(path)!.Replace('\\', '/'));
                    prefabGo = connectGameObjectToPrefab
                        ? PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.UserAction, out _)
                        : PrefabUtility.SaveAsPrefabAsset(go, path);
                    if (connectGameObjectToPrefab && prefabGo != null)
                        EditorUtility.SetDirty(go);
                }

                if (prefabGo == null)
                    throw new InvalidOperationException($"Unity failed to save the prefab at '{path}'. See the Console for the reason.");
                EditorUtils.RepaintAllEditorWindows();
                return new AssetObjectRef(prefabGo);
            });
        }
    }
}
