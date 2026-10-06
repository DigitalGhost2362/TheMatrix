#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Feeder.MCP.Editor.HyperTesting
{
    // active only inside a clone made by HyperClonePool, and only while the original project still has the mode enabled
    [InitializeOnLoad]
    public static class HyperRunner
    {
        const string PendingCommandKey = "Feeder.HyperRunner.PendingCommand";
        const string LastCommandKey = "Feeder.HyperRunner.LastCommand";
        const string LastResultKey = "Feeder.HyperRunner.LastResult";
        const string BlockedWritesKey = "Feeder.HyperRunner.BlockedWrites";
        const string SetupDoneKey = "Feeder.HyperRunner.SetupDone";
        const string ToolSetDoneKey = "Feeder.HyperRunner.ToolSetDone";
        const string SettledKey = "Feeder.HyperRunner.Settled";
        const string CreatedKey = "Feeder.HyperRunner.CreatedAssets";
        const string CreatedCountKey = "Feeder.HyperRunner.CreatedAssetCount";
        const string LastRefreshKey = "Feeder.HyperRunner.LastRefreshUtc";
        const double TickSeconds = 2;
        const int SettleTicks = 10;
        const int MaxCreatedPaths = 50;
        const double MemoryRefreshSeconds = 10;
        const double QuitPlayWaitSeconds = 30;

        static readonly string[] PlaytestTools =
        {
            "ping", "unity-tool-list", "tool-set-enabled-state", "editor-application-get-state", "editor-application-set-state",
            "console-get-logs", "screenshot-game-view", "screenshot-camera", "script-execute", "scene-list-opened", "scene-get-data",
            "scene-open", "gameobject-find", "gameobject-component-get", "object-get-data", "reflection-method-find",
            "reflection-method-call", "assets-find", "ui-inspect-tree", "ui-inspect-element", "profiler-get-memory-stats",
            "gameobject-modify", "gameobject-component-modify", "object-modify",
        };

        static readonly string[] BlockedTools =
        {
            "script-update-or-create", "script-delete", "assets-delete", "assets-move", "assets-modify", "assets-copy",
            "assets-create-folder", "assets-material-create", "assets-prefab-create", "assets-prefab-save", "scene-save",
            "scene-create", "package-add", "package-remove", "assets-refresh",
        };

        public static HyperCloneMarker? Marker { get; private set; }
        public static bool IsActive { get; private set; }
        static string _problem = string.Empty;
        static double _nextTick;
        static int _savedQualityLevel = -1;
        static bool _setupPending;
        static bool _toolSetPending;
        static bool _refreshing;
        static int _idleTicks;
        static double _quitRequestedAt;
        static double _memoryAt = double.MinValue;
        static EditorMemory _memory;

        static HyperRunner()
        {
            // an import worker of a clone loads every editor assembly too; the runner must stay out of it
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;
            Marker = ReadMarker();
            if (Marker == null)
                return;

            if (!HyperTestingActivation.IsEnabledFor(Marker.originDataPath))
            {
                _problem = "the original project has Hyper Testing Mode OFF (or the package version changed); runner stays idle";
                Debug.LogWarning($"[Hyper Testing] clone hc{Marker.index}: {_problem}.");
            }
            else
            {
                IsActive = true;
                Activate(Marker);
            }
            EditorApplication.update += Tick;
            EditorApplication.quitting += () => WriteStatus(false);
        }

        static HyperCloneMarker? ReadMarker()
        {
            try
            {
                var root = Path.GetFullPath(HyperTestingPaths.ProjectRoot);
                var path = Path.Combine(root, HyperTestingPaths.MarkerFileName);
                if (!File.Exists(path))
                    return null;
                var marker = JsonUtility.FromJson<HyperCloneMarker>(File.ReadAllText(path));
                if (marker == null || !SamePath(marker.cloneRoot, root) || SamePath(marker.originRoot, root))
                {
                    Debug.LogError($"[Hyper Testing] ignoring {HyperTestingPaths.MarkerFileName}: it does not describe this folder as a clone.");
                    return null;
                }
                return marker;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Hyper Testing] unreadable clone marker: {ex.Message}");
                return null;
            }
        }

        static bool SamePath(string a, string b) =>
            !string.IsNullOrEmpty(a) && string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

        static void Activate(HyperCloneMarker marker)
        {
            AssetDatabase.DisallowAutoRefresh();
            AssemblyReloadEvents.beforeAssemblyReload += AssetDatabase.AllowAutoRefresh;
            ApplyImportLimits(marker, forceWorkerCount: true);

            if (marker.isolatePlayerPrefs && !PlayerSettings.productName.EndsWith($"_hc{marker.index}"))
                PlayerSettings.productName += $"_hc{marker.index}";

            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            _setupPending = !SessionState.GetBool(SetupDoneKey, false);
            _toolSetPending = !SessionState.GetBool(ToolSetDoneKey, false);
        }

        // refresh import mode lives in the clone's ProjectSettings, which every refresh copies over from the original,
        // and the worker count is an in-memory value: both are applied again before each import
        static void ApplyImportLimits(HyperCloneMarker marker, bool forceWorkerCount)
        {
            if (EditorSettings.refreshImportMode != AssetDatabase.RefreshImportMode.InProcess)
                EditorSettings.refreshImportMode = AssetDatabase.RefreshImportMode.InProcess;

            var workers = Math.Max(0, marker.importWorkers);
            var changed = AssetDatabase.DesiredWorkerCount != workers;
            if (changed)
                AssetDatabase.DesiredWorkerCount = workers;
            // forcing a non-zero count starts the workers at once, so that is left to the first import
            if (changed || (forceWorkerCount && workers == 0))
                AssetDatabase.ForceToDesiredWorkerCount();
        }

        static void FirstSetup(HyperCloneMarker marker)
        {
            try
            {
                if (!MatrixActivation.IsEnabled)
                    MatrixActivation.Enable();

                if (marker.closeSceneViews)
                {
                    foreach (var view in SceneView.sceneViews.OfType<SceneView>().ToList())
                        view.Close();
                }
                var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
                if (gameViewType != null)
                    EditorWindow.GetWindow(gameViewType);
                Debug.Log($"[Hyper Testing] clone hc{marker.index} runner active on port {marker.port}. Auto refresh is off; asset writes are blocked.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Hyper Testing] clone setup failed: {ex}");
            }
        }

        static void ApplyToolSet()
        {
            var manager = UnityMcpPluginEditor.Instance.Tools;
            if (manager == null)
                return;
            foreach (var tool in manager.GetAllTools().Where(t => t.Name != null))
            {
                if (PlaytestTools.Contains(tool.Name!))
                    manager.SetToolEnabled(tool.Name!, true);
                else if (BlockedTools.Contains(tool.Name!))
                    manager.SetToolEnabled(tool.Name!, false);
            }
            UnityMcpPluginEditor.Instance.Save();
            _toolSetPending = false;
            SessionState.SetBool(ToolSetDoneKey, true);
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            var marker = Marker;
            if (marker == null)
                return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                if (marker.playTargetFrameRate > 0)
                {
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = marker.playTargetFrameRate;
                }
                if (marker.playLowestQuality)
                {
                    _savedQualityLevel = QualitySettings.GetQualityLevel();
                    QualitySettings.SetQualityLevel(0, false);
                }
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                if (_savedQualityLevel >= 0)
                    QualitySettings.SetQualityLevel(_savedQualityLevel, false);
                _savedQualityLevel = -1;
                EditorUtility.UnloadUnusedAssetsImmediate();
                GC.Collect();
                if (SessionState.GetString(PendingCommandKey, string.Empty).Length > 0)
                    RunPendingRefresh();
            }
        }

        static void Tick()
        {
            if (EditorApplication.isPlaying && !InternalEditorUtility.isApplicationActive)
                EditorApplication.QueuePlayerLoopUpdate();

            if (EditorApplication.timeSinceStartup < _nextTick)
                return;
            _nextTick = EditorApplication.timeSinceStartup + TickSeconds;

            if (IsActive)
            {
                // update fires in an unfocused editor, delayCall does not
                RunDeferredSetup();
                ApplyImportLimits(Marker!, forceWorkerCount: false);
                ReadCommand();
                CompletePendingIfSettled();
                MarkSettled();
                if (ExitWhenRequested())
                    return;
            }
            WriteStatus(true);
        }

        static void RunDeferredSetup()
        {
            if (_setupPending)
            {
                _setupPending = false;
                FirstSetup(Marker!);
                SessionState.SetBool(SetupDoneKey, true);
            }
            if (_toolSetPending && SessionState.GetBool(SetupDoneKey, false))
                ApplyToolSet();
        }

        // imports before the editor has been idle for a while are the startup refresh of the original's own changes
        static void MarkSettled()
        {
            if (SessionState.GetBool(SettledKey, false))
                return;
            var idle = !EditorApplication.isUpdating && !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode;
            _idleTicks = idle ? _idleTicks + 1 : 0;
            if (_idleTicks >= SettleTicks)
                SessionState.SetBool(SettledKey, true);
        }

        static bool ExitWhenRequested()
        {
            if (_quitRequestedAt <= 0)
                return false;
            if (EditorApplication.isPlaying && EditorApplication.timeSinceStartup - _quitRequestedAt < QuitPlayWaitSeconds)
                return false;
            WriteStatus(false);
            EditorApplication.Exit(0);
            return true;
        }

        static void ReadCommand()
        {
            var path = HyperTestingPaths.CommandFile(HyperTestingPaths.ProjectRoot);
            if (!File.Exists(path))
                return;
            HyperCloneCommand? command;
            try
            {
                command = JsonUtility.FromJson<HyperCloneCommand>(File.ReadAllText(path));
                File.Delete(path);
            }
            catch (Exception)
            {
                return;
            }
            if (command == null || string.IsNullOrEmpty(command.id))
                return;

            switch (command.command)
            {
                case "refresh":
                    SessionState.SetString(PendingCommandKey, command.id);
                    if (EditorApplication.isPlaying)
                        EditorApplication.isPlaying = false;
                    else
                        RunPendingRefresh();
                    break;
                case "quit":
                    if (EditorApplication.isPlaying)
                        EditorApplication.isPlaying = false;
                    Finish(command.id, "quitting");
                    _quitRequestedAt = Math.Max(1, EditorApplication.timeSinceStartup);
                    break;
                case "gc":
                    EditorUtility.UnloadUnusedAssetsImmediate();
                    GC.Collect();
                    Finish(command.id, "collected");
                    break;
                default:
                    Finish(command.id, $"unknown command '{command.command}'");
                    Debug.LogError($"[Hyper Testing] unknown runner command '{command.command}'");
                    break;
            }
        }

        static void RunPendingRefresh()
        {
            SessionState.SetString(LastRefreshKey, DateTime.UtcNow.ToString("o"));
            ApplyImportLimits(Marker!, forceWorkerCount: true);
            AssetDatabase.AllowAutoRefresh();
            _refreshing = true;
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            finally
            {
                _refreshing = false;
                AssetDatabase.DisallowAutoRefresh();
            }
            _nextTick = EditorApplication.timeSinceStartup + TickSeconds;
        }

        static void CompletePendingIfSettled()
        {
            var pending = SessionState.GetString(PendingCommandKey, string.Empty);
            if (pending.Length == 0 || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            SessionState.EraseString(PendingCommandKey);
            Finish(pending, EditorUtility.scriptCompilationFailed ? "compile-failed" : "ok");
        }

        static void Finish(string id, string result)
        {
            SessionState.SetString(LastCommandKey, id);
            SessionState.SetString(LastResultKey, result);
        }

        // a file the original made is older than the refresh that imports it, so an import outside our own refresh
        // of a file newer than both this editor and the last refresh was created by the clone
        internal static void CheckImported(IEnumerable<string> paths)
        {
            if (_refreshing || !SessionState.GetBool(SettledKey, false) || SessionState.GetString(PendingCommandKey, string.Empty).Length > 0)
                return;
            var since = DateTime.UtcNow.AddSeconds(-EditorApplication.timeSinceStartup);
            if (DateTime.TryParse(SessionState.GetString(LastRefreshKey, string.Empty), null, System.Globalization.DateTimeStyles.RoundtripKind, out var refreshed) && refreshed.ToUniversalTime() > since)
                since = refreshed.ToUniversalTime();
            foreach (var path in paths)
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("Packages/", StringComparison.Ordinal))
                    continue;
                var full = Path.Combine(HyperTestingPaths.ProjectRoot, path);
                if ((File.Exists(full) || Directory.Exists(full)) && File.GetCreationTimeUtc(full) > since)
                    RecordCreated(path);
            }
        }

        static void RecordCreated(string path)
        {
            var known = SessionState.GetString(CreatedKey, string.Empty).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (known.Contains(path))
                return;
            SessionState.SetInt(CreatedCountKey, SessionState.GetInt(CreatedCountKey, 0) + 1);
            if (known.Count < MaxCreatedPaths)
            {
                known.Add(path);
                SessionState.SetString(CreatedKey, string.Join("\n", known));
            }
            Debug.LogError($"[Hyper Testing] CRITICAL: clone hc{Marker?.index} created '{path}'. Its Assets folder is the original project's, so the file now exists in the ORIGINAL project.");
        }

        internal static void CountBlockedWrite(string what)
        {
            SessionState.SetInt(BlockedWritesKey, SessionState.GetInt(BlockedWritesKey, 0) + 1);
            Debug.LogError($"[Hyper Testing] blocked a write from clone hc{Marker?.index}: {what}. Clones are read-only; their Assets folder is the original project's.");
        }

        static void RefreshMemory()
        {
            if (EditorApplication.timeSinceStartup - _memoryAt < MemoryRefreshSeconds)
                return;
            _memoryAt = EditorApplication.timeSinceStartup;
            _memory = HyperSystemMemory.ReadEditor(HyperProcess.CurrentPid, HyperProcess.Snapshot());
        }

        static void WriteStatus(bool alive)
        {
            var marker = Marker;
            if (marker == null)
                return;
            try
            {
                RefreshMemory();
                var status = new HyperCloneStatus
                {
                    index = marker.index,
                    pid = alive ? HyperProcess.CurrentPid : 0,
                    writtenUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    runnerActive = IsActive && alive,
                    runnerProblem = _problem,
                    isPlaying = EditorApplication.isPlaying,
                    isCompiling = EditorApplication.isCompiling,
                    isUpdating = EditorApplication.isUpdating,
                    compileFailed = EditorUtility.scriptCompilationFailed,
                    workingSetMb = _memory.Editor.WorkingSetMb,
                    peakWorkingSetMb = _memory.Editor.PeakWorkingSetMb,
                    commitMb = _memory.Editor.CommitMb,
                    workers = _memory.Workers,
                    workersCommitMb = _memory.WorkersCommitMb,
                    blockedWrites = SessionState.GetInt(BlockedWritesKey, 0),
                    createdAssetCount = SessionState.GetInt(CreatedCountKey, 0),
                    createdAssets = SessionState.GetString(CreatedKey, string.Empty).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries),
                    lastCommandId = SessionState.GetString(LastCommandKey, string.Empty),
                    lastCommandResult = SessionState.GetString(LastResultKey, string.Empty),
                };
                Directory.CreateDirectory(HyperTestingPaths.ChannelFolder(HyperTestingPaths.ProjectRoot));
                HyperClonePool.WriteAtomic(HyperTestingPaths.StatusFile(HyperTestingPaths.ProjectRoot), JsonUtility.ToJson(status));
            }
            catch (Exception)
            {
            }
        }
    }

    public class HyperRunnerWriteGuard : AssetModificationProcessor
    {
        static string[] OnWillSaveAssets(string[] paths)
        {
            if (!HyperRunner.IsActive || paths.Length == 0)
                return paths;
            HyperRunner.CountBlockedWrite("save " + string.Join(", ", paths.Take(5)));
            return Array.Empty<string>();
        }

        static AssetDeleteResult OnWillDeleteAsset(string path, RemoveAssetOptions options)
        {
            if (!HyperRunner.IsActive)
                return AssetDeleteResult.DidNotDelete;
            HyperRunner.CountBlockedWrite("delete " + path);
            return AssetDeleteResult.FailedDelete;
        }

        static AssetMoveResult OnWillMoveAsset(string source, string destination)
        {
            if (!HyperRunner.IsActive)
                return AssetMoveResult.DidNotMove;
            HyperRunner.CountBlockedWrite($"move {source} -> {destination}");
            return AssetMoveResult.FailedMove;
        }
    }

    // creation cannot be cancelled from AssetModificationProcessor, so it is detected after the import
    public class HyperCloneAssetWatch : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (HyperRunner.IsActive)
                HyperRunner.CheckImported(imported.Concat(moved));
        }
    }
}
