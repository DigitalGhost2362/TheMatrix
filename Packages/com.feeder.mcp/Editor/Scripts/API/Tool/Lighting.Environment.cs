#nullable enable
using System;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Runtime.Utils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Lighting
    {
        public const string LightingEnvironmentGetToolId = "lighting-environment-get";
        public const string LightingEnvironmentSetToolId = "lighting-environment-set";

        static readonly MethodInfo? GetRenderSettingsMethod = typeof(RenderSettings)
            .GetMethod("GetRenderSettings", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        [AiTool
        (
            LightingEnvironmentGetToolId,
            Title = "Lighting / Environment Get",
            ReadOnlyHint = true,
            IdempotentHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Read the active scene's skybox, ambient light, fog, reflection and sun settings (RenderSettings) as key=value lines.")]
        [Description("Reads the environment of the active scene: skybox material, ambient mode/intensity/colors, fog, reflection intensity and sun source.")]
        public string EnvironmentGet()
            => MainThread.Instance.Run(() => Describe());

        [AiTool
        (
            LightingEnvironmentSetToolId,
            Title = "Lighting / Environment Set",
            Enabled = false
        )]
        [AiSkillDescription("Change the active scene's skybox, ambient light, fog, reflection intensity or sun. Only the arguments you pass change; the answer lists the new values. One undo step.")]
        [Description("Sets environment values of the active scene (RenderSettings) in one undo step and marks the scene dirty (it does not save). " +
            "Colors: '#RRGGBB', '#RRGGBBAA', a color name or 'r,g,b[,a]' with 0-1 floats. Only passed arguments change. Editor-changing.")]
        public string EnvironmentSet
        (
            [Description("Skybox material asset path ('Assets/.../Sky.mat'), or 'none' to clear it.")]
            string? skyboxMaterial = null,
            [Description("'Skybox', 'Trilight', 'Flat' or 'Custom'.")]
            string? ambientMode = null,
            [Description("Ambient intensity multiplier (Skybox mode).")]
            float? ambientIntensity = null,
            [Description("Flat ambient color.")]
            string? ambientColor = null,
            [Description("Trilight sky color.")]
            string? ambientSkyColor = null,
            [Description("Trilight equator color.")]
            string? ambientEquatorColor = null,
            [Description("Trilight ground color.")]
            string? ambientGroundColor = null,
            [Description("Reflection intensity, 0 to 1.")]
            float? reflectionIntensity = null,
            [Description("Turn fog on or off.")]
            bool? fogEnabled = null,
            [Description("'Linear', 'Exponential' or 'ExponentialSquared'.")]
            string? fogMode = null,
            [Description("Fog color.")]
            string? fogColor = null,
            [Description("Fog density (exponential modes).")]
            float? fogDensity = null,
            [Description("Fog start distance (linear mode).")]
            float? fogStart = null,
            [Description("Fog end distance (linear mode).")]
            float? fogEnd = null,
            [Description("Scene path of the Light used as the sun, or 'none' to clear it.")]
            string? sun = null
        )
        {
            // validate everything before touching the scene
            Material? sky = null;
            var clearSky = string.Equals(skyboxMaterial, "none", StringComparison.OrdinalIgnoreCase);
            AmbientMode? mode = ambientMode == null ? null : RenderingParse.RequireEnum<AmbientMode>(nameof(ambientMode), ambientMode);
            FogMode? fMode = fogMode == null ? null : RenderingParse.RequireEnum<FogMode>(nameof(fogMode), fogMode);
            Color? flat = ambientColor == null ? null : RenderingParse.RequireColor(nameof(ambientColor), ambientColor);
            Color? skyC = ambientSkyColor == null ? null : RenderingParse.RequireColor(nameof(ambientSkyColor), ambientSkyColor);
            Color? eq = ambientEquatorColor == null ? null : RenderingParse.RequireColor(nameof(ambientEquatorColor), ambientEquatorColor);
            Color? gr = ambientGroundColor == null ? null : RenderingParse.RequireColor(nameof(ambientGroundColor), ambientGroundColor);
            Color? fog = fogColor == null ? null : RenderingParse.RequireColor(nameof(fogColor), fogColor);
            if (reflectionIntensity is < 0f or > 1f)
                throw new ArgumentException("reflectionIntensity must be between 0 and 1.");

            return MainThread.Instance.Run(() =>
            {
                if (!string.IsNullOrEmpty(skyboxMaterial) && !clearSky)
                {
                    sky = AssetDatabase.LoadAssetAtPath<Material>(skyboxMaterial);
                    if (sky == null)
                        throw new ArgumentException($"skyboxMaterial: no Material at '{skyboxMaterial}'.");
                }
                Light? sunLight = null;
                var clearSun = string.Equals(sun, "none", StringComparison.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(sun) && !clearSun)
                {
                    sunLight = SceneLookup.Find(sun)?.GetComponent<Light>();
                    if (sunLight == null)
                        throw new ArgumentException($"sun: no Light on scene object '{sun}'.");
                }

                var scene = SceneManager.GetActiveScene();
                if (!scene.IsValid() || !scene.isLoaded)
                    throw new InvalidOperationException("There is no loaded active scene.");

                var settings = GetRenderSettingsMethod?.Invoke(null, null) as UnityEngine.Object;
                Undo.IncrementCurrentGroup();
                var group = Undo.GetCurrentGroup();
                if (settings != null)
                    Undo.RecordObject(settings, "Matrix lighting environment");

                if (clearSky) RenderSettings.skybox = null;
                else if (sky != null) RenderSettings.skybox = sky;
                if (mode.HasValue) RenderSettings.ambientMode = mode.Value;
                if (ambientIntensity.HasValue) RenderSettings.ambientIntensity = ambientIntensity.Value;
                if (flat.HasValue) RenderSettings.ambientLight = flat.Value;
                if (skyC.HasValue) RenderSettings.ambientSkyColor = skyC.Value;
                if (eq.HasValue) RenderSettings.ambientEquatorColor = eq.Value;
                if (gr.HasValue) RenderSettings.ambientGroundColor = gr.Value;
                if (reflectionIntensity.HasValue) RenderSettings.reflectionIntensity = reflectionIntensity.Value;
                if (fogEnabled.HasValue) RenderSettings.fog = fogEnabled.Value;
                if (fMode.HasValue) RenderSettings.fogMode = fMode.Value;
                if (fog.HasValue) RenderSettings.fogColor = fog.Value;
                if (fogDensity.HasValue) RenderSettings.fogDensity = fogDensity.Value;
                if (fogStart.HasValue) RenderSettings.fogStartDistance = fogStart.Value;
                if (fogEnd.HasValue) RenderSettings.fogEndDistance = fogEnd.Value;
                if (clearSun) RenderSettings.sun = null;
                else if (sunLight != null) RenderSettings.sun = sunLight;

                if (settings != null)
                    EditorUtility.SetDirty(settings);
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.SetCurrentGroupName("Matrix lighting environment set");
                Undo.CollapseUndoOperations(group);
                return "updated\n" + Describe();
            });
        }

        static string Describe()
        {
            var sb = new StringBuilder();
            var scene = SceneManager.GetActiveScene();
            sb.AppendLine($"scene={scene.name} path={(string.IsNullOrEmpty(scene.path) ? "(unsaved)" : scene.path)}");
            var sky = RenderSettings.skybox;
            var skyPath = sky == null ? "(none)" : AssetDatabase.GetAssetPath(sky);
            if (sky != null && skyPath.Length == 0)
                skyPath = sky.name;
            sb.AppendLine($"skybox={skyPath}");
            sb.AppendLine($"ambientMode={RenderSettings.ambientMode} ambientIntensity={RenderingParse.F(RenderSettings.ambientIntensity)}");
            sb.AppendLine($"ambientColor={RenderingParse.Color(RenderSettings.ambientLight)} sky={RenderingParse.Color(RenderSettings.ambientSkyColor)} " +
                $"equator={RenderingParse.Color(RenderSettings.ambientEquatorColor)} ground={RenderingParse.Color(RenderSettings.ambientGroundColor)}");
            sb.AppendLine($"reflectionIntensity={RenderingParse.F(RenderSettings.reflectionIntensity)} reflectionMode={RenderSettings.defaultReflectionMode}");
            sb.AppendLine($"fog={RenderSettings.fog} mode={RenderSettings.fogMode} color={RenderingParse.Color(RenderSettings.fogColor)} " +
                $"density={RenderingParse.F(RenderSettings.fogDensity)} start={RenderingParse.F(RenderSettings.fogStartDistance)} end={RenderingParse.F(RenderSettings.fogEndDistance)}");
            var sunLight = RenderSettings.sun;
            sb.Append($"sun={(sunLight == null ? "(none)" : sunLight.gameObject.GetPath())}");
            return sb.ToString();
        }
    }
}
