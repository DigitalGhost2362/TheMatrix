#nullable enable

using System.Collections.Generic;
using System.Reflection;
using Feeder.MCP.Editor.API;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class PrefabInstantiateUndoTests
    {
        string prefabPath = string.Empty;
        Scene previousActive;
        Scene tempScene;
        GameObject? parent;

        [SetUp]
        public void SetUp()
        {
            prefabPath = $"Assets/MatrixUndoTest_{System.Guid.NewGuid():N}.prefab";
            var source = new GameObject("MatrixUndoSource");
            PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
            Object.DestroyImmediate(source);

            parent = null;
            tempScene = default;
            previousActive = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(previousActive.path))
                return;
            tempScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(tempScene);
        }

        [TearDown]
        public void TearDown()
        {
            if (parent != null)
                Object.DestroyImmediate(parent);
            if (previousActive.IsValid())
                SceneManager.SetActiveScene(previousActive);
            if (tempScene.IsValid())
                EditorSceneManager.CloseScene(tempScene, true);
            AssetDatabase.DeleteAsset(prefabPath);
        }

        static List<string> UndoRecords()
        {
            var undo = new List<string>();
            var method = typeof(Undo).GetMethod("GetRecords", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
                new[] { typeof(List<string>), typeof(List<string>) }, null);
            method?.Invoke(null, new object[] { undo, new List<string>() });
            return undo;
        }

        [Test]
        public void Instantiate_RegistersOneUndoStepThatRemovesTheObject()
        {
            parent = new GameObject("MatrixUndoParent");
            if (tempScene.IsValid())
                SceneManager.MoveGameObjectToScene(parent, tempScene);

            new Tool_Assets_Prefab().Instantiate(prefabPath, "MatrixUndoParent/Spawned");

            var spawned = parent.transform.Find("Spawned");
            Assert.IsNotNull(spawned);

            var records = UndoRecords();
            if (records.Count > 0)
                Assert.IsTrue(records.Exists(r => r.StartsWith("Instantiate")), "Undo records: " + string.Join(" | ", records));
            Assert.IsTrue(Undo.GetCurrentGroupName().StartsWith("Instantiate"), Undo.GetCurrentGroupName());

            Undo.PerformUndo();

            Assert.IsTrue(parent.transform.Find("Spawned") == null, "Undo should destroy the instantiated object.");
        }
    }
}
