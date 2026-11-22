#nullable enable

using System;
using System.IO;
using System.Linq;
using Feeder.MCP.Editor.API;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Feeder.MCP.Editor.Tests.Tools
{
    // volumes and renderer features are exercised on temporary copies under Assets/_MxTest3D4; project assets and scenes are never changed
    public sealed class RenderingTests
    {
        const string Folder = "Assets/_MxTest3D4";
        readonly Tool_Rendering tool = new Tool_Rendering();
        Scene previousActive;
        Scene temp;

        [SetUp]
        public void SetUp()
        {
            previousActive = SceneManager.GetActiveScene();
            temp = default;
            if (!string.IsNullOrEmpty(previousActive.path))
            {
                temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(temp);
            }
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", "_MxTest3D4");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name.StartsWith("MxTest"))
                    UnityEngine.Object.DestroyImmediate(root);
            if (previousActive.IsValid() && previousActive.isLoaded)
                SceneManager.SetActiveScene(previousActive);
            if (temp.IsValid())
                EditorSceneManager.CloseScene(temp, true);
            AssetDatabase.DeleteAsset(Folder);
        }

        static Type? VolumeType => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEngine.Rendering.Volume", false)).FirstOrDefault(t => t != null);
        static Type? ProfileType => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEngine.Rendering.VolumeProfile", false)).FirstOrDefault(t => t != null);

        string NewProfileAsset()
        {
            if (ProfileType == null || VolumeType == null)
                Assert.Ignore("The render-pipelines core package is not installed.");
            var path = Folder + "/Profile.asset";
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance(ProfileType!), path);
            return path;
        }

        [Test]
        public void VolumeSet_OnProfileAsset_AddsComponentAndSetsOverride()
        {
            var path = NewProfileAsset();

            var result = tool.VolumeSet(path, "Vignette", "intensity", "0.45");

            StringAssert.Contains("intensity = 0.45 [override]", result);
            Assert.IsTrue(tool.VolumeGet(path).Contains("Vignette active=True"));
            StringAssert.Contains("intensity = 0.45", tool.VolumeGet(path));
        }

        [Test]
        public void VolumeSet_OverrideStateFalse_HidesParameterFromDefaultListing()
        {
            var path = NewProfileAsset();
            tool.VolumeSet(path, "Vignette", "intensity", "0.2");

            tool.VolumeSet(path, "Vignette", "intensity", overrideState: false);

            StringAssert.DoesNotContain("intensity", tool.VolumeGet(path));
            StringAssert.Contains("intensity = 0.2", tool.VolumeGet(path, includeAll: true));
        }

        [Test]
        public void VolumeSet_UnknownParameter_ListsTheRealOnes()
        {
            var path = NewProfileAsset();
            var ex = Assert.Throws<ArgumentException>(() => tool.VolumeSet(path, "Vignette", "nope", "1"))!;
            StringAssert.Contains("intensity", ex.Message);
        }

        [Test]
        public void VolumeSet_AddIfMissingFalse_Throws()
        {
            var path = NewProfileAsset();
            Assert.Throws<InvalidOperationException>(() => tool.VolumeSet(path, "Vignette", addIfMissing: false));
        }

        [Test]
        public void VolumeSet_OnSceneVolume_UsesItsSharedProfile_AndListingFindsTheVolume()
        {
            var path = NewProfileAsset();
            var go = new GameObject("MxTestVolume");
            go.AddComponent(VolumeType!);
            var volume = go.GetComponent(VolumeType!);
            var profile = AssetDatabase.LoadAssetAtPath(path, ProfileType!);
            var sharedProperty = VolumeType!.GetProperty("sharedProfile");
            if (sharedProperty != null)
                sharedProperty.SetValue(volume, profile);
            else
                VolumeType!.GetField("sharedProfile")!.SetValue(volume, profile);

            var result = tool.VolumeSet("MxTestVolume", "Vignette", "intensity", "0.3");

            StringAssert.Contains(path, result);
            StringAssert.Contains("MxTestVolume global=", tool.VolumeGet());
            StringAssert.Contains("intensity = 0.3", tool.VolumeGet(path));
        }

        [Test]
        public void VolumeSet_IsUndoable()
        {
            var path = NewProfileAsset();
            tool.VolumeSet(path, "Vignette", "intensity", "0.1");
            Undo.IncrementCurrentGroup();
            tool.VolumeSet(path, "Vignette", "intensity", "0.9");
            StringAssert.Contains("intensity = 0.9", tool.VolumeGet(path));

            Undo.PerformUndo();

            StringAssert.Contains("intensity = 0.1", tool.VolumeGet(path));
        }

        [Test]
        public void VolumeGet_NeedsAProfile()
        {
            NewProfileAsset();
            var go = new GameObject("MxTestEmptyVolume");
            go.AddComponent(VolumeType!);
            Assert.Throws<ArgumentException>(() => tool.VolumeGet("MxTestEmptyVolume"));
        }

        [Test]
        public void FeaturesSet_TogglesAFeatureOnACopyOfTheActivePipeline()
        {
            var original = GraphicsSettings.currentRenderPipeline;
            if (original == null)
                Assert.Ignore("The built-in pipeline is active.");
            var pipelineCopy = Folder + "/Pipeline.asset";
            Assert.IsTrue(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original), pipelineCopy));
            var copy = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(pipelineCopy);
            var so = new SerializedObject(copy);
            var list = so.FindProperty("m_RendererDataList");
            if (list == null || list.arraySize == 0)
                Assert.Ignore("The active pipeline has no renderer list (not URP).");
            var defaultIndex = so.FindProperty("m_DefaultRendererIndex").intValue;
            var rendererPath = Folder + "/Renderer.asset";
            Assert.IsTrue(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(list.GetArrayElementAtIndex(defaultIndex).objectReferenceValue), rendererPath));
            list.GetArrayElementAtIndex(defaultIndex).objectReferenceValue = AssetDatabase.LoadMainAssetAtPath(rendererPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            var previousQuality = QualitySettings.renderPipeline;
            var previousDefault = GraphicsSettings.defaultRenderPipeline;
            try
            {
                QualitySettings.renderPipeline = copy;
                var listing = tool.FeaturesList();
                StringAssert.Contains("Pipeline", listing);
                var line = listing.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("feature["));
                if (line == null)
                    Assert.Ignore("The default renderer has no features to toggle.");
                var name = line!.Trim().Split(new[] { "] " }, 2, StringSplitOptions.None)[1].Split(new[] { " type=" }, StringSplitOptions.None)[0];
                var wasActive = line.Contains("active=True");

                var result = tool.FeaturesSet(name, !wasActive);

                StringAssert.Contains($"{name} type=", result);
                StringAssert.Contains($"active={!wasActive}", result);
                Assert.Throws<ArgumentException>(() => tool.FeaturesSet("no-such-feature", true));
            }
            finally
            {
                QualitySettings.renderPipeline = previousQuality;
                GraphicsSettings.defaultRenderPipeline = previousDefault;
            }
        }
    }
}
