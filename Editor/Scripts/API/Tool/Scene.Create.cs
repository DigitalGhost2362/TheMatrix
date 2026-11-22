#nullable enable
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Data;
using Feeder.MCP.Editor.Utils;
using UnityEditor.SceneManagement;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Scene
    {
        public const string SceneCreateToolId = "scene-create";
        [AiTool
        (
            SceneCreateToolId,
            Title = "Scene / Create",
            Enabled = false
        )]
        [AiSkillDescription("Create a new scene asset at a `.unity` path, without losing unsaved work in the scenes already open.")]
        [AiSkillBody("## Inputs\n\n" +
            "- `path` - new `.unity` path under `Assets/`; missing folders are created.\n" +
            "- `newSceneSetup` (default `DefaultGameObjects`) - `EmptyScene` or `DefaultGameObjects`.\n" +
            "- `newSceneMode` (default `Single`) - `Single` replaces the open scenes, `Additive` keeps them.\n" +
            "- `ifDirty` (default `refuse`) - Single mode with unsaved scenes: `refuse`, `save` or `discard`.")]
        [Description("Create and save a new scene asset. Returns the new scene.")]
        public SceneDataShallow Create
        (
            [Description("New scene path, e.g. 'Assets/Scenes/Level2.unity'.")]
            string path,
            [Description("EmptyScene or DefaultGameObjects (default).")]
            NewSceneSetup newSceneSetup = NewSceneSetup.DefaultGameObjects,
            [Description("Single (default) replaces the open scenes; Additive keeps them.")]
            NewSceneMode newSceneMode = NewSceneMode.Single,
            [Description("Single mode with unsaved scenes: 'refuse' (default), 'save' or 'discard'.")]
            string ifDirty = MxGuard.Refuse
        )
        {
            var scene = Mx.NewScene(path, ifDirty, newSceneMode == NewSceneMode.Additive, newSceneSetup == NewSceneSetup.EmptyScene);
            return MainThread.Instance.Run(() =>
            {
                EditorUtils.RepaintAllEditorWindows();
                return scene.ToSceneDataShallow();
            });
        }
    }
}
