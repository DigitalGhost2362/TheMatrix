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
        public const string AssetsPrefabInstantiateToolId = "assets-prefab-instantiate";
        [AiTool
        (
            AssetsPrefabInstantiateToolId,
            Title = "Assets / Prefab / Instantiate"
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
            return MainThread.Instance.Run(() =>
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
                if (prefab == null)
                    throw new ArgumentException($"Prefab not found at path '{prefabAssetPath}'.", nameof(prefabAssetPath));

                var parentGo = default(GameObject);
                if (StringUtils.Path_ParseParent(gameObjectPath, out var parentPath, out var name))
                {
                    parentGo = GameObjectUtils.FindByPath(parentPath);
                    if (parentGo == null)
                        throw new ArgumentException($"GameObject with path '{parentPath}' not found.", nameof(gameObjectPath));
                }

                position ??= Vector3.zero;
                rotation ??= Vector3.zero;
                scale ??= Vector3.one;

                var go = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (go == null)
                    throw new Exception($"Failed to instantiate prefab from path '{prefabAssetPath}'.");

                go.name = name ?? prefab.name;
                if (parentGo != null)
                    go.transform.SetParent(parentGo.transform, false);
                go.SetTransform(position, rotation, scale, isLocalSpace);

                EditorUtility.SetDirty(go);
                EditorUtils.RepaintAllEditorWindows();

                return new GameObjectRef(go);
            });
        }
    }
}
