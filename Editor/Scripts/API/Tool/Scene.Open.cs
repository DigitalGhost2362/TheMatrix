#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.Utils;
using Feeder.MCP.Data;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Scene
    {
        public const string SceneOpenToolId = "scene-open";
        [AiTool
        (
            SceneOpenToolId,
            Title = "Scene / Open",
            Enabled = true
        )]
        [AiSkillDescription("Open a scene asset in Single or Additive mode without losing unsaved work in the scenes already open. " +
            "Returns the opened-scene list.")]
        [AiSkillBody("## Inputs\n\n" +
            "- `path` - scene asset path, for example `Assets/Scenes/Main.unity`.\n" +
            "- `loadSceneMode` (default `Single`) - `Single` replaces the open scenes, `Additive` opens it next to them.\n" +
            "- `ifDirty` (default `refuse`) - what Single mode does when an open scene has unsaved changes: " +
            "`refuse` stops and lists those scenes, `save` saves them first, `discard` drops the changes.\n\n" +
            "Refused in play mode. Opening a scene that is already open only makes it active.")]
        [Description("Open a scene asset in Single or Additive mode. Returns the open scenes.")]
        public SceneDataShallow[] Open
        (
            [Description("Scene asset path, e.g. 'Assets/Scenes/Main.unity'.")]
            string path,
            [Description("Single: replace the open scenes. Additive: open it next to them.")]
            OpenSceneMode loadSceneMode = OpenSceneMode.Single,
            [Description("Single mode with unsaved scenes: 'refuse' (default), 'save' or 'discard'.")]
            string ifDirty = "refuse"
        )
        {
            Mx.OpenScene(path, loadSceneMode, ifDirty);
            return MainThread.Instance.Run(() =>
            {
                EditorUtils.RepaintAllEditorWindows();
                return OpenedScenes.Select(scene => scene.ToSceneDataShallow()).ToArray();
            });
        }
    }
}
