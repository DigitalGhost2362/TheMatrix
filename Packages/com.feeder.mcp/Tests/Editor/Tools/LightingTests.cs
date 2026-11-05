#nullable enable

using System;
using Feeder.MCP.Editor.API;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Feeder.MCP.Editor.Tests.Tools
{
    // runs on a temporary additive scene: the project scenes and their lighting are never touched
    public sealed class LightingTests
    {
        readonly Tool_Lighting tool = new Tool_Lighting();
        Scene previousActive;
        Scene temp;
        Material? skybox;
        AmbientMode ambientMode;
        float ambientIntensity, reflectionIntensity, fogDensity, fogStart, fogEnd;
        Color ambientLight, ambientSky, ambientEquator, ambientGround, fogColor;
        bool fog;
        FogMode fogMode;
        Light? sun;

        [SetUp]
        public void SetUp()
        {
            previousActive = SceneManager.GetActiveScene();
            skybox = RenderSettings.skybox; ambientMode = RenderSettings.ambientMode; ambientIntensity = RenderSettings.ambientIntensity;
            reflectionIntensity = RenderSettings.reflectionIntensity; ambientLight = RenderSettings.ambientLight;
            ambientSky = RenderSettings.ambientSkyColor; ambientEquator = RenderSettings.ambientEquatorColor; ambientGround = RenderSettings.ambientGroundColor;
            fog = RenderSettings.fog; fogMode = RenderSettings.fogMode; fogColor = RenderSettings.fogColor;
            fogDensity = RenderSettings.fogDensity; fogStart = RenderSettings.fogStartDistance; fogEnd = RenderSettings.fogEndDistance; sun = RenderSettings.sun;
            temp = default;
            if (!string.IsNullOrEmpty(previousActive.path))
            {
                temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(temp);
            }
        }

        [TearDown]
        public void TearDown()
        {
            RenderSettings.skybox = skybox; RenderSettings.ambientMode = ambientMode; RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.reflectionIntensity = reflectionIntensity; RenderSettings.ambientLight = ambientLight;
            RenderSettings.ambientSkyColor = ambientSky; RenderSettings.ambientEquatorColor = ambientEquator; RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.fog = fog; RenderSettings.fogMode = fogMode; RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity; RenderSettings.fogStartDistance = fogStart; RenderSettings.fogEndDistance = fogEnd; RenderSettings.sun = sun;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name.StartsWith("MxTest"))
                    UnityEngine.Object.DestroyImmediate(root);
            if (previousActive.IsValid() && previousActive.isLoaded)
                SceneManager.SetActiveScene(previousActive);
            if (temp.IsValid())
                EditorSceneManager.CloseScene(temp, true);
        }

        [Test]
        public void EnvironmentSet_ChangesOnlyPassedValues_AndGetReportsThem()
        {
            var fogBefore = RenderSettings.fog;
            var result = tool.EnvironmentSet(ambientMode: "Flat", ambientColor: "#336699", fogEnabled: true, fogMode: "Linear", fogStart: 5f, fogEnd: 90f, fogColor: "1,0,0");

            Assert.AreEqual(AmbientMode.Flat, RenderSettings.ambientMode);
            Assert.AreEqual(new Color32(0x33, 0x66, 0x99, 255), (Color32)RenderSettings.ambientLight);
            Assert.IsTrue(RenderSettings.fog);
            Assert.AreEqual(FogMode.Linear, RenderSettings.fogMode);
            Assert.AreEqual(5f, RenderSettings.fogStartDistance, 1e-4f);
            Assert.AreEqual(90f, RenderSettings.fogEndDistance, 1e-4f);
            StringAssert.Contains("ambientMode=Flat", result);
            StringAssert.Contains("fog=True mode=Linear color=#FF0000FF", tool.EnvironmentGet());
            Assert.AreNotEqual(fogBefore, RenderSettings.fog);
        }

        [Test]
        public void EnvironmentSet_BadColor_ThrowsAndChangesNothing()
        {
            var before = RenderSettings.ambientIntensity;
            Assert.Throws<ArgumentException>(() => tool.EnvironmentSet(ambientIntensity: 3f, fogColor: "not-a-color"));
            Assert.AreEqual(before, RenderSettings.ambientIntensity);
        }

        [Test]
        public void EnvironmentSet_MissingSkyboxMaterial_Throws()
            => Assert.Throws<ArgumentException>(() => tool.EnvironmentSet(skyboxMaterial: "Assets/_MxTestNope/Sky.mat"));

        [Test]
        public void EnvironmentSet_ClearsSkybox_WithNone()
        {
            RenderSettings.skybox = new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Sprites/Default"));
            var material = RenderSettings.skybox;
            try
            {
                tool.EnvironmentSet(skyboxMaterial: "none");
                Assert.IsNull(RenderSettings.skybox);
                StringAssert.Contains("skybox=(none)", tool.EnvironmentGet());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void EnvironmentSet_IsOneUndoStep()
        {
            RenderSettings.fog = false;
            Undo.IncrementCurrentGroup();
            tool.EnvironmentSet(fogEnabled: true);
            Assert.IsTrue(RenderSettings.fog);
            Undo.PerformUndo();
            Assert.IsFalse(RenderSettings.fog);
        }

        [Test]
        public void Bake_Status_WhenIdle_ReportsIdle_AndRejectsUnknownAction()
        {
            Assert.IsFalse(Lightmapping.isRunning, "a bake is running in the Editor; run this test later");
            StringAssert.Contains("state=idle", tool.Bake());
            Assert.Throws<ArgumentException>(() => tool.Bake("explode"));
        }

        [Test]
        public void Bake_Start_RefusesAnUnsavedScene()
        {
            Assert.IsFalse(Lightmapping.isRunning, "a bake is running in the Editor; run this test later");
            Assert.Throws<InvalidOperationException>(() => tool.Bake("start"));
        }
    }
}
