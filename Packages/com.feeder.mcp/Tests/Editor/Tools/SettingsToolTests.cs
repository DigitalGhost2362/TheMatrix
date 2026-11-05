#nullable enable

using System;
using System.Linq;
using Feeder.MCP.Editor.API;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class SettingsToolTests
    {
        const string TagName = "MxTestTagG2";
        const string LayerName = "MxTestLayerG2";
        const string SortingName = "MxTestSortingG2";

        readonly Tool_Settings tool = new Tool_Settings();
        EditorBuildSettingsScene[] buildBefore = Array.Empty<EditorBuildSettingsScene>();
        string timeBefore = string.Empty;

        [SetUp]
        public void SetUp()
        {
            buildBefore = EditorBuildSettings.scenes;
            timeBefore = tool.Get("time", "Maximum Allowed Timestep").Properties.Single().Value;
        }

        [TearDown]
        public void TearDown()
        {
            tool.Tags("remove-tag", TagName);
            try
            {
                tool.Tags("clear-layer", LayerName);
            }
            catch (ArgumentException)
            {
            }
            tool.Tags("remove-sorting-layer", SortingName);
            tool.Set("time", new[] { "Maximum Allowed Timestep=" + timeBefore });
            EditorBuildSettings.scenes = buildBefore;
        }

        [Test]
        public void Tags_AddAndRemove()
        {
            var added = tool.Tags("add-tag", TagName);
            CollectionAssert.Contains(added.Tags, TagName);
            Assert.AreEqual("Matrix add-tag", Undo.GetCurrentGroupName());

            var removed = tool.Tags("remove-tag", TagName);
            CollectionAssert.DoesNotContain(removed.Tags, TagName);
        }

        [Test]
        public void Tags_AddingAnExistingTagIsReportedNotDuplicated()
        {
            tool.Tags("add-tag", TagName);
            var again = tool.Tags("add-tag", TagName);

            StringAssert.Contains("already exists", again.Message);
            Assert.AreEqual(1, again.Tags.Count(t => t == TagName));
        }

        [Test]
        public void Tags_BuiltInTagCannotBeRemoved()
        {
            Assert.Throws<ArgumentException>(() => tool.Tags("remove-tag", "Player"));
        }

        [Test]
        public void Layers_SetUsesFirstFreeIndex_AndClearEmptiesIt()
        {
            var set = tool.Tags("set-layer", LayerName);
            var layer = set.Layers.Single(l => l.Name == LayerName);
            Assert.GreaterOrEqual(layer.Index, Tool_Settings.FirstUserLayer);
            Assert.AreEqual(layer.Index, LayerMask.NameToLayer(LayerName));

            var cleared = tool.Tags("clear-layer", LayerName);
            Assert.IsFalse(cleared.Layers.Any(l => l.Name == LayerName));
        }

        [Test]
        public void Layers_BuiltInIndexIsRefused()
        {
            Assert.Throws<ArgumentException>(() => tool.Tags("set-layer", LayerName, 3));
        }

        [Test]
        public void SortingLayers_AddAndRemove_DefaultIsProtected()
        {
            CollectionAssert.Contains(tool.Tags("add-sorting-layer", SortingName).SortingLayers, SortingName);
            CollectionAssert.DoesNotContain(tool.Tags("remove-sorting-layer", SortingName).SortingLayers, SortingName);
            Assert.Throws<ArgumentException>(() => tool.Tags("remove-sorting-layer", "Default"));
        }

        [Test]
        public void Tags_UnknownActionThrows()
        {
            Assert.Throws<ArgumentException>(() => tool.Tags("explode"));
        }

        [Test]
        public void Get_ReadsAndSet_WritesAPhysicsValue_WithOneUndoStep()
        {
            var result = tool.Set("time", new[] { "Maximum Allowed Timestep=0.25" });

            Assert.AreEqual(0, result.Failed);
            Assert.AreEqual(0.25f, Time.maximumDeltaTime, 1e-6f);
            Assert.AreEqual("0.25", tool.Get("time", "Maximum Allowed Timestep").Properties.Single().Value);
            Assert.AreEqual("Matrix settings set", Undo.GetCurrentGroupName());
        }

        [Test]
        public void Set_ReportsABadValue_AndStillAppliesTheOthers()
        {
            var result = tool.Set("time", new[] { "Maximum Allowed Timestep=abc", "NoSuchProperty=1", "Maximum Allowed Timestep=0.3" });

            Assert.AreEqual(2, result.Failed);
            Assert.AreEqual(1, result.Changed);
            Assert.AreEqual(0.3f, Time.maximumDeltaTime, 1e-6f);
        }

        [Test]
        public void Get_PhysicsGravityIsAVector()
        {
            var gravity = tool.Get("physics", "m_Gravity").Properties.Single();

            Assert.AreEqual("Vector3", gravity.Type);
            Assert.AreEqual(3, gravity.Value.Split(',').Length);
        }

        [Test]
        public void Get_WholeAreaIsListedAndCappedByLimit()
        {
            var result = tool.Get("player", null, 1, 5);

            Assert.AreEqual(5, result.Properties.Count);
            Assert.IsTrue(result.Truncated);
        }

        [Test]
        public void Get_UnknownAreaOrPathThrows()
        {
            Assert.Throws<ArgumentException>(() => tool.Get("nope"));
            Assert.Throws<ArgumentException>(() => tool.Get("time", "NoSuchProperty"));
        }

        [Test]
        public void BuildScenes_AddMoveDisableRemove_ThenRestored()
        {
            var guid = AssetDatabase.FindAssets("t:Scene").FirstOrDefault();
            Assume.That(guid, Is.Not.Null, "The project has no scene asset.");
            var path = AssetDatabase.GUIDToAssetPath(guid);
            Assume.That(path.StartsWith("Assets/"), "Needs a scene under Assets.");
            Assume.That(buildBefore.Any(s => s.path == path), Is.False, "The scene is already in the build.");

            var added = tool.BuildScenes("add", path, enabled: false);
            Assert.AreEqual(buildBefore.Length + 1, added.Scenes.Count);
            Assert.IsFalse(added.Scenes.Single(s => s.Path == path).Enabled);

            var enabled = tool.BuildScenes("set-enabled", path, enabled: true);
            Assert.IsTrue(enabled.Scenes.Single(s => s.Path == path).Enabled);

            var moved = tool.BuildScenes("move", path, toIndex: 0);
            Assert.AreEqual(path, moved.Scenes[0].Path);

            var removed = tool.BuildScenes("remove", path);
            Assert.AreEqual(buildBefore.Length, removed.Scenes.Count);
            Assert.IsFalse(removed.Scenes.Any(s => s.Path == path));
        }

        [Test]
        public void BuildScenes_RejectsAMissingSceneAndAnUnknownAction()
        {
            Assert.Throws<ArgumentException>(() => tool.BuildScenes("add", "Assets/DoesNotExist.unity"));
            Assert.Throws<ArgumentException>(() => tool.BuildScenes("explode"));
        }
    }
}
