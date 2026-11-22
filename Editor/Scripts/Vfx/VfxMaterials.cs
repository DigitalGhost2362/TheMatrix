#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Feeder.MCP.Editor.Vfx
{
    // "texture/blend" shorthand -> one shared material per pair in Assets/VFX/_Materials/<texture>__<blend>.mat
    public static class VfxMaterials
    {
        public const string Folder = "Assets/VFX/_Materials";
        public const string DefaultTexture = "vfx_glow";
        public const string DefaultSpec = DefaultTexture + "/add";

        public static readonly string[] Blends = { "add", "alpha", "premul", "mul" };

        const string UrpShader = "Universal Render Pipeline/Particles/Unlit";
        const string BuiltinShader = "Particles/Standard Unlit";

        public static string Describe(Material? mat)
        {
            if (mat == null) return "none";
            var path = AssetDatabase.GetAssetPath(mat);
            if (string.IsNullOrEmpty(path)) return "none";
            if (path.StartsWith(Folder + "/"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var split = name.LastIndexOf("__", StringComparison.Ordinal);
                if (split > 0) return name.Substring(0, split) + "/" + name.Substring(split + 2);
            }
            return path;
        }

        public static Material? Resolve(string? spec, List<string> log)
        {
            if (string.IsNullOrEmpty(spec) || spec == "none") return null;
            if (spec!.EndsWith(".mat"))
                return AssetDatabase.LoadAssetAtPath<Material>(spec) ?? throw new ArgumentException($"material not found '{spec}'");

            var slash = spec.LastIndexOf('/');
            var blend = "add";
            var texName = spec;
            if (slash > 0 && Blends.Contains(spec.Substring(slash + 1)))
            {
                texName = spec.Substring(0, slash);
                blend = spec.Substring(slash + 1);
            }
            if (texName == DefaultTexture) VfxTextures.EnsureDefault(DefaultTexture);
            var tex = VfxTextures.Find(texName)
                      ?? throw new ArgumentException($"texture '{texName}' not found - create it with vfx-texture first (list: vfx-texture action=list)");

            var safe = Path.GetFileNameWithoutExtension(texName);
            var path = $"{Folder}/{safe}__{blend}.mat";
            var shader = PickShader();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
            {
                if (mat.shader == shader) return mat;
                mat.shader = shader;
                Setup(mat, tex, blend);
                EditorUtility.SetDirty(mat);
                log.Add($"material {path} switched to {shader.name} (render pipeline changed)");
                return mat;
            }

            Directory.CreateDirectory(Folder);
            mat = new Material(shader) { name = $"{safe}__{blend}" };
            Setup(mat, tex, blend);
            AssetDatabase.CreateAsset(mat, path);
            log.Add($"material {path} ({shader.name})");
            return mat;
        }

        public static Texture? MainTexture(Material? mat)
        {
            if (mat == null) return null;
            if (mat.HasProperty("_BaseMap")) return mat.GetTexture("_BaseMap");
            return mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
        }

        public static void AutoFlipbook(ParticleSystem ps, Material? mat, List<string> log)
        {
            var (cols, rows) = VfxTextures.Frames(MainTexture(mat));
            var sheet = ps.textureSheetAnimation;
            if (cols * rows <= 1)
            {
                return;
            }
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = cols;
            sheet.numTilesY = rows;
            log.Add($"'{ps.name}' flipbook {cols}x{rows} enabled from texture recipe");
        }

        static Shader PickShader()
        {
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                var urp = Shader.Find(UrpShader);
                if (urp != null) return urp;
            }
            return Shader.Find(BuiltinShader) ?? throw new InvalidOperationException($"neither '{UrpShader}' nor '{BuiltinShader}' shader is available");
        }

        static void Setup(Material mat, Texture tex, string blend)
        {
            var (src, dst) = blend switch
            {
                "add" => (BlendMode.SrcAlpha, BlendMode.One),
                "alpha" => (BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha),
                "premul" => (BlendMode.One, BlendMode.OneMinusSrcAlpha),
                "mul" => (BlendMode.DstColor, BlendMode.Zero),
                _ => throw new ArgumentOutOfRangeException(nameof(blend), blend, "unknown blend; valid add|alpha|premul|mul")
            };
            var isUrp = mat.shader.name == UrpShader;
            mat.shaderKeywords = Array.Empty<string>();
            if (isUrp)
            {
                mat.SetTexture("_BaseMap", tex);
                mat.SetColor("_BaseColor", Color.white);
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", blend switch { "alpha" => 0f, "premul" => 1f, "add" => 2f, _ => 3f });
                mat.SetFloat("_Cull", (float)CullMode.Off);
            }
            else
            {
                mat.SetTexture("_MainTex", tex);
                mat.SetFloat("_Mode", blend switch { "add" => 4f, "mul" => 6f, _ => 2f });
            }
            mat.SetFloat("_SrcBlend", (float)src);
            mat.SetFloat("_DstBlend", (float)dst);
            if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword(isUrp ? "_SURFACE_TYPE_TRANSPARENT" : "_ALPHABLEND_ON");
            if (blend == "premul") mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            if (blend == "mul") mat.EnableKeyword("_ALPHAMODULATE_ON");
            if (isUrp) RunUrpKeywordSetup(mat);
        }

        // the urp editor assembly is optional, so its keyword helpers are reached by reflection
        static void RunUrpKeywordSetup(Material mat)
        {
            var particleGui = Type.GetType("UnityEditor.Rendering.Universal.ShaderGUI.ParticleGUI, Unity.RenderPipelines.Universal.Editor");
            var baseGui = Type.GetType("UnityEditor.BaseShaderGUI, Unity.RenderPipelines.Universal.Editor");
            try
            {
                var setBase = baseGui?.GetMethods().FirstOrDefault(m => m.Name == "SetMaterialKeywords" && m.IsStatic);
                setBase?.Invoke(null, setBase.GetParameters().Select((p, i) => i == 0 ? (object)mat : null).ToArray());
                var setParticle = particleGui?.GetMethods().FirstOrDefault(m => m.Name == "SetMaterialKeywords" && m.IsStatic && m.GetParameters().Length == 1);
                setParticle?.Invoke(null, new object[] { mat });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Matrix VFX] urp keyword setup skipped for {mat.name}: {ex.InnerException?.Message ?? ex.Message}");
            }
        }
    }
}
