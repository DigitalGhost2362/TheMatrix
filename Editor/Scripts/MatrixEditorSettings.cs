#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor
{
    // What Setup changes outside Matrix, and the record that lets Revert put it back.
    // Preferences are machine-wide (every Unity project on this machine), Run In Background is per project.
    public static class MatrixEditorSettings
    {
        const string MachineSnapshotKey = "Feeder_Matrix_SetupPrefsSnapshot";
        static string ProjectSnapshotKey => $"Feeder_Matrix_SetupProjectSnapshot_{Application.dataPath}";

        internal const string AutoRefreshKey = "kAutoRefreshMode";
        internal const string ScriptChangesKey = "ScriptCompilationDuringPlay";

        internal static readonly (string key, int value, string label)[] Prefs =
        {
            (AutoRefreshKey, 0, "Preferences > Asset Pipeline > Auto Refresh: Disabled (agents refresh through assets-refresh, which waits for the editor lock)"),
            (ScriptChangesKey, 1, "Preferences > General > Script Changes While Playing: Recompile After Finished Playing"),
        };

        internal const string RunInBackgroundLabel = "Player Settings > Run In Background: On (this project; ProjectSettings.asset changes)";

        [Serializable]
        internal class PrefValue
        {
            public string key = string.Empty;
            public bool had;
            public int value;
        }

        [Serializable]
        internal class MachineSnapshot
        {
            public List<PrefValue> prefs = new();
        }

        [Serializable]
        internal class ProjectSnapshot
        {
            public bool runInBackground;
            public string gitignoreAppended = string.Empty;
            public bool toolsCaptured;
            public List<string> enabledTools = new();
        }

        public static bool HasSnapshot => EditorPrefs.HasKey(MachineSnapshotKey) || EditorPrefs.HasKey(ProjectSnapshotKey);

        // first Setup only: a later run must not overwrite what the machine looked like before Matrix
        internal static void CaptureIfMissing()
        {
            if (!EditorPrefs.HasKey(MachineSnapshotKey))
            {
                var keys = Prefs.Select(pref => pref.key);
                var machine = new MachineSnapshot
                {
                    prefs = keys.Select(key => new PrefValue { key = key, had = EditorPrefs.HasKey(key), value = EditorPrefs.GetInt(key) }).ToList(),
                };
                EditorPrefs.SetString(MachineSnapshotKey, JsonUtility.ToJson(machine));
            }

            if (!EditorPrefs.HasKey(ProjectSnapshotKey))
            {
                var project = new ProjectSnapshot
                {
                    runInBackground = PlayerSettings.runInBackground,
                };
                var tools = MatrixPluginEditor.Instance.Tools;
                if (tools != null)
                {
                    project.toolsCaptured = true;
                    project.enabledTools = tools.GetAllTools()
                        .Where(tool => tool.Name != null && tools.IsToolEnabled(tool.Name))
                        .Select(tool => tool.Name!)
                        .ToList();
                }
                SaveProject(project);
            }
        }

        internal static void RecordGitignoreAppend(string appended)
        {
            var project = LoadProject();
            if (project == null || appended.Length == 0)
                return;
            project.gitignoreAppended += appended;
            SaveProject(project);
        }

        internal static string Apply()
        {
            foreach (var (key, value, _) in Prefs)
                EditorPrefs.SetInt(key, value);
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
            return "Editor settings:\n- " + string.Join("\n- ", Prefs.Select(pref => pref.label).Append(RunInBackgroundLabel));
        }

        internal static string Revert()
        {
            var lines = new List<string>();
            var machine = Load<MachineSnapshot>(MachineSnapshotKey);
            if (machine != null)
            {
                foreach (var pref in machine.prefs)
                {
                    if (pref.had)
                        EditorPrefs.SetInt(pref.key, pref.value);
                    else
                        EditorPrefs.DeleteKey(pref.key);
                }
                EditorPrefs.DeleteKey(MachineSnapshotKey);
                lines.Add("Preferences: Auto Refresh and Script Changes While Playing restored");
            }
            else
                lines.Add("Preferences: no record from Setup, left as they are");

            var project = LoadProject();
            if (project == null)
            {
                lines.Add("Project: no record from Setup, Run In Background, .gitignore and tools left as they are");
                return string.Join("\n", lines);
            }

            if (PlayerSettings.runInBackground != project.runInBackground)
            {
                PlayerSettings.runInBackground = project.runInBackground;
                AssetDatabase.SaveAssets();
            }
            lines.Add($"Run In Background: {(project.runInBackground ? "On" : "Off")}");
            lines.Add(RemoveGitignoreAppend(project.gitignoreAppended));
            if (project.toolsCaptured)
                lines.Add(RestoreTools(project.enabledTools));
            EditorPrefs.DeleteKey(ProjectSnapshotKey);
            return string.Join("\n", lines);
        }

        internal static string RemoveFromText(string text, string appended)
        {
            if (appended.Length == 0)
                return text;
            var index = text.LastIndexOf(appended, StringComparison.Ordinal);
            return index < 0 ? text : text.Remove(index, appended.Length);
        }

        static string RemoveGitignoreAppend(string appended)
        {
            if (appended.Length == 0)
                return "Git: .gitignore was not changed by Setup";
            var path = Path.Combine(MatrixPluginEditor.ProjectRootPath, ".gitignore");
            if (!File.Exists(path))
                return "Git: .gitignore is gone, nothing to remove";
            var text = File.ReadAllText(path);
            var restored = RemoveFromText(text, appended);
            if (restored == text)
                return "Git: the lines Setup added to .gitignore were edited since, left as they are";
            if (restored.Length == 0)
                File.Delete(path);
            else
                File.WriteAllText(path, restored);
            return "Git: removed the lines Setup added to .gitignore";
        }

        static string RestoreTools(List<string> enabled)
        {
            var manager = MatrixPluginEditor.Instance.Tools;
            if (manager == null)
                return "Tools: the MCP plugin is not running, tool states left as they are";
            var keep = new HashSet<string>(enabled, StringComparer.OrdinalIgnoreCase);
            var changed = 0;
            foreach (var tool in manager.GetAllTools().Where(tool => tool.Name != null))
            {
                var enable = keep.Contains(tool.Name!);
                if (manager.IsToolEnabled(tool.Name!) == enable)
                    continue;
                manager.SetToolEnabled(tool.Name!, enable);
                changed++;
            }
            if (changed > 0)
                MatrixPluginEditor.Instance.Save();
            return $"Tools: {changed} tool state(s) restored";
        }

        static ProjectSnapshot? LoadProject() => Load<ProjectSnapshot>(ProjectSnapshotKey);

        static void SaveProject(ProjectSnapshot project) => EditorPrefs.SetString(ProjectSnapshotKey, JsonUtility.ToJson(project));

        static T? Load<T>(string key) where T : class
        {
            var json = EditorPrefs.GetString(key, string.Empty);
            if (json.Length == 0)
                return null;
            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
