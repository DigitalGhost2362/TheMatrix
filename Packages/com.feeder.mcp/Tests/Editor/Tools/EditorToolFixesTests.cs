#nullable enable

using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using Feeder.MCP.Editor.API;
using Feeder.MCP.Editor.API.TestRunner;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using TestMode = UnityEditor.TestTools.TestRunner.Api.TestMode;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class EditorToolFixesTests
    {
        const string TempBaseScenePath = "Assets/__MatrixSceneUnloadBase.unity";
        const string TempScenePath = "Assets/__MatrixSceneUnloadTest.unity";

        [Test]
        public void TestFilter_NamespaceAndClass_AreAndedInOneGroupPattern()
        {
            var filter = Tool_Tests.CreateTestFilter(TestMode.EditMode, new TestFilterParameters(testNamespace: "Game.Tests", testClass: "Combat"));

            Assert.AreEqual(1, filter.groupNames.Length);
            var group = new Regex(filter.groupNames[0]);
            Assert.IsTrue(group.IsMatch("Game.Tests.Combat.Hits"));
            Assert.IsTrue(group.IsMatch("Game.Tests.Sub.Combat.Hits"));
            Assert.IsFalse(group.IsMatch("Other.Combat.Hits"));
            Assert.IsFalse(group.IsMatch("Game.Tests.Movement.Hits"));
        }

        [Test]
        public void TestFilter_AssemblyAndMethod_AreBothApplied()
        {
            var filter = Tool_Tests.CreateTestFilter(TestMode.EditMode, new TestFilterParameters(testAssembly: "Game.Tests", testMethod: "Game.Tests.Combat.Hits"));

            CollectionAssert.AreEqual(new[] { "Game.Tests" }, filter.assemblyNames);
            CollectionAssert.AreEqual(new[] { "Game.Tests.Combat.Hits" }, filter.testNames);
            Assert.IsTrue(filter.groupNames == null || filter.groupNames.Length == 0);
        }

        [UnityTest]
        public IEnumerator SceneUnload_EditMode_ClosesAdditiveScene()
        {
            try
            {
                // an untitled scene blocks additive scene creation, so the base scene is saved first
                Assert.IsTrue(EditorSceneManager.SaveScene(EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single), TempBaseScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                Assert.IsTrue(EditorSceneManager.SaveScene(scene, TempScenePath));

                var task = new Tool_Scene().Unload(scene.name);
                for (var frame = 0; frame < 300 && !task.IsCompleted; frame++)
                    yield return null;

                Assert.IsTrue(task.IsCompleted, "scene-unload did not finish");
                Assert.IsNull(task.Exception, task.Exception?.ToString());
                Assert.IsFalse(Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).path == TempScenePath));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(TempScenePath);
                AssetDatabase.DeleteAsset(TempBaseScenePath);
            }
        }
    }
}
