#nullable enable
using System;
using System.IO;
using System.Linq;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Feeder.MCP.Editor
{
    // every message says what to send instead: it is the only instruction an agent gets when it is wrong
    public static class MxGuard
    {
        public const string Refuse = "refuse", Save = "save", Discard = "discard";
        static readonly string[] IfDirtyValues = { Refuse, Save, Discard };

        public static string NotEmpty(string? value, string name, string? example = null)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw Fail($"'{name}' is required" + (example == null ? "." : $", e.g. '{example}'."));
            return value!.Trim();
        }

        public static int InRange(int value, int min, int max, string name)
        {
            if (value < min || value > max)
                throw Fail($"'{name}' must be between {min} and {max}; got {value}.");
            return value;
        }

        public static string OneOf(string? value, string[] allowed, string name)
        {
            var match = allowed.FirstOrDefault(a => string.Equals(a, value?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match == null)
                throw Fail($"'{name}' must be one of: {string.Join(", ", allowed)}; got '{value}'.");
            return match;
        }

        public static string IfDirty(string? value) => OneOf(value, IfDirtyValues, "ifDirty");

        // a project-relative path under Assets/, forward slashes, no '..', no absolute path
        public static string AssetPath(string? path, params string[] extensions) => AssetPathUnder(path, false, extensions);

        // same, but a read may also point into Packages/
        public static string ReadableAssetPath(string? path, params string[] extensions) => AssetPathUnder(path, true, extensions);

        static string AssetPathUnder(string? path, bool allowPackages, string[] extensions)
        {
            var p = NotEmpty(path, "path", "Assets/Folder/Name" + (extensions.Length > 0 ? extensions[0] : "")).Replace('\\', '/');
            var where = allowPackages ? "'Assets/' or 'Packages/'" : "'Assets/'";
            if (Path.IsPathRooted(p) || p.Contains(':'))
                throw Fail($"'{p}' is an absolute path. Pass a project path starting with {where}.");
            if (p.Split('/').Any(part => part == ".." || part == "."))
                throw Fail($"'{p}' contains '.' or '..'. Pass the plain path starting with {where}.");
            var underAssets = p.StartsWith("Assets/", StringComparison.Ordinal) && p.Length > "Assets/".Length;
            var underPackages = allowPackages && p.StartsWith("Packages/", StringComparison.Ordinal) && p.Length > "Packages/".Length;
            if (!underAssets && !underPackages)
                throw Fail($"'{p}' is outside the project's assets. Pass a path starting with {where}.");
            if (extensions.Length > 0 && !extensions.Any(e => p.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                throw Fail($"'{p}' must end with {string.Join(" or ", extensions)}.");
            return p.TrimEnd('/');
        }

        // a file strictly inside the project root; the extension is added before the check, so '.' cannot become '<root>.png'
        public static string ProjectFile(string? path, string? defaultExtension = null)
        {
            var p = NotEmpty(path, "path", "Temp/shot.png");
            var root = Path.GetFullPath(MatrixPluginEditor.ProjectRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(root, p)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (defaultExtension != null && !Path.HasExtension(full))
                full += defaultExtension;
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw Fail($"'{path}' resolves to '{full}', outside the project. Pass a file path inside '{root}', e.g. 'Temp/shot.png'.");
            return full;
        }

        // where a capture is written: a .png inside the project, never on top of another kind of file or an imported asset
        public static string PngFile(string? path)
        {
            var full = ProjectFile(path, ".png");
            if (!full.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                throw Fail($"'{path}' must end with .png: the capture is a PNG and is not written over another kind of file. Pass e.g. 'Temp/shot.png'.");
            var root = Path.GetFullPath(MatrixPluginEditor.ProjectRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var relative = full.Substring(root.Length + 1).Replace('\\', '/');
            var isAsset = relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);
            if (isAsset && File.Exists(full))
                throw Fail($"'{relative}' already exists and is a project asset; it is not overwritten. Pass a new path, e.g. 'Temp/shot.png'.");
            return full;
        }

        public static string NotExisting(string path)
        {
            if (File.Exists(path) || Directory.Exists(path))
                throw Fail($"'{path}' already exists and is not overwritten. Pass a new path, or delete the old asset first if that is really intended.");
            return path;
        }

        internal static Func<bool> IsPlaying = () => EditorApplication.isPlayingOrWillChangePlaymode;

        public static void NotPlaying(string action)
        {
            if (MainThread.Instance.Run(IsPlaying))
                throw Fail($"Cannot {action} in play mode. Leave play mode first (editor-application-set-state isPlaying=false), then call again.");
        }

        // ifDirty: refuse lists the unsaved scenes, save saves them, discard leaves them to the caller; no scenes = every open scene
        public static void ScenesSaved(string? ifDirty, params Scene[] scenes)
        {
            var mode = IfDirty(ifDirty);
            MainThread.Instance.Run(() =>
            {
                var pool = scenes.Length > 0 ? scenes : Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
                var dirty = pool.Where(scene => scene.IsValid() && scene.isDirty).ToArray();
                if (dirty.Length == 0 || mode == Discard)
                    return;

                var names = string.Join(", ", dirty.Select(scene => string.IsNullOrEmpty(scene.path) ? $"{scene.name} (untitled)" : scene.path));
                if (mode == Refuse)
                    throw Fail($"[unsaved_scenes] These scenes have unsaved changes: {names}. " +
                        "Call again with ifDirty='save' to save them or ifDirty='discard' to drop the changes.");
                if (dirty.Any(scene => string.IsNullOrEmpty(scene.path)))
                    throw Fail("An untitled scene has unsaved changes. Save it with scene-save and a path first, or call again with ifDirty='discard'.");
                if (!EditorSceneManager.SaveScenes(dirty))
                    throw new InvalidOperationException($"Unity failed to save: {names}.");
            });
        }

        public static ToolCallerException Fail(string message) => new ToolCallerException(message);
    }
}
