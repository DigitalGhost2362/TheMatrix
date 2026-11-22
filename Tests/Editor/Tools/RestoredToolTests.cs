#nullable enable

using System.IO;
using System.Linq;
using Feeder.MCP.Data;
using Feeder.MCP.Editor.API;
using Feeder.ToolHost;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class RestoredToolTests
    {
        const string Folder = "Assets/_MxTestRestored";
        const string ScenesFolder = MxTestScene.Folder;

        readonly Tool_Scene scenes = new Tool_Scene();
        readonly Tool_Assets assets = new Tool_Assets();
        readonly Tool_Assets_Prefab prefabs = new Tool_Assets_Prefab();
        readonly Tool_UiToolkit ui = new Tool_UiToolkit();

        [SetUp]
        public void SetUp()
        {
            MxTestScene.Park();
            Mx.EnsureFolder(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null)
                StageUtility.GoToMainStage();
            Tool_Assets_Prefab.OnInstantiated = null;
            MxTestScene.Cleanup();
            AssetDatabase.DeleteAsset(Folder);
        }

        static Scene DirtyAdditiveScene(string? savePath = null)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            if (savePath != null)
                EditorSceneManager.SaveScene(scene, savePath);
            SceneManager.MoveGameObjectToScene(new GameObject("MxDirty"), scene);
            EditorSceneManager.MarkSceneDirty(scene);
            return scene;
        }

        static string Hash(string path) => System.Convert.ToBase64String(System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(path)));

        [Test]
        public void SceneCreate_Single_RefusesWhileASceneIsDirty_AndNeverOverwrites()
        {
            var dirty = DirtyAdditiveScene();
            var path = ScenesFolder + "/Created.unity";

            var ex = Assert.Throws<ToolCallerException>(() => scenes.Create(path));
            StringAssert.Contains("[unsaved_scenes]", ex!.Message);
            Assert.IsTrue(dirty.isLoaded, "the dirty scene stays open");
            Assert.IsFalse(File.Exists(path));

            EditorSceneManager.CloseScene(dirty, true);
            scenes.Create(path, NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var before = Hash(path);
            Assert.Throws<ToolCallerException>(() => scenes.Create(path, NewSceneSetup.EmptyScene, NewSceneMode.Additive));
            Assert.AreEqual(before, Hash(path));
        }

        [Test]
        public void SceneCreate_MakesMissingFolders()
        {
            var path = ScenesFolder + "/Deep/Er/New.unity";
            scenes.Create(path, NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            Assert.IsTrue(File.Exists(path));
        }

        [Test]
        public void SceneUnload_RefusesADirtyScene_UnlessDiscard()
        {
            var path = ScenesFolder + "/Unload.unity";
            var scene = DirtyAdditiveScene(path);

            var refused = scenes.Unload(path);
            var ex = Assert.Throws<ToolCallerException>(() => refused.GetAwaiter().GetResult());
            StringAssert.Contains("[unsaved_scenes]", ex!.Message);
            Assert.IsTrue(scene.isLoaded);

            var done = scenes.Unload(path, "discard");
            Assert.IsTrue(done.IsCompleted);
            Assert.IsFalse(SceneManager.GetSceneByPath(path).IsValid());
        }

        [Test]
        public void SceneSetActive_PicksByPath_WhenTwoScenesShareAName()
        {
            var a = ScenesFolder + "/A/Same.unity";
            var b = ScenesFolder + "/B/Same.unity";
            Mx.NewScene(a, additive: true, empty: true);
            Mx.NewScene(b, additive: true, empty: true);

            scenes.SetActive(b);
            Assert.AreEqual(b, SceneManager.GetActiveScene().path);
            scenes.SetActive(a);
            Assert.AreEqual(a, SceneManager.GetActiveScene().path);
            var ex = Assert.Throws<ToolCallerException>(() => scenes.SetActive("Same"));
            StringAssert.Contains("Pass the path", ex!.Message);
        }

        [Test]
        public void MaterialCreate_SecondCallOnTheSamePath_FailsAndKeepsTheFile()
        {
            var path = Folder + "/M.mat";
            assets.CreateMaterial(path, "Hidden/InternalErrorShader");
            var before = Hash(path);

            Assert.Throws<ToolCallerException>(() => assets.CreateMaterial(path, "Sprites/Default"));

            Assert.AreEqual(before, Hash(path));
        }

        [Test]
        public void PrefabCreate_SecondCallOnTheSamePath_FailsAndKeepsTheFile()
        {
            var go = new GameObject("MxPrefabSource");
            try
            {
                var path = Folder + "/P.prefab";
                prefabs.Create(path, new GameObjectRef(go), connectGameObjectToPrefab: false);
                var before = Hash(path);

                var ex = Assert.Throws<ToolCallerException>(() => prefabs.Create(path, new GameObjectRef(go), connectGameObjectToPrefab: false));

                Assert.AreEqual(before, Hash(path));
                Assert.Less(ex!.Message.Length, 300, "the error must not list every prefab in the project");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Delete_RefusesPackagesAndTheRoot_AndTrashesByDefault()
        {
            var path = Folder + "/T.mat";
            Mx.CreateAsset(new Material(Shader.Find("Hidden/InternalErrorShader")), path);

            var result = assets.Delete(new[] { "Packages/com.feeder.mcp/package.json", "Assets", "Assets/../ProjectSettings/x.asset", Folder + "/Missing.mat", path });

            CollectionAssert.AreEqual(new[] { path }, result.DeletedPaths);
            Assert.AreEqual(4, result.Errors!.Count);
            Assert.IsFalse(File.Exists(path));
            Assert.IsTrue(File.Exists("Packages/com.feeder.mcp/package.json"));
        }

        [Test]
        public void CreateFolder_MakesEveryNestedLevel()
        {
            var result = assets.CreateFolders(new[] { new CreateFolderInput { ParentFolderPath = Folder, NewFolderName = "A/B/C" } });

            Assert.IsNull(result.Errors);
            Assert.IsTrue(AssetDatabase.IsValidFolder(Folder + "/A"));
            Assert.IsTrue(AssetDatabase.IsValidFolder(Folder + "/A/B"));
            Assert.IsTrue(AssetDatabase.IsValidFolder(Folder + "/A/B/C"));
        }

        [Test]
        public void CopyAndMove_RefuseADestinationOutsideAssets_AndCreateNothing()
        {
            var path = Folder + "/C.mat";
            Mx.CreateAsset(new Material(Shader.Find("Hidden/InternalErrorShader")), path);
            var outside = "Library/MxTestCopy.mat";
            File.Delete(outside);

            var copy = assets.Copy(new[] { path }, new[] { outside });
            var move = assets.Move(new[] { path }, new[] { "Library/MxTestMove.mat" });

            Assert.IsNull(copy.CopiedAssets);
            Assert.AreEqual(1, copy.Errors!.Count);
            Assert.IsNull(move.MovedPaths);
            Assert.IsFalse(File.Exists(outside));
            Assert.IsFalse(File.Exists("Library/MxTestMove.mat"));
            Assert.IsTrue(File.Exists(path));
        }

        [Test]
        public void PrefabOpen_NullRef_IsACallerError_NotANullReference()
        {
            Assert.Throws<ToolCallerException>(() => prefabs.Open(null!));
        }

        [Test]
        public void PrefabClose_WithoutSave_RefusesUnsavedChanges_UnlessDiscard()
        {
            var path = Folder + "/S.prefab";
            var go = new GameObject("MxStage");
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.OpenPrefab(path);
            new GameObject("MxChild").transform.SetParent(stage.prefabContentsRoot.transform);
            EditorSceneManager.MarkSceneDirty(stage.scene);

            var ex = Assert.Throws<ToolCallerException>(() => prefabs.Close(save: false));
            StringAssert.Contains("discard=true", ex!.Message);
            Assert.IsNotNull(UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage());

            prefabs.Close(save: false, discard: true);
            Assert.IsNull(UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage());
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(path).transform.Find("MxChild"));
        }

        [Test]
        public void PrefabInstantiate_FailingHalfway_LeavesNoInstanceBehind()
        {
            var path = Folder + "/I.prefab";
            var go = new GameObject("MxHalf");
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            Tool_Assets_Prefab.OnInstantiated = _ => throw new System.InvalidOperationException("boom");

            Assert.Throws<System.InvalidOperationException>(() => prefabs.Instantiate(path, "MxHalfSpawn"));

            Assert.IsFalse(Resources.FindObjectsOfTypeAll<GameObject>().Any(o => o.name == "MxHalf" && o.scene.IsValid()),
                "the half-made instance was reverted");
        }

        [Test]
        public void AttachDocument_WrongParent_WritesNoPanelSettingsFile()
        {
            var uxml = Folder + "/A.uxml";
            ui.UiToolkitWrite(uxml);
            var panel = Folder + "/Panel.asset";

            Assert.Throws<ToolCallerException>(() => ui.AttachDocument("MxNoSuchParent/Hud", uxml, panel));

            Assert.IsFalse(File.Exists(panel));
        }

        [TestCase(".a { background-image: url(\"x{y}.png\"); }")]
        [TestCase(".a { -unity-font: url('f}.ttf'); }")]
        [TestCase(".a:hover { content: \"{\"; }")]
        [TestCase(".a { background-image: url(x}.png); }")]
        public void Validate_Uss_IgnoresBracesInStringsAndUrls(string uss)
            => Assert.IsNull(Tool_UiToolkit.Validate(uss, false));

        [Test]
        public void Validate_Uss_StillCatchesRealErrors()
        {
            Assert.IsNotNull(Tool_UiToolkit.Validate(".a { color: red;", false));
            Assert.IsNotNull(Tool_UiToolkit.Validate(".a { content: \"open; }", false));
        }
    }
}
