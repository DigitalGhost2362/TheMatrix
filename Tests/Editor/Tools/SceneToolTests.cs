#nullable enable

using System;
using System.IO;
using Feeder.MCP.Editor.API;
using Feeder.ToolHost;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class SceneToolTests
    {
        const string Folder = MxTestScene.Folder;
        const string ScenePath = Folder + "/A.unity";
        const string CopyPath = Folder + "/B.unity";

        readonly Tool_Scene tool = new Tool_Scene();

        [SetUp]
        public void SetUp()
        {
            MxTestScene.Park();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);
        }

        [TearDown]
        public void TearDown()
        {
            MxTestScene.Cleanup();
        }

        [Test]
        public void Open_Single_RefusesWhileAnOpenSceneHasUnsavedChanges()
        {
            var temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.MoveGameObjectToScene(new GameObject("MxTestDirty"), temp);
                EditorSceneManager.MarkSceneDirty(temp);

                var ex = Assert.Throws<ToolCallerException>(() => tool.Open(ScenePath));
                StringAssert.Contains("unsaved_scenes", ex!.Message);
                Assert.IsTrue(temp.isLoaded, "the dirty scene must stay open");
            }
            finally
            {
                EditorSceneManager.CloseScene(temp, true);
            }
        }

        [Test]
        public void Open_RejectsAMissingSceneAndABadIfDirtyValue()
        {
            Assert.Throws<ToolCallerException>(() => tool.Open(Folder + "/Missing.unity", OpenSceneMode.Additive));
            Assert.Throws<ToolCallerException>(() => tool.Open(ScenePath, OpenSceneMode.Additive, "maybe"));
        }

        [Test]
        public void Save_PathOnASavedScene_WritesACopy_AndKeepsTheSceneOnItsFile()
        {
            tool.Open(ScenePath, OpenSceneMode.Additive);

            StringAssert.Contains("copy", tool.Save(ScenePath, CopyPath));

            Assert.IsTrue(File.Exists(CopyPath));
            Assert.IsTrue(SceneManager.GetSceneByPath(ScenePath).IsValid());
            Assert.IsFalse(SceneManager.GetSceneByPath(CopyPath).IsValid());
        }
    }
}
