#nullable enable
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Scene
    {
        public const string SceneUnloadToolId = "scene-unload";
        [AiTool
        (
            SceneUnloadToolId,
            Title = "Scene / Unload",
            Enabled = false
        )]
        [AiSkillDescription("Close one open scene (unload it in play mode) without dropping its unsaved changes by accident.")]
        [AiSkillBody("## Inputs\n\n" +
            "- `scene` - path of the open scene, or its name when only one open scene has it.\n" +
            "- `ifDirty` (default `refuse`) - the scene has unsaved changes: `refuse`, `save` or `discard`.\n\n" +
            "In play mode the scene is unloaded with `SceneManager.UnloadSceneAsync`; nothing is saved. The last open scene cannot be closed.")]
        [Description("Close an open scene (unload in play mode). Returns its name and asset.")]
        public Task<UnloadSceneResult> Unload
        (
            [Description("Path of the open scene (preferred), or its name when only one open scene has it.")]
            string scene,
            [Description("Unsaved changes in that scene: 'refuse' (default), 'save' or 'discard'.")]
            string ifDirty = MxGuard.Refuse
        )
        {
            MxGuard.IfDirty(ifDirty);
            return MainThread.Instance.Run(async () =>
            {
                var target = FindOpenScene(scene);
                var path = target.path;
                var name = target.name;
                if (UnityEngine.Application.isPlaying)
                {
                    var operation = SceneManager.UnloadSceneAsync(target)
                        ?? throw MxGuard.Fail($"'{name}' is the only loaded scene and cannot be unloaded. Load another scene first.");
                    while (!operation.isDone)
                        await Task.Yield();
                }
                else
                {
                    if (EditorSceneManager.sceneCount <= 1)
                        throw MxGuard.Fail($"'{name}' is the only open scene and cannot be closed. Open another scene first (scene-open).");
                    MxGuard.ScenesSaved(ifDirty, target);
                    if (!EditorSceneManager.CloseScene(target, removeScene: true))
                        throw new InvalidOperationException($"Unity failed to close '{name}'. Check the Console for the reason.");
                }

                var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
                return new UnloadSceneResult { Name = name, AssetObjectRef = asset == null ? null : new AssetObjectRef(asset) };
            });
        }
    }
}
