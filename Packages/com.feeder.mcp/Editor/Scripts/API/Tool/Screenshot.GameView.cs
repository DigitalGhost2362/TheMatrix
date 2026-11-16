#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using Feeder.MCP.Data;
using Feeder.MCP.Runtime.Extensions;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;
using Feeder.Reflect.Utils;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Screenshot
    {
        public const string ScreenshotGameViewToolId = "screenshot-game-view";
        [AiTool
        (
            ScreenshotGameViewToolId,
            Title = "Screenshot / Game View",
            ReadOnlyHint = true,
            IdempotentHint = true,
            Enabled = true
        )]
        [AiSkillDescription("Capture a screenshot of the Unity Editor's Game View, or of any Camera when `cameraRef` is given, " +
            "and return it as a PNG for direct inspection. Optionally also saves the PNG inside the project. " +
            "Scaled to `maxResolution` (default 1024; 0 = full size). For the Scene View as the user sees it use 'screenshot-editor-window' with window=SceneView.")]
        [AiSkillBody("Captures the Game View, or one camera, and returns it as an image.\n\n" +
            "## Inputs\n\n" +
            "- `cameraRef` (optional) - GameObject hosting a `Camera`. When set, that camera is rendered into a temporary texture at its pixel size (1920x1080 when unknown) instead of reading the Game View. " +
            "Use it for cameras that do not draw to the Game View.\n" +
            "- `maxResolution` (default 1024) - longest side of the returned image; `0` returns the full size (capped at 3840).\n" +
            "- `savePath` (optional) - also writes the returned PNG there and names the path in the reply text. Relative paths start at the project root; paths outside the project root are rejected; missing folders are created; `.png` is appended when there is no extension, any other extension is rejected, and an existing file under `Assets/` or `Packages/` is never overwritten.\n\n" +
            "## Behavior\n\n" +
            "Without `cameraRef`: locates `UnityEditor.GameView`, repaints it, reads its `m_RenderTexture` and flips the pixels on graphics APIs whose UV origin is top-left. Requires an open Game View. " +
            "With `cameraRef`: swaps a temporary `RenderTexture` onto the camera, calls `Camera.Render`, reads back and restores the camera's `targetTexture`.")]
        [Description("Captures the Game View (or the camera given by cameraRef) and returns it as an image; savePath also saves it inside the project.")]
        public ResponseCallTool ScreenshotGameView
        (
            [Description("Optional camera GameObject to render instead of the Game View.")]
            GameObjectRef? cameraRef = null,
            [Description("Longest side of the returned image in pixels; larger captures are scaled down to save tokens. Default: 1024. 0 = full size (still capped at 3840).")]
            int maxResolution = 1024,
            [Description("Optional file to also save the PNG to, inside the project root. Relative paths start at the project root.")]
            string? savePath = null
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var path = string.IsNullOrWhiteSpace(savePath) ? null : MxGuard.PngFile(savePath);

                byte[]? pngBytes;
                string label;
                int width, height;
                string error;
                if (cameraRef == null)
                {
                    label = "Game View";
                    pngBytes = CaptureGameView(maxResolution, out width, out height, out error);
                }
                else
                {
                    var camera = cameraRef.FindGameObject()?.GetComponent<Camera>();
                    if (camera == null)
                        return ResponseCallTool.Error("The referenced GameObject was not found or has no Camera.");
                    label = $"camera '{camera.name}'";
                    pngBytes = CaptureCamera(camera, maxResolution, out width, out height, out error);
                }
                if (pngBytes == null)
                    return ResponseCallTool.Error(error);

                var caption = $"Screenshot from {label} ({width}x{height})";
                if (path != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, pngBytes);
                    caption += $", saved to {path}";
                }

                return ResponseCallTool.Image(pngBytes, Feeder.ToolHost.Common.Consts.MimeType.ImagePng, caption);
            });
        }

        internal static byte[]? CaptureCamera(Camera camera, int maxResolution, out int width, out int height, out string error)
        {
            error = string.Empty;
            (width, height) = camera.pixelWidth > 0 && camera.pixelHeight > 0 ? (camera.pixelWidth, camera.pixelHeight) : (1920, 1080);
            (width, height) = ClampToTransportLimit(width, height);
            (width, height) = ApplyMaxResolution(width, height, maxResolution);

            var rt = new RenderTexture(width, height, 24);
            var prevTarget = camera.targetTexture;
            var prevActive = RenderTexture.active;
            Texture2D? tex = null;
            try
            {
                camera.targetTexture = rt;
                camera.Render();

                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                return tex.EncodeToPNG();
            }
            finally
            {
                camera.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                if (tex != null)
                    UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        internal static byte[]? CaptureGameView(int maxResolution, out int width, out int height, out string error)
        {
            error = string.Empty;
            width = height = 0;

            var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
            if (gameViewType == null)
            {
                error = "GameView type not found in UnityEditor assembly.";
                return null;
            }

            var gameView = EditorWindow.GetWindow(gameViewType, false, null, false);
            if (gameView == null)
            {
                error = "No Game View window is open.";
                return null;
            }

            gameView.Repaint();

            var rtField = gameViewType.GetField("m_RenderTexture", BindingFlags.NonPublic | BindingFlags.Instance);
            var sourceRt = rtField?.GetValue(gameView) as RenderTexture;
            if (sourceRt == null || !sourceRt.IsCreated())
            {
                error = "Game View render texture is not available. Ensure the Game View window is open and visible.";
                return null;
            }

            // The Game View RT is read directly, so DirectX/Metal need the Y-flip Unity otherwise does at display-blit time.
            // A large live resolution is downscaled on the GPU (RT to RT blit keeps pixel orientation) to stay under the MCP message limit.
            var limit = maxResolution > 0 ? Mathf.Min(maxResolution, MaxScreenshotDimension) : MaxScreenshotDimension;
            var scale = Mathf.Min(1f, (float)limit / Mathf.Max(sourceRt.width, sourceRt.height));
            width = Mathf.Max(1, Mathf.RoundToInt(sourceRt.width * scale));
            height = Mathf.Max(1, Mathf.RoundToInt(sourceRt.height * scale));

            var prevActive = RenderTexture.active;
            RenderTexture? scaledRt = null;
            Texture2D? tex = null;
            try
            {
                var readSource = sourceRt;
                if (scale < 1f)
                {
                    scaledRt = RenderTexture.GetTemporary(width, height, 0, sourceRt.format);
                    Graphics.Blit(sourceRt, scaledRt);
                    readSource = scaledRt;
                }

                RenderTexture.active = readSource;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);

                if (SystemInfo.graphicsUVStartsAtTop)
                {
                    var pixels = tex.GetPixels32();
                    var flipped = new Color32[pixels.Length];
                    for (var y = 0; y < height; y++)
                        Array.Copy(pixels, y * width, flipped, (height - 1 - y) * width, width);
                    tex.SetPixels32(flipped);
                }

                tex.Apply();
                return tex.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (scaledRt != null)
                    RenderTexture.ReleaseTemporary(scaledRt);
                if (tex != null)
                    UnityEngine.Object.DestroyImmediate(tex);
            }
        }
    }
}
