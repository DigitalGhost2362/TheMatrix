#nullable enable

using System.IO;
using Feeder.ToolHost;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class MxGuardTests
    {
        static void Refused(TestDelegate call, string hint)
        {
            var ex = Assert.Throws<ToolCallerException>(call);
            StringAssert.Contains(hint, ex!.Message);
        }

        [Test]
        public void NotEmpty_RefusesBlank_AndNamesAnExample()
        {
            Refused(() => MxGuard.NotEmpty(null, "name", "Player"), "e.g. 'Player'");
            Refused(() => MxGuard.NotEmpty("  ", "name"), "'name' is required");
            Assert.AreEqual("Hero", MxGuard.NotEmpty(" Hero ", "name"));
        }

        [Test]
        public void InRange_And_OneOf_RefuseOutsiders()
        {
            Refused(() => MxGuard.InRange(11, 0, 10, "count"), "between 0 and 10");
            Assert.AreEqual(5, MxGuard.InRange(5, 0, 10, "count"));
            Refused(() => MxGuard.OneOf("maybe", new[] { "a", "b" }, "mode"), "one of: a, b");
            Assert.AreEqual("save", MxGuard.IfDirty("SAVE"));
        }

        [TestCase("Assets/../Outside.asset", "'..'")]
        [TestCase("Assets/./A.asset", "'..'")]
        [TestCase("C:/Temp/A.asset", "absolute")]
        [TestCase("/Temp/A.asset", "absolute")]
        [TestCase("Packages/com.x/A.asset", "starting with 'Assets/'")]
        [TestCase("Assets", "starting with 'Assets/'")]
        [TestCase("Library/A.asset", "starting with 'Assets/'")]
        [TestCase("Assets/A.txt", "must end with .asset")]
        public void AssetPath_RefusesEscapes_AndWrongExtension(string path, string hint)
            => Refused(() => MxGuard.AssetPath(path, ".asset"), hint);

        [Test]
        public void AssetPath_NormalizesSlashes_AndReadsMayUsePackages()
        {
            Assert.AreEqual("Assets/A/B.asset", MxGuard.AssetPath("Assets\\A\\B.asset", ".asset"));
            Assert.AreEqual("Packages/com.x/A.uxml", MxGuard.ReadableAssetPath("Packages/com.x/A.uxml", ".uxml"));
            Refused(() => MxGuard.ReadableAssetPath("Packages/../A.uxml"), "'..'");
        }

        [Test]
        public void ProjectFile_AddsTheExtensionBeforeTheCheck()
        {
            Refused(() => MxGuard.ProjectFile(".", ".png"), "outside the project");
            Refused(() => MxGuard.ProjectFile("../x.png"), "outside the project");
            Refused(() => MxGuard.ProjectFile(""), "required");
            var root = Path.GetFullPath(MatrixPluginEditor.ProjectRootPath);
            var full = MxGuard.ProjectFile("Temp/mx-guard", ".png");
            StringAssert.StartsWith(root.TrimEnd('/', '\\'), full);
            StringAssert.EndsWith(Path.Combine("Temp", "mx-guard.png"), full);
        }

        [Test]
        public void PngFile_OnlyWritesPng_AndNeverOverAProjectAsset()
        {
            Refused(() => MxGuard.PngFile("Assets/Scripts/Player.cs"), "must end with .png");
            Refused(() => MxGuard.PngFile("Packages/manifest.json"), "must end with .png");
            Refused(() => MxGuard.PngFile("../shot.png"), "outside the project");
            StringAssert.EndsWith(Path.Combine("Temp", "mx-guard-shot.png"), MxGuard.PngFile("Temp/mx-guard-shot"));

            // a capture may replace an earlier capture outside the asset folders, never an imported asset
            var temp = "Temp/mx-guard-shot-old.png";
            var asset = "Assets/_MxTestGuardShot.png";
            File.WriteAllText(temp, "x");
            File.WriteAllText(asset, "x");
            try
            {
                Assert.DoesNotThrow(() => MxGuard.PngFile(temp));
                Refused(() => MxGuard.PngFile(asset), "is a project asset");
                Assert.DoesNotThrow(() => MxGuard.PngFile("Assets/_MxTestGuardShotNew.png"));
            }
            finally
            {
                File.Delete(temp);
                File.Delete(asset);
                File.Delete(asset + ".meta");
            }
        }

        [Test]
        public void NotExisting_RefusesAnExistingFile()
        {
            var path = "Temp/mx-guard-exists.txt";
            File.WriteAllText(path, "x");
            try
            {
                Refused(() => MxGuard.NotExisting(path), "already exists");
                Refused(() => MxGuard.NotExisting("Temp"), "already exists");
                Assert.AreEqual("Temp/mx-guard-missing.txt", MxGuard.NotExisting("Temp/mx-guard-missing.txt"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void NotPlaying_RefusesInPlayMode_WithTheWayOut()
        {
            var original = MxGuard.IsPlaying;
            try
            {
                MxGuard.IsPlaying = () => true;
                Refused(() => MxGuard.NotPlaying("create a scene"), "Leave play mode first");
                MxGuard.IsPlaying = () => false;
                Assert.DoesNotThrow(() => MxGuard.NotPlaying("create a scene"));
            }
            finally
            {
                MxGuard.IsPlaying = original;
            }
        }

        [Test]
        public void ScenesSaved_RefusesUnsavedScenes_UnlessDiscard()
        {
            MxTestScene.Park();
            var temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.MoveGameObjectToScene(new GameObject("MxGuardDirty"), temp);
                EditorSceneManager.MarkSceneDirty(temp);

                Refused(() => MxGuard.ScenesSaved(MxGuard.Refuse, temp), "[unsaved_scenes]");
                Refused(() => MxGuard.ScenesSaved(MxGuard.Save, temp), "untitled");
                Refused(() => MxGuard.ScenesSaved("maybe", temp), "one of: refuse, save, discard");
                Assert.DoesNotThrow(() => MxGuard.ScenesSaved(MxGuard.Discard, temp));
                Assert.IsTrue(temp.isLoaded && temp.isDirty, "the guard itself never closes or cleans a scene");
            }
            finally
            {
                EditorSceneManager.CloseScene(temp, true);
                MxTestScene.Cleanup();
            }
        }
    }
}
