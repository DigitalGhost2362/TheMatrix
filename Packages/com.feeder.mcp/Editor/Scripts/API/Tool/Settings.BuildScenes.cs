#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Settings
    {
        public const string SettingsBuildScenesToolId = "settings-build-scenes";

        public sealed class BuildSceneInfo
        {
            public int Index { get; set; }
            public string Path { get; set; } = string.Empty;
            public bool Enabled { get; set; }
        }

        public sealed class BuildScenesResult
        {
            public string Action { get; set; } = string.Empty;
            public string? Message { get; set; }
            public List<BuildSceneInfo> Scenes { get; set; } = new List<BuildSceneInfo>();
        }

        [AiTool
        (
            SettingsBuildScenesToolId,
            Title = "Settings / Build Scenes",
            Enabled = false
        )]
        [AiSkillDescription("List and edit the scenes in Build Settings: add, remove, enable, disable, move or replace the whole list.")]
        [AiSkillBody("Edits the scene list of File > Build Settings (`ProjectSettings/EditorBuildSettings.asset`).\n\n" +
            "## Actions\n\n" +
            "- `list` - scenes in build order with index and enabled flag.\n" +
            "- `add` - `scenePath` (an existing `.unity`), optional `index` to insert at (default: end), optional `enabled` (default true).\n" +
            "- `remove` - by `scenePath` or `index`.\n" +
            "- `set-enabled` - by `scenePath` or `index`, `enabled` true or false.\n" +
            "- `move` - by `scenePath` or `index`, to position `toIndex`.\n" +
            "- `set` - `scenePaths` replaces the whole list (all enabled).\n\n" +
            "Every action returns the resulting list. The scene index in a build is its position among the enabled scenes.")]
        [Description("List and edit the scenes in Build Settings. Actions: list, add, remove, set-enabled, move, set. " +
            "Returns the resulting list in build order. Saved to disk.")]
        public BuildScenesResult BuildScenes
        (
            [Description("list, add, remove, set-enabled, move or set.")]
            string action,
            [Description("Scene asset path, for example 'Assets/Scenes/Main.unity' (add, remove, set-enabled, move).")]
            string? scenePath = null,
            [Description("Position in the list: insert position for add, the scene to act on for remove/set-enabled/move when no scenePath is given.")]
            int? index = null,
            [Description("Enabled flag for add (default true) and set-enabled.")]
            bool? enabled = null,
            [Description("Target position for move.")]
            int? toIndex = null,
            [Description("Full list of scene paths for set.")]
            string[]? scenePaths = null
        )
        {
            var kind = (action ?? string.Empty).Trim().ToLowerInvariant();
            return MainThread.Instance.Run(() =>
            {
                var before = EditorBuildSettings.scenes;
                var scenes = before.ToList();
                string? message = null;
                switch (kind)
                {
                    case "list":
                        break;
                    case "add":
                        var add = ExistingScene(scenePath);
                        if (scenes.Any(s => s.path == add))
                            throw new ArgumentException($"'{add}' is already in the build.");
                        var position = Math.Max(0, Math.Min(index ?? scenes.Count, scenes.Count));
                        scenes.Insert(position, new EditorBuildSettingsScene(add, enabled ?? true));
                        message = $"Added '{add}' at {position}.";
                        break;
                    case "remove":
                        var removeAt = Locate(scenes, scenePath, index);
                        message = $"Removed '{scenes[removeAt].path}'.";
                        scenes.RemoveAt(removeAt);
                        break;
                    case "set-enabled":
                        if (!enabled.HasValue)
                            throw new ArgumentException("enabled is required for set-enabled.");
                        var toggleAt = Locate(scenes, scenePath, index);
                        scenes[toggleAt] = new EditorBuildSettingsScene(scenes[toggleAt].path, enabled.Value);
                        message = $"'{scenes[toggleAt].path}' enabled={enabled.Value}.";
                        break;
                    case "move":
                        if (!toIndex.HasValue)
                            throw new ArgumentException("toIndex is required for move.");
                        var moveAt = Locate(scenes, scenePath, index);
                        var moved = scenes[moveAt];
                        scenes.RemoveAt(moveAt);
                        var target = Math.Max(0, Math.Min(toIndex.Value, scenes.Count));
                        scenes.Insert(target, moved);
                        message = $"Moved '{moved.path}' to {target}.";
                        break;
                    case "set":
                        if (scenePaths == null)
                            throw new ArgumentException("scenePaths is required for set.");
                        var paths = scenePaths.Select(ExistingScene).ToList();
                        if (paths.Distinct().Count() != paths.Count)
                            throw new ArgumentException("scenePaths contains a scene twice.");
                        scenes = paths.Select(p => new EditorBuildSettingsScene(p, true)).ToList();
                        message = $"Build list replaced with {scenes.Count} scene(s).";
                        break;
                    default:
                        throw new ArgumentException("action must be list, add, remove, set-enabled, move or set.");
                }

                if (kind != "list")
                {
                    var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/EditorBuildSettings.asset").FirstOrDefault();
                    if (asset != null)
                        Undo.RecordObject(asset, "Matrix build scenes " + kind);
                    EditorBuildSettings.scenes = scenes.ToArray();
                    if (asset != null)
                        EditorUtility.SetDirty(asset);
                    AssetDatabase.SaveAssets();
                }

                var result = new BuildScenesResult { Action = kind, Message = message };
                var current = EditorBuildSettings.scenes;
                for (var i = 0; i < current.Length; i++)
                    result.Scenes.Add(new BuildSceneInfo { Index = i, Path = current[i].path, Enabled = current[i].enabled });
                return result;
            });
        }

        static string ExistingScene(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("scenePath is required for this action.");
            var clean = path!.Trim().Replace('\\', '/');
            if (!clean.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || AssetDatabase.AssetPathToGUID(clean).Length == 0)
                throw new ArgumentException($"'{clean}' is not a scene asset in the project.");
            return clean;
        }

        static int Locate(List<EditorBuildSettingsScene> scenes, string? path, int? index)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                var clean = path!.Trim().Replace('\\', '/');
                var at = scenes.FindIndex(s => string.Equals(s.path, clean, StringComparison.OrdinalIgnoreCase));
                if (at < 0)
                    throw new ArgumentException($"'{clean}' is not in the build list.");
                return at;
            }
            if (!index.HasValue)
                throw new ArgumentException("Give scenePath or index.");
            if (index.Value < 0 || index.Value >= scenes.Count)
                throw new ArgumentException($"index {index.Value} is outside the list (0-{scenes.Count - 1}).");
            return index.Value;
        }
    }
}
