#nullable enable
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.HyperTesting
{
    public class HyperTestingWindow : EditorWindow
    {
        const string RiskText =
            "Hyper Testing Mode is EXPERIMENTAL and may break.\n\n" +
            "It creates clone projects next to this one (Assets and embedded packages are directory junctions to THIS project, " +
            "Library is copied per clone), launches one extra Unity Editor per clone and lets AI subagents play test in parallel.\n\n" +
            "- Each clone costs about 5 GB of memory (commit) and a Library copy on disk. They share one GPU with this editor: on a 4 GB card only one clone starts.\n" +
            "- Clones are read-only by design, but a bug could still write through a junction into this project. Commit first.\n" +
            "- The mode is per machine and per project, never shared through git, and turns itself off on every package update.\n" +
            "- Agents cannot enable it. Only this button can.";

        bool _understood;
        string _typed = string.Empty;
        string _error = string.Empty;
        string _status = string.Empty;
        int _count = 2;
        Vector2 _scroll;

        public static void ShowWindow()
        {
            var window = GetWindow<HyperTestingWindow>();
            window.titleContent = new GUIContent("Hyper Testing");
            window.minSize = new Vector2(460, 420);
            window.Show();
            window.RefreshStatus();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField(HyperTestingActivation.ModeName, EditorStyles.boldLabel);

            if (File.Exists(Path.Combine(HyperTestingPaths.ProjectRoot, HyperTestingPaths.MarkerFileName)))
            {
                DrawClone();
                return;
            }
            if (!HyperTestingActivation.IsSupportedPlatform)
            {
                EditorGUILayout.HelpBox("Hyper Testing Mode needs the Windows Editor.", MessageType.Info);
                return;
            }
            if (HyperTestingActivation.IsEnabled)
                DrawEnabled();
            else
                DrawConsent();
        }

        void DrawClone()
        {
            var marker = HyperRunner.Marker;
            var text = marker == null
                ? "This folder has a clone marker that does not match it. The runner is idle."
                : $"This editor is clone hc{marker.index} (port {marker.port}) of {marker.originRoot}.\nRunner: {(HyperRunner.IsActive ? "active - auto refresh off, asset writes blocked" : "idle - the original has the mode off")}.\nControl the pool from the original project.";
            EditorGUILayout.HelpBox(text, MessageType.Info);
        }

        void DrawConsent()
        {
            EditorGUILayout.HelpBox(RiskText, MessageType.Warning);
            if (!MatrixActivation.IsEnabled)
            {
                EditorGUILayout.HelpBox("Turn Matrix on first: Tools > Feeder > Setup...", MessageType.Error);
                return;
            }

            _understood = EditorGUILayout.ToggleLeft("I understand this mode is experimental and I am turning it on myself.", _understood);
            EditorGUILayout.LabelField($"Type the project folder name to confirm: {HyperTestingActivation.ExpectedConfirmation}");
            _typed = EditorGUILayout.TextField(_typed);

            using (new EditorGUI.DisabledScope(!_understood))
            {
                if (GUILayout.Button("Enable Hyper Testing Mode", GUILayout.Height(28)))
                {
                    if (HyperTestingActivation.TryEnableFromHumanClick(_typed, out _error))
                    {
                        _typed = string.Empty;
                        _understood = false;
                        RefreshStatus();
                    }
                }
            }
            if (_error.Length > 0)
                EditorGUILayout.HelpBox(_error, MessageType.Error);
        }

        void DrawEnabled()
        {
            EditorGUILayout.HelpBox("ON for this project on this machine. Agents drive it through the hyper-testing tool and the matrix-hyper-testing skill. Restart Claude Code once after the first enable so it sees .claude/agents.", MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                _count = EditorGUILayout.IntSlider("Clones", _count, 1, Mathf.Max(1, HyperTestingConfig.Load().maxClones));
                if (GUILayout.Button("Up", GUILayout.Width(70)))
                    StartJob($"up {_count}", () => HyperClonePool.UpAsync(_count));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh clones"))
                    StartJob("refresh", HyperClonePool.RefreshAsync);
                if (GUILayout.Button("Stop clones"))
                    StartJob("down", () => HyperClonePool.DownAsync(false));
                if (GUILayout.Button("Delete clones") && EditorUtility.DisplayDialog("Delete clones", "Stop every clone editor and delete the clone folders? Junctions are unlinked first; this project is not touched.", "Delete", "Cancel"))
                    StartJob("purge", () => HyperClonePool.DownAsync(true));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Status"))
                    RefreshStatus();
                if (GUILayout.Button("RAM sample"))
                    _status = HyperRamMonitor.Sample();
                if (GUILayout.Button("Journal"))
                    EditorUtility.RevealInFinder(HyperTestingPaths.Journal);
                if (GUILayout.Button("Config"))
                    EditorUtility.OpenWithDefaultApp(HyperTestingPaths.ConfigFile);
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_status, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Disable Hyper Testing Mode"))
            {
                HyperTestingActivation.Disable();
                RefreshStatus();
            }
        }

        void StartJob(string name, System.Func<Task<string>> work)
        {
            _status = HyperClonePool.StartJob(name, work);
        }

        async void RefreshStatus()
        {
            _status = await HyperClonePool.StatusAsync();
            Repaint();
        }
    }
}
