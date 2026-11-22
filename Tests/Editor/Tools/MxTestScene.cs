#nullable enable

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Feeder.MCP.Editor.Tests.Tools
{
    // the Test Runner opens an untitled scene and Unity refuses additive scenes next to one, so park on a saved empty scene
    static class MxTestScene
    {
        public const string Folder = "Assets/_MxTestScenes";
        const string ParkPath = Folder + "/_Park.unity";
        static bool parked;

        public static void Park()
        {
            EnsureFolder();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                if (!string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                    continue;
                var park = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(park, ParkPath);
                parked = true;
                return;
            }
        }

        public static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", "_MxTestScenes");
        }

        // closes every scene saved under the folder, then deletes it
        public static void Cleanup()
        {
            if (parked)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                parked = false;
            }
            for (var i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.path.StartsWith(Folder + "/") && SceneManager.sceneCount > 1)
                    EditorSceneManager.CloseScene(scene, true);
            }
            if (SceneManager.GetSceneAt(0).path.StartsWith(Folder + "/"))
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(Folder);
        }
    }
}
