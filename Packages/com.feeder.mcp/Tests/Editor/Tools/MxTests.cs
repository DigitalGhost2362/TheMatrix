#nullable enable

using System.IO;
using Feeder.ToolHost;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class MxTests
    {
        const string Folder = "Assets/_MxTestMx";

        [SetUp]
        public void SetUp() => MxTestScene.Park();

        [TearDown]
        public void TearDown()
        {
            MxTestScene.Cleanup();
            AssetDatabase.DeleteAsset(Folder);
        }

        [Test]
        public void EnsureFolder_CreatesEveryLevel()
        {
            Mx.EnsureFolder(Folder + "/A/B/C");
            Assert.IsTrue(AssetDatabase.IsValidFolder(Folder + "/A/B/C"));
            Assert.DoesNotThrow(() => Mx.EnsureFolder(Folder + "/A/B/C"));
        }

        [Test]
        public void AnAssetDirectlyUnderAssets_IsCreated()
        {
            const string path = "Assets/_MxTestRootAsset.mat";
            try
            {
                Assert.AreEqual("Assets", Mx.EnsureFolder("Assets"));
                Assert.AreEqual("Assets", Mx.EnsureFolder("Assets/"));
                Mx.CreateAsset(new Material(Shader.Find("Hidden/InternalErrorShader")), path);
                Assert.IsTrue(File.Exists(path));
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        [Test]
        public void CreateAsset_MakesTheFolder_AndNeverOverwrites()
        {
            var path = Folder + "/Deep/M.mat";
            Mx.CreateAsset(new Material(Shader.Find("Hidden/InternalErrorShader")), path);
            var before = File.ReadAllBytes(path);

            Assert.Throws<ToolCallerException>(() => Mx.CreateAsset(new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "Other" }, path));

            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            Assert.Throws<ToolCallerException>(() => Mx.CreateAsset(new Material(Shader.Find("Hidden/InternalErrorShader")), "Library/M.mat"));
        }

        [Test]
        public void TrashAsset_RemovesTheFile_AndRefusesTheAssetsRoot()
        {
            var path = Folder + "/T.mat";
            Mx.CreateAsset(new Material(Shader.Find("Hidden/InternalErrorShader")), path);

            Mx.TrashAsset(path);

            Assert.IsFalse(File.Exists(path));
            Assert.Throws<ToolCallerException>(() => Mx.TrashAsset("Assets"));
            Assert.Throws<ToolCallerException>(() => Mx.TrashAsset("Packages/com.feeder.mcp/package.json"));
        }

        [Test]
        public void NewScene_RefusesAnExistingPath_AndUnsavedScenes()
        {
            var path = MxTestScene.Folder + "/New.unity";
            var scene = Mx.NewScene(path, additive: true, empty: true);
            Assert.IsTrue(File.Exists(path));
            Assert.AreEqual(path, scene.path);

            Assert.Throws<ToolCallerException>(() => Mx.NewScene(path, additive: true));

            SceneManager.MoveGameObjectToScene(new GameObject("MxDirty"), scene);
            EditorSceneManager.MarkSceneDirty(scene);
            var ex = Assert.Throws<ToolCallerException>(() => Mx.NewScene(MxTestScene.Folder + "/Other.unity"));
            StringAssert.Contains("[unsaved_scenes]", ex!.Message);
            Assert.IsTrue(scene.isLoaded, "the unsaved scene stays open");
            Assert.IsFalse(File.Exists(MxTestScene.Folder + "/Other.unity"));
        }

        [Test]
        public void OpenScene_And_SaveScene_RoundTrip()
        {
            var path = MxTestScene.Folder + "/Open.unity";
            var created = Mx.NewScene(path, additive: true, empty: true);
            EditorSceneManager.CloseScene(created, true);

            var scene = Mx.OpenScene(path, additive: true);
            new GameObject("MxSaved").transform.SetParent(null);
            SceneManager.MoveGameObjectToScene(GameObject.Find("MxSaved"), scene);

            Assert.AreEqual(path, Mx.SaveScene(scene));
            Assert.IsFalse(scene.isDirty);
            StringAssert.Contains("MxSaved", File.ReadAllText(path));
            Assert.Throws<ToolCallerException>(() => Mx.OpenScene(MxTestScene.Folder + "/Missing.unity", additive: true));
        }

        [Test]
        public void Instantiate_KeepsThePrefabLink_AndIsOneUndoStep()
        {
            var prefabPath = SavePrefab("P");

            Undo.IncrementCurrentGroup();
            var instance = Mx.Instantiate(prefabPath);
            Assert.IsTrue(PrefabUtility.IsPartOfPrefabInstance(instance));
            Assert.AreEqual(prefabPath, PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance));

            Undo.PerformUndo();
            Assert.IsTrue(instance == null, "Undo removes the instance");
        }

        [Test]
        public void EditPrefab_SavesTheChange_AndLeavesTheFileWhenTheEditThrows()
        {
            var prefabPath = SavePrefab("E");
            Mx.EditPrefab(prefabPath, root => new GameObject("Child").transform.SetParent(root.transform));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).transform.Find("Child"));

            var before = File.ReadAllBytes(prefabPath);
            Assert.Throws<System.InvalidOperationException>(() => Mx.EditPrefab(prefabPath, _ => throw new System.InvalidOperationException("boom")));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(prefabPath));
        }

        [Test]
        public void Call_ReachesPrivateAndStaticMembers_AndExplainsAMismatch()
        {
            var target = new CallTarget();
            Assert.AreEqual(5, Mx.Call(target, "Add", 2, 3));
            Assert.AreEqual("static:x", Mx.Call(typeof(CallTarget), "Static", "x"));
            Assert.AreEqual(7, Mx.Call(target, "WithDefault"));

            var ex = Assert.Throws<ToolCallerException>(() => Mx.Call(target, "Add", "a"));
            StringAssert.Contains("Overloads: (Int32, Int32)", ex!.Message);
            Assert.Throws<ToolCallerException>(() => Mx.Call(target, "Missing"));
        }

        [Test]
        public void Shot_SavesACameraPng_InsideTheProjectOnly()
        {
            var camera = new GameObject("MxShotCamera").AddComponent<Camera>();
            var path = "Temp/mx-shot-test";
            try
            {
                var full = Mx.Shot(path, camera);
                Assert.IsTrue(full.EndsWith(".png") && File.Exists(full));
                Assert.Throws<ToolCallerException>(() => Mx.Shot(".", camera));
                File.Delete(full);
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        static string SavePrefab(string name)
        {
            Mx.EnsureFolder(Folder);
            var go = new GameObject(name);
            var path = $"{Folder}/{name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return path;
        }

        sealed class CallTarget
        {
            int Add(int a, int b) => a + b;
            static string Static(string s) => "static:" + s;
            int WithDefault(int x = 7) => x;
        }
    }
}
