#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;
using Feeder.Reflect.Utils;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Screenshot
    {
        public const string ScreenshotEditorWindowToolId = "screenshot-editor-window";
        internal const string CaptureLog = "[Matrix Capture]";

        const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly FieldInfo? ParentField = typeof(EditorWindow).GetField("m_Parent", InstanceFlags);

        [AiTool
        (
            ScreenshotEditorWindowToolId,
            Title = "Screenshot / Editor Window",
            ReadOnlyHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Capture any open Unity Editor window (Inspector, Console, a custom tool window, a floating window) " +
            "as the Editor draws it. Pick it by title or type name; default is the focused window. Scaled to `maxResolution` (default 1024; 0 = full size).")]
        [AiSkillBody("Captures an open Editor window from the screen and returns it as an image.\n\n" +
            "## Inputs\n\n" +
            "- `window` - window title (`titleContent.text`, e.g. `Inspector`) or type name (e.g. `InspectorWindow`), case-insensitive. " +
            "Empty = the focused window. A title that only contains the text matches when nothing matches exactly.\n" +
            "- `maxResolution` (default 1024) - longest side of the returned image; `0` returns the full size.\n" +
            "- `savePath` - optional. Also writes the full-size PNG there. Relative paths start at the project root; " +
            "a path without an extension is a folder and the file is named after the window title.\n\n" +
            "## Behavior\n\n" +
            "Focuses the window (a docked tab is brought to front), repaints it and reads its own back buffer " +
            "(`GUIView.GrabPixels`), so other apps or monitors covering it do not matter. The image is the window content " +
            "without the tab strip, `position.size * EditorGUIUtility.pixelsPerPoint` pixels. " +
            "The error lists the open windows when none matches.\n\n" +
            "People can also use `Tools > Feeder > Matrix Bridge > Capture`; it saves to `<project>/Docs/Captures/<title>.png`.")]
        [Description("Captures an open Editor window as the Editor draws it and returns it as an image.")]
        public ResponseCallTool ScreenshotEditorWindow
        (
            [Description("Window title or type name, case-insensitive. Empty = the focused window.")]
            string? window = null,
            [Description("Longest side of the returned image in pixels. Default: 1024. 0 = full size (still capped at 3840).")]
            int maxResolution = 1024,
            [Description("Optional file or folder to also save the full-size PNG to. Relative paths start at the project root.")]
            string? savePath = null
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var target = FindEditorWindow(window);
                if (target == null)
                    return ResponseCallTool.Error((string.IsNullOrWhiteSpace(window) ? "No Editor window has focus." : $"No open Editor window matches '{window}'.") +
                        " Open windows: " + string.Join(", ", OpenEditorWindows().Select(w => $"'{w.titleContent.text}' ({w.GetType().Name})")));

                var texture = CaptureEditorWindow(target, out var error);
                if (texture == null)
                    return ResponseCallTool.Error(error);

                try
                {
                    var caption = $"Editor window '{target.titleContent.text}' ({target.GetType().Name}), captured {texture.width}x{texture.height}";
                    if (!string.IsNullOrWhiteSpace(savePath))
                        caption += $", saved to {SaveCapture(texture, ResolveCapturePath(savePath!, target))}";
                    var png = EncodeScaledPng(texture, maxResolution, out var width, out var height);
                    return ResponseCallTool.Image(png, Feeder.ToolHost.Common.Consts.MimeType.ImagePng, $"{caption}, returned {width}x{height}");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            });
        }

        internal static EditorWindow[] OpenEditorWindows()
            => Resources.FindObjectsOfTypeAll<EditorWindow>()
                .Where(w => w != null && w.position.width > 0 && w.position.height > 0)
                .OrderBy(w => w.titleContent.text, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        static EditorWindow? FindEditorWindow(string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return EditorWindow.focusedWindow;

            var name = query!.Trim();
            var windows = OpenEditorWindows();
            return windows.FirstOrDefault(w => string.Equals(w.titleContent.text, name, StringComparison.OrdinalIgnoreCase))
                ?? windows.FirstOrDefault(w => string.Equals(w.GetType().Name, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(w.GetType().FullName, name, StringComparison.OrdinalIgnoreCase))
                ?? windows.FirstOrDefault(w => w.titleContent.text.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // Reads the window's own back buffer instead of the screen: screen reads land off target on
        // multi-monitor layouts with negative coordinates, and catch whatever covers the window.
        internal static Texture2D? CaptureEditorWindow(EditorWindow window, out string error)
        {
            error = string.Empty;
            var view = ParentField?.GetValue(window);
            var grab = view?.GetType().GetMethod("GrabPixels", InstanceFlags, null, new[] { typeof(RenderTexture), typeof(Rect) }, null);
            var viewRectProperty = view?.GetType().GetProperty("position", InstanceFlags);
            var viewScreenProperty = view?.GetType().GetProperty("screenPosition", InstanceFlags);
            if (view == null || grab == null || viewRectProperty == null || viewScreenProperty == null)
            {
                error = "This Unity version does not expose GUIView.GrabPixels, so Editor windows cannot be captured.";
                return null;
            }

            window.Focus();
            view.GetType().GetMethod("RepaintImmediately", InstanceFlags, null, Type.EmptyTypes, null)?.Invoke(view, null);

            var scale = EditorGUIUtility.pixelsPerPoint;
            var viewRect = (Rect)viewRectProperty.GetValue(view);
            var viewScreen = (Rect)viewScreenProperty.GetValue(view);
            var content = window.position;
            var viewWidth = Mathf.RoundToInt(viewRect.width * scale);
            var viewHeight = Mathf.RoundToInt(viewRect.height * scale);
            var width = Mathf.Min(viewWidth, Mathf.RoundToInt(content.width * scale));
            var height = Mathf.Min(viewHeight, Mathf.RoundToInt(content.height * scale));
            var left = Mathf.Clamp(Mathf.RoundToInt((content.x - viewScreen.x) * scale), 0, viewWidth - width);
            if (width <= 0 || height <= 0 || viewWidth > MaxDimension || viewHeight > MaxDimension)
            {
                error = $"Window '{window.titleContent.text}' has an unusable size {width}x{height}.";
                return null;
            }

            var previous = RenderTexture.active;
            var target = new RenderTexture(viewWidth, viewHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var full = new Texture2D(viewWidth, viewHeight, TextureFormat.RGB24, false);
            try
            {
                grab.Invoke(view, new object[] { target, new Rect(0, 0, viewWidth, viewHeight) });
                RenderTexture.active = target;
                full.ReadPixels(new Rect(0, 0, viewWidth, viewHeight), 0, 0);
            }
            catch (Exception e)
            {
                UnityEngine.Object.DestroyImmediate(full);
                error = $"Reading the window failed: {(e is TargetInvocationException t ? t.InnerException ?? e : e).Message}";
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }

            // The content sits under the tab strip; in an upright texture (origin bottom-left) that is the bottom rows.
            var pixels = full.GetPixels32();
            UnityEngine.Object.DestroyImmediate(full);
            var cropped = new Color32[width * height];
            for (var y = 0; y < height; y++)
            {
                var sourceRow = SystemInfo.graphicsUVStartsAtTop ? viewHeight - 1 - y : y;
                Array.Copy(pixels, sourceRow * viewWidth + left, cropped, y * width, width);
            }

            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.SetPixels32(cropped);
            texture.Apply();
            return texture;
        }

        static byte[] EncodeScaledPng(Texture2D source, int maxResolution, out int width, out int height)
        {
            (width, height) = ApplyMaxResolution(source.width, source.height, maxResolution);
            (width, height) = ClampToTransportLimit(width, height);
            if (width == source.width && height == source.height)
                return source.EncodeToPNG();

            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(width, height, 0);
            Texture2D? scaled = null;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                scaled = new Texture2D(width, height, TextureFormat.RGB24, false);
                scaled.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                scaled.Apply();
                return scaled.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                if (scaled != null)
                    UnityEngine.Object.DestroyImmediate(scaled);
            }
        }

        internal static string ResolveCapturePath(string path, EditorWindow window)
        {
            var full = Path.IsPathRooted(path) ? path : Path.Combine(MatrixPluginEditor.ProjectRootPath, path);
            return Path.HasExtension(full) ? Path.GetFullPath(full) : Path.GetFullPath(Path.Combine(full, CaptureSlug(window.titleContent.text) + ".png"));
        }

        internal static string SaveCapture(Texture2D texture, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            return path;
        }

        static string CaptureSlug(string text)
        {
            var builder = new StringBuilder();
            foreach (var c in text.ToLowerInvariant())
            {
                var keep = char.IsLetterOrDigit(c) && c < 128;
                if (keep || (builder.Length > 0 && builder[builder.Length - 1] != '-'))
                    builder.Append(keep ? c : '-');
            }
            var slug = builder.ToString().Trim('-');
            return slug.Length > 0 ? slug : "window";
        }
    }

    static class EditorWindowCaptureMenu
    {
        const string CaptureMenu = UI.MenuItems.Root + "Capture/";
        const string DefaultFolder = "Docs/Captures";

        [MenuItem(CaptureMenu + "Capture Focused Window", priority = 100)]
        static void CaptureFocused()
        {
            var window = EditorWindow.focusedWindow;
            if (window == null)
            {
                Debug.LogWarning($"{Tool_Screenshot.CaptureLog} No Editor window has focus.");
                return;
            }
            CaptureLater(window);
        }

        [MenuItem(CaptureMenu + "Capture Window...", priority = 101)]
        static void PickWindow() => EditorWindowCapturePicker.Open();

        internal static void CaptureLater(EditorWindow window)
        {
            window.Focus();
            window.Repaint();
            EditorApplication.delayCall += () => EditorApplication.delayCall += () => Capture(window);
        }

        static void Capture(EditorWindow window)
        {
            if (window == null)
                return;

            var texture = Tool_Screenshot.CaptureEditorWindow(window, out var error);
            if (texture == null)
            {
                Debug.LogWarning($"{Tool_Screenshot.CaptureLog} {error}");
                return;
            }

            try
            {
                var path = Tool_Screenshot.SaveCapture(texture, Tool_Screenshot.ResolveCapturePath(DefaultFolder, window));
                Debug.Log($"{Tool_Screenshot.CaptureLog} Saved '{window.titleContent.text}' ({texture.width}x{texture.height}) to {path}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tool_Screenshot.CaptureLog} Saving the capture failed: {e.Message}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }

    class EditorWindowCapturePicker : EditorWindow
    {
        Vector2 _scroll;

        internal static void Open()
        {
            var picker = CreateInstance<EditorWindowCapturePicker>();
            picker.titleContent = new GUIContent("Capture Window");
            picker.minSize = new Vector2(320, 240);
            picker.ShowUtility();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Pick a window to capture to Docs/Captures.", EditorStyles.wordWrappedLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var window in Tool_Screenshot.OpenEditorWindows())
            {
                if (window == this)
                    continue;
                if (GUILayout.Button($"{window.titleContent.text}  ({window.GetType().Name})", EditorStyles.miniButton))
                {
                    Close();
                    EditorWindowCaptureMenu.CaptureLater(window);
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
