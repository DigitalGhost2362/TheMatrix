#nullable enable
using System.ComponentModel;
using System.Linq;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Data;
using Feeder.MCP.Editor.Utils;
using UnityEditor.SceneManagement;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Scene
    {
        public const string SceneSetActiveToolId = "scene-set-active";
        [AiTool
        (
            SceneSetActiveToolId,
            Title = "Scene / Set Active",
            IdempotentHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Make an open scene the active one (new GameObjects go there).")]
        [Description("Make an open scene active, by path or by name. Returns the open scenes.")]
        public SceneDataShallow[] SetActive
        (
            [Description("Path of the open scene (preferred), or its name when only one open scene has it.")]
            string scene
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var target = FindOpenScene(scene);
                if (EditorSceneManager.GetActiveScene() != target && !EditorSceneManager.SetActiveScene(target))
                    throw new System.InvalidOperationException($"Unity refused to make '{target.path}' active; is it loaded?");
                EditorUtils.RepaintAllEditorWindows();
                return OpenedScenes.Select(open => open.ToSceneDataShallow()).ToArray();
            });
        }
    }
}
