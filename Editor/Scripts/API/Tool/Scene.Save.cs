#nullable enable
using System;
using System.ComponentModel;
using System.Linq;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.Utils;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Scene
    {
        public const string SceneSaveToolId = "scene-save";
        [AiTool
        (
            SceneSaveToolId,
            Title = "Scene / Save",
            IdempotentHint = true,
            Enabled = true
        )]
        [AiSkillDescription("Save the active scene, one open scene, or every open scene with changes. " +
            "A `path` on a saved scene writes a copy and leaves the open scene on its own file.")]
        [AiSkillBody("## Inputs\n\n" +
            "- `scene` (optional) - name or path of an open scene. Empty = the active scene.\n" +
            "- `path` (optional) - `.unity` destination. For an untitled scene it becomes the scene's file; " +
            "for a scene that already has a file it writes a copy there.\n" +
            "- `all` - save every open scene that has unsaved changes (ignores `scene` and `path`).\n\n" +
            "Refused in play mode: changes made in play mode are not scene data.")]
        [Description("Save the active scene, a named open scene, or all changed scenes (all=true). A path on a saved scene writes a copy.")]
        public string Save
        (
            [Description("Name or path of an open scene. Empty = the active scene.")]
            string? scene = null,
            [Description("Destination '.unity' path. Untitled scene: saved there. Saved scene: a copy is written there.")]
            string? path = null,
            [Description("Save every open scene that has unsaved changes.")]
            bool all = false
        )
        {
            return MainThread.Instance.Run(() =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Leave play mode before saving: play-mode changes are not scene data.");

                if (all)
                {
                    var dirty = OpenedScenes.Where(s => s.isDirty).ToArray();
                    if (dirty.Any(s => string.IsNullOrEmpty(s.path)))
                        throw new InvalidOperationException("An untitled scene has changes; save it alone with a path first.");
                    if (!EditorSceneManager.SaveScenes(dirty))
                        throw new Exception($"Failed to save the changed scenes.\n{OpenedScenesText}");
                    return dirty.Length == 0 ? "No open scene had unsaved changes." : "Saved: " + string.Join(", ", dirty.Select(s => s.path));
                }

                var target = EditorSceneManager.GetActiveScene();
                if (!string.IsNullOrEmpty(scene))
                {
                    target = OpenedScenes.FirstOrDefault(s => s.path == scene);
                    if (!target.IsValid())
                        target = OpenedScenes.FirstOrDefault(s => s.name == scene);
                }
                if (!target.IsValid())
                    throw new ArgumentException(Error.NotFoundSceneWithName(scene), nameof(scene));

                var hasFile = !string.IsNullOrEmpty(target.path);
                var destination = string.IsNullOrEmpty(path) ? target.path : path!;
                if (string.IsNullOrEmpty(destination))
                    throw new ArgumentException($"Scene '{target.name}' is untitled; pass a path.", nameof(path));
                if (!destination.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException(Error.FilePathMustEndsWithUnity(), nameof(path));

                var copy = hasFile && destination != target.path;
                if (!EditorSceneManager.SaveScene(target, destination, copy))
                    throw new Exception($"Failed to save scene at '{destination}'.\n{OpenedScenesText}");

                EditorUtils.RepaintAllEditorWindows();
                return copy ? $"Saved a copy of '{target.path}' to '{destination}'." : $"Saved '{destination}'.";
            });
        }
    }
}
