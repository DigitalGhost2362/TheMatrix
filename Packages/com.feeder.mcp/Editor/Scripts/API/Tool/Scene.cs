#nullable enable
using System.Collections.Generic;
using System.Linq;
using Feeder.ToolHost;
using Feeder.MCP.Runtime.Utils;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_Scene
    {
        public static IEnumerable<UnityEngine.SceneManagement.Scene> OpenedScenes => SceneUtils.GetAllOpenedScenes();
        public static string OpenedScenesText
            => $"Opened Scenes:\n{string.Join("\n", SceneUtils.GetAllOpenedScenes().Select(scene => scene.name))}";

        // an exact path wins; a name must match exactly one open scene
        internal static UnityEngine.SceneManagement.Scene FindOpenScene(string pathOrName)
        {
            var key = MxGuard.NotEmpty(pathOrName, "scene", "Assets/Scenes/Main.unity").Replace('\\', '/');
            var byPath = OpenedScenes.FirstOrDefault(scene => scene.path == key);
            if (byPath.IsValid())
                return byPath;
            var byName = OpenedScenes.Where(scene => scene.name == key).ToArray();
            if (byName.Length == 1)
                return byName[0];
            var open = string.Join(", ", OpenedScenes.Select(scene => string.IsNullOrEmpty(scene.path) ? scene.name + " (untitled)" : scene.path));
            throw MxGuard.Fail(byName.Length == 0
                ? $"No open scene '{key}'. Open scenes: {open}."
                : $"{byName.Length} open scenes are named '{key}'. Pass the path instead: {open}.");
        }

        public static class Error
        {
            static string ScenesPrinted => string.Join("\n", SceneUtils.GetAllOpenedScenes().Select(scene => scene.name));

            public static string NotFoundSceneWithName(string? name)
                => $"Scene '{name ?? "null"}' not found. Available scenes:\n{ScenesPrinted}";
            public static string ScenePathIsEmpty()
                => "Scene path is empty. Please provide a valid path. Sample: \"Assets/Scenes/MyScene.unity\".";
            public static string FilePathMustEndsWithUnity()
                => "File path must end with '.unity'. Please provide a valid path. Sample: \"Assets/Scenes/MyScene.unity\".";
        }
    }
}
