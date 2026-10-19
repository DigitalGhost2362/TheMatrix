#nullable enable
using System.ComponentModel;
using System.Linq;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.Utils;
using Feeder.MCP.Data;
using Feeder.MCP.Runtime.Extensions;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Scene
    {
        public const string SceneOpenToolId = "scene-open";
        [AiTool
        (
            SceneOpenToolId,
            Title = "Scene / Open"
        )]
        [AiSkillDescription("Open a Unity scene asset in Single or Additive mode. " +
            "Returns the post-open list of all opened scenes. " +
            "Use '" + Tool_Rql.QueryToolId + "' to locate the scene asset first.")]
        [AiSkillBody("Open scene from the project asset file. " +
            "Use '" + Tool_Rql.QueryToolId + "' tool to find the scene asset first.\n\n" +
            "## Inputs\n\n" +
            "- `sceneRef` — `AssetObjectRef` pointing at a `SceneAsset`. Throws if the asset cannot be resolved or " +
            "is not a `SceneAsset`.\n" +
            "- `loadSceneMode` (default `Single`):\n" +
            "  - `Single` — closes the currently opened scenes and opens this one.\n" +
            "  - `Additive` — keeps the currently opened scenes and opens this one alongside them.")]
        [Description("Open scene from the project asset file. " +
            "Use '" + Tool_Rql.QueryToolId + "' tool to find the scene asset first.")]
        public SceneDataShallow[] Open
        (
            AssetObjectRef sceneRef,
            [Description("Open scene mode. " +
                "Single: closes the current scenes and opens a new one. " +
                "Additive: keeps the current scene and opens additional one.")]
            UnityEditor.SceneManagement.OpenSceneMode loadSceneMode = UnityEditor.SceneManagement.OpenSceneMode.Single
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var sceneAsset = sceneRef.FindAssetObject<UnityEditor.SceneAsset>()
                    ?? throw new System.ArgumentException($"Requested scene is not valid or not found.");

                var scenePath = sceneAsset.GetAssetPath();

                var sceneOpened = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, loadSceneMode);

                if (!sceneOpened.IsValid())
                    throw new System.Exception($"Failed to load scene at '{scenePath}'.\n{OpenedScenesText}");

                EditorUtils.RepaintAllEditorWindows();

                return OpenedScenes
                    .Select(scene => scene.ToSceneDataShallow())
                    .ToArray();
            });
        }
    }
}
