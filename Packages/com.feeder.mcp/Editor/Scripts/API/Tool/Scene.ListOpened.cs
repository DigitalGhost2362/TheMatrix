#nullable enable
using System.ComponentModel;
using System.Linq;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Data;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Scene
    {
        public const string SceneListOpenedToolId = "scene-list-opened";
        [AiTool
        (
            SceneListOpenedToolId,
            Title = "Scene / List Opened",
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [AiSkillDescription("List every scene currently opened in the Unity Editor as a shallow snapshot " +
            "(name, path, build flags). Use '" + SceneGetDataToolId + "' for the deep view of a specific scene.")]
        [AiSkillBody("Returns the list of currently opened scenes in Unity Editor. " +
            "Use '" + SceneGetDataToolId + "' tool to get detailed information about a specific scene.\n\n" +
            "## Behavior\n\n" +
            "Maps `OpenedScenes` through `ToSceneDataShallow()` on the main thread and returns the resulting array. " +
            "No filtering or pagination — every opened scene is included.")]
        [Description("Returns the list of currently opened scenes in Unity Editor. " +
            "Use '" + SceneGetDataToolId + "' tool to get detailed information about a specific scene.")]
        public SceneDataShallow[] ListOpened(string? nothing = null)
        {
            return MainThread.Instance.Run(() =>
            {
                return OpenedScenes
                    .Select(scene => scene.ToSceneDataShallow())
                    .ToArray();
            });
        }
    }
}
