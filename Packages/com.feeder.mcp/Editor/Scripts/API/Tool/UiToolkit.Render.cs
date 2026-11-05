#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;
using Feeder.Reflect.Utils;
using Feeder.MCP.Runtime.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_UiToolkit
    {
        public const string UiToolkitRenderToolId = "uitoolkit-render";
        const int MaxRenderSide = 4096;

        [AiTool
        (
            UiToolkitRenderToolId,
            Title = "UI Toolkit / Render",
            ReadOnlyHint = true,
            IdempotentHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Render a UIDocument (scene object path), a .uxml asset or a PanelSettings asset to a PNG at a chosen size, in Edit mode, without Game View or play mode. " +
            "Leaves scenes and assets as they were.")]
        [Description("Draws a UI Toolkit panel into an offscreen texture and returns it as an image. target: a scene object with a UIDocument, a '.uxml' asset " +
            "(shown with panelSettingsPath or default settings), or a PanelSettings '.asset' (every UIDocument in the loaded scenes that uses it). " +
            "Temporary objects and texture bindings are removed afterwards. Pass flipY=true if the image comes out upside down. " +
            "Answers '[unsupported] ...' (an error, never a blank image) when the panel cannot be painted in Edit mode.")]
        public ResponseCallTool Render
        (
            [Description("Scene path of a GameObject with a UIDocument, a '.uxml' path, or a PanelSettings '.asset' path.")]
            string target,
            [Description("Image width in pixels, 16 to 4096. Default: 800")]
            int width = 800,
            [Description("Image height in pixels, 16 to 4096. Default: 600")]
            int height = 600,
            [Description("PanelSettings asset used when target is a .uxml (optional).")]
            string? panelSettingsPath = null,
            [Description("Flip the image vertically. Default: false")]
            bool flipY = false,
            [Description("Longest side of the returned image. Default: 1024. 0 = full size.")]
            int maxResolution = 1024
        )
        {
            if (string.IsNullOrWhiteSpace(target))
                throw new ArgumentException("target is required.");
            if (width < 16 || width > MaxRenderSide || height < 16 || height > MaxRenderSide)
                throw new ArgumentException($"width and height must be between 16 and {MaxRenderSide}.");

            return MainThread.Instance.Run(() =>
            {
                var t = target.Trim();
                var docs = new List<(UIDocument Doc, PanelSettings? Original)>();
                GameObject? temp = null;
                RenderTexture? rt = null;
                var clones = new List<UnityEngine.Object>();
                try
                {
                    if (t.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase))
                    {
                        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(t)
                            ?? throw new ArgumentException($"target: no UXML asset at '{t}'.");
                        temp = new GameObject("MatrixUiRender") { hideFlags = HideFlags.DontSave };
                        temp.SetActive(false);
                        var doc = temp.AddComponent<UIDocument>();
                        doc.visualTreeAsset = tree;
                        var source = string.IsNullOrWhiteSpace(panelSettingsPath) ? null : AssetDatabase.LoadAssetAtPath<PanelSettings>(RequireAssetPath(panelSettingsPath, ".asset"));
                        if (!string.IsNullOrWhiteSpace(panelSettingsPath) && source == null)
                            throw new ArgumentException($"panelSettingsPath: no PanelSettings at '{panelSettingsPath}'.");
                        docs.Add((doc, null));
                        doc.panelSettings = source;
                    }
                    else if (t.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    {
                        var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(t)
                            ?? throw new ArgumentException($"target: no PanelSettings at '{t}'.");
                        foreach (var d in Resources.FindObjectsOfTypeAll<UIDocument>().Where(d => d.gameObject.scene.IsValid() && d.gameObject.activeInHierarchy))
                            if (d.panelSettings == ps)
                                docs.Add((d, ps));
                        if (docs.Count == 0)
                            throw new InvalidOperationException($"No active UIDocument in the loaded scenes uses '{t}'.");
                    }
                    else
                    {
                        var go = SceneLookup.Find(t) ?? throw new ArgumentException($"target: no scene object '{t}'.");
                        var doc = go.GetComponent<UIDocument>() ?? throw new ArgumentException($"target: '{t}' has no UIDocument.");
                        if (doc.panelSettings == null)
                            throw new InvalidOperationException($"The UIDocument on '{t}' has no PanelSettings.");
                        docs.Add((doc, doc.panelSettings));
                    }

                    rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
                    rt.Create();
                    foreach (var (doc, original) in docs)
                    {
                        var clone = original != null ? UnityEngine.Object.Instantiate(original) : (doc.panelSettings != null ? UnityEngine.Object.Instantiate(doc.panelSettings) : NewPanelSettings());
                        clone.targetTexture = rt;
                        clone.scaleMode = PanelScaleMode.ConstantPixelSize;
                        clone.scale = 1f;
                        clone.clearColor = true;
                        clone.colorClearValue = Clear;
                        clones.Add(clone);
                        doc.panelSettings = clone;
                    }
                    temp?.SetActive(true);
                    // re-attach each document so it builds its panel on the new settings before the pump
                    foreach (var (doc, _) in docs)
                    {
                        doc.enabled = false;
                        doc.enabled = true;
                        _ = doc.rootVisualElement;
                    }

                    // painted = the texture changed during the pump (the clear color alone proves it, even for an empty UXML)
                    var before = Snapshot(rt);
                    if (!PumpPanels())
                        return ResponseCallTool.Error("[unsupported] This Unity version has no safe way to paint UI Toolkit panels in Edit mode (UIElementsRuntimeUtility.UpdatePanels / RepaintPanels / RenderOffscreenPanels were not found).");
                    if (before.SequenceEqual(Snapshot(rt)))
                        return ResponseCallTool.Error("[unsupported] The panel did not paint into the texture in Edit mode (the render target stayed untouched). Enter play mode and use 'screenshot-game-view' instead.");

                    var png = ReadPng(rt, flipY, maxResolution, out var w, out var h, out var blank, out _);
                    var caption = $"UI Toolkit render of {t} ({w}x{h})" + (blank ? "; the image is a single flat color (the UXML may be empty)" : string.Empty);
                    return ResponseCallTool.Image(png, Feeder.ToolHost.Common.Consts.MimeType.ImagePng, caption);
                }
                finally
                {
                    foreach (var (doc, original) in docs)
                        if (doc != null && original != null)
                            doc.panelSettings = original;
                    if (temp != null)
                        UnityEngine.Object.DestroyImmediate(temp);
                    foreach (var c in clones)
                        if (c != null)
                            UnityEngine.Object.DestroyImmediate(c);
                    if (rt != null)
                    {
                        rt.Release();
                        UnityEngine.Object.DestroyImmediate(rt);
                    }
                }
            });
        }

        static readonly Color Clear = new Color(0.2f, 0.2f, 0.2f, 1f);

        static Color32[] Snapshot(RenderTexture rt)
        {
            var previous = RenderTexture.active;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                return tex.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        // Signatures checked in the Unity 6000.4 metadata: UpdatePanels(), RepaintPanels(bool), RenderOffscreenPanels(); false when one is missing
        static bool PumpPanels()
        {
            var type = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public;
            var update = type?.GetMethod("UpdatePanels", flags, null, Type.EmptyTypes, null);
            var repaint = type?.GetMethod("RepaintPanels", flags, null, new[] { typeof(bool) }, null);
            var render = type?.GetMethod("RenderOffscreenPanels", flags, null, Type.EmptyTypes, null);
            if (update == null || repaint == null || render == null)
                return false;
            for (var i = 0; i < 2; i++)
            {
                update.Invoke(null, null);
                repaint.Invoke(null, new object[] { false });
                render.Invoke(null, null);
            }
            return true;
        }

        static byte[] ReadPng(RenderTexture rt, bool flipY, int maxResolution, out int width, out int height, out bool blank, out Color32 first)
        {
            first = default;
            var (w, h) = Tool_Screenshot.ApplyMaxResolution(rt.width, rt.height, maxResolution);
            width = w;
            height = h;
            var previous = RenderTexture.active;
            RenderTexture? scaled = null;
            Texture2D? tex = null;
            try
            {
                var source = rt;
                if (w != rt.width || h != rt.height)
                {
                    scaled = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(rt, scaled);
                    source = scaled;
                }
                RenderTexture.active = source;
                tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                var pixels = tex.GetPixels32();
                first = pixels.Length > 0 ? pixels[0] : default;
                blank = pixels.Length > 0 && pixels.All(p => p.r == pixels[0].r && p.g == pixels[0].g && p.b == pixels[0].b);
                if (flipY)
                {
                    var flipped = new Color32[pixels.Length];
                    for (var y = 0; y < h; y++)
                        Array.Copy(pixels, y * w, flipped, (h - 1 - y) * w, w);
                    tex.SetPixels32(flipped);
                }
                tex.Apply();
                return tex.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previous;
                if (scaled != null)
                    RenderTexture.ReleaseTemporary(scaled);
                if (tex != null)
                    UnityEngine.Object.DestroyImmediate(tex);
            }
        }
    }
}
