#nullable enable
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Data;
using Feeder.MCP.Runtime.Utils;
using Feeder.MCP.Utils;
using Feeder.Logging;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Scene
    {
        public const string SceneUnloadToolId = "scene-unload";
        [AiTool
        (
            SceneUnloadToolId,
            Title = "Scene / Unload"
        )]
        [AiSkillDescription("Unload an opened scene from the Unity Editor (asynchronously via " +
            "`SceneManager.UnloadSceneAsync`). Unsaved changes in that scene are lost — save with '" +
            SceneSaveToolId + "' first if needed. Use '" + Tool_Rql.QueryToolId + "' to find the scene name first.")]
        [AiSkillBody("Unload scene from the Opened scenes in Unity Editor. " +
            "Use '" + Tool_Rql.QueryToolId + "' tool to get the list of all opened scenes.\n\n" +
            "## Warning\n\n" +
            "Any unsaved changes in the unloaded scene are lost. If the scene may have unsaved edits, save it " +
            "with '" + SceneSaveToolId + "' before unloading.\n\n" +
            "## Inputs\n\n" +
            "- `name` — required non-empty scene name. Must match an opened scene; otherwise throws.\n\n" +
            "## Behavior\n\n" +
            "Runs `SceneManager.UnloadSceneAsync` on the main thread and awaits completion. Returns an " +
            "`UnloadSceneResult` containing the scene name and an `AssetObjectRef` to its asset (or `null` if the " +
            "scene was not backed by an asset on disk).")]
        [Description("Unload scene from the Opened scenes in Unity Editor. " +
            "Use '" + Tool_Rql.QueryToolId + "' tool to get the list of all opened scenes.")]
        public Task<UnloadSceneResult> Unload
        (
            [Description("Name of the loaded scene.")]
            string name
        )
        {
            return MainThread.Instance.Run(async () =>
            {
                var logger = UnityLoggerFactory.LoggerFactory.CreateLogger<Tool_Scene>();

                if (string.IsNullOrEmpty(name))
                    throw new ArgumentException(Error.SceneNameIsEmpty(), nameof(name));

                var scene = SceneUtils.GetAllOpenedScenes()
                    .FirstOrDefault(scene => scene.name == name);

                if (!scene.IsValid())
                    throw new ArgumentException(Error.NotFoundSceneWithName(name), nameof(name));

                var scenePath = scene.path;
                logger.LogInformation("Unloading scene '{Name}' at path '{Path}'", name, scenePath);

                var asyncOperation = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);

                while (!asyncOperation.isDone)
                    await Task.Yield();

                logger.LogInformation("Successfully unloaded scene '{Name}'", name);

                var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);

                return new UnloadSceneResult
                {
                    Name = name,
                    AssetObjectRef = sceneAsset == null
                        ? null
                        : new AssetObjectRef(sceneAsset)
                };
            });
        }

    }
}
