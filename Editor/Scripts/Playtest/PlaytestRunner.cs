#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Feeder.MCP.Editor.Activity;
using Feeder.MCP.Editor.API;
using Feeder.ToolHost.Common.Model;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Feeder.MCP.Editor.Playtest
{
    // runs one flow of the project's Playtest.md in play mode; the job sits in SessionState so it survives the domain
    // reload of entering play mode, and the steps tick in Application.onBeforeRender, inside the player loop, where
    // Screen and the EventSystem describe the Game view. After the flow, play mode ends first, then the saves are
    // compared and restored and the contact sheet is built, so writes the game makes on quit are caught too.
    [InitializeOnLoad]
    internal static class PlaytestRunner
    {
        const string JobKey = "Feeder_Matrix_Playtest_Job";
        const string LastKey = "Feeder_Matrix_Playtest_Last";
        const string LogTag = "[Matrix Playtest]";
        const double EnterPlayGiveUpSeconds = 120;
        const double ExitPlayGiveUpSeconds = 60;

        static Job? _job;
        static PlaytestFlow? _run;

        static PlaytestRunner()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;
            _job = Job.Parse(SessionState.GetString(JobKey, string.Empty));
            if (_job != null)
                EditorApplication.update += Pump;
        }

        #region Commands

        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath)!;

        public static PlaytestFile LoadFile(string? file)
        {
            var path = string.IsNullOrWhiteSpace(file) ? PlaytestFile.Find(ProjectRoot) : Path.GetFullPath(Path.Combine(ProjectRoot, file!));
            if (path == null || !File.Exists(path))
                throw new FileNotFoundException($"No {PlaytestFile.FileName} found{(file == null ? " in Docs/, docs/ or ProjectDocs/" : $" at '{file}'")}. Write one first.");
            return PlaytestFile.Load(path);
        }

        // true when the result comes later through requestId
        public static bool Start(string? file, string flow, float speed, bool? cursor, bool stay, bool device, string? requestId, out string message)
        {
            if (_job != null)
                throw new InvalidOperationException($"A playtest is already running (flow '{_job.Flow}'). Call action=stop first.");
            var playtest = LoadFile(file);
            if (playtest.Errors.Count > 0)
                throw new FormatException(playtest.Describe());
            if (device)
                return PlaytestDeviceRun.Start(playtest, flow, speed, cursor, requestId, out message);
            playtest.Expand(flow);

            var job = new Job
            {
                RequestId = requestId ?? string.Empty,
                File = playtest.Path,
                Flow = flow,
                Speed = speed > 0 ? speed : playtest.GetFloat("speed", 1f),
                Cursor = cursor ?? playtest.GetBool("cursor", true),
                Stay = stay,
                StartTicks = DateTime.UtcNow.Ticks,
                Protect = playtest.GetBool("protect-prefs", true),
                Folder = Path.Combine(Path.GetDirectoryName(playtest.Path)!, "Playtest_Runs", $"{DateTime.Now:yyyyMMdd-HHmmss}-{flow}"),
            };
            if (!EditorApplication.isPlaying && EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("The project has compilation errors; fix them before a playtest.");
            if (job.Protect)
                PlaytestSaveGuard.Snapshot();
            if (!EditorApplication.isPlaying)
            {
                var startScene = playtest.Get("start-scene", string.Empty);
                if (startScene.Length > 0)
                {
                    var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(startScene);
                    if (scene == null)
                        throw new ArgumentException($"start-scene '{startScene}' is not a scene asset.");
                    job.PreviousStartScene = EditorSceneManager.playModeStartScene != null
                        ? AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)
                        : "-";
                    EditorSceneManager.playModeStartScene = scene;
                }
                job.EnteredPlay = true;
                AgentActivity.MarkAgentPlay();
                EditorApplication.isPlaying = true;
            }
            _job = job;
            Save();
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
            message = $"Playtest '{flow}' started{(job.EnteredPlay ? ", entering play mode" : string.Empty)}.";
            return job.RequestId.Length > 0;
        }

        public static string Status()
        {
            if (PlaytestDeviceRun.Running)
                return PlaytestDeviceRun.Progress;
            if (_run != null)
                return _run.Progress();
            if (_job != null)
                return _job.Pending
                    ? $"Playtest '{_job.Flow}' finished; restoring saves and building the contact sheet once play mode has ended."
                    : $"Playtest '{_job.Flow}' is waiting for play mode.";
            var last = SessionState.GetString(LastKey, string.Empty);
            return last.Length > 0 ? last : "No playtest has run in this Editor session.";
        }

        public static string Stop()
        {
            if (PlaytestDeviceRun.Running)
                return PlaytestDeviceRun.Stop();
            if (_run != null)
            {
                _run.Fail("stopped by action=stop");
                return Finish();
            }
            if (_job == null)
                return "No playtest is running.";
            if (_job.Pending)
                return Complete();
            return Deliver("Playtest stopped before play mode started.", false);
        }

        #endregion

        #region Lifecycle

        static void Pump()
        {
            if (_job == null)
            {
                EditorApplication.update -= Pump;
                return;
            }
            var elapsed = new TimeSpan(DateTime.UtcNow.Ticks - _job.StartTicks).TotalSeconds;
            if (_job.Pending)
            {
                var playOver = !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;
                if (playOver || !_job.EnteredPlay || _job.Stay || elapsed > _job.FinishedSeconds + ExitPlayGiveUpSeconds)
                    Complete();
                return;
            }
            if (_run != null)
            {
                if (!EditorApplication.isPlaying)
                {
                    _run.Fail("play mode ended during the run");
                    Finish();
                }
                else if (_run.Done)
                    Finish();
                return;
            }
            var settled = !EditorApplication.isCompiling && !EditorApplication.isUpdating;
            if (EditorApplication.isPlaying && settled)
            {
                Begin(_job);
                return;
            }
            if (settled && !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode && elapsed > 1)
                Deliver(EditorUtility.scriptCompilationFailed
                    ? "Unity refused to enter play mode: the project has compilation errors."
                    : "Unity refused to enter play mode. Check the Console.", false);
            else if (elapsed > EnterPlayGiveUpSeconds)
                Deliver($"Play mode did not start within {EnterPlayGiveUpSeconds:0} s.", false);
        }

        static void Begin(Job job)
        {
            if (job.PreviousStartScene.Length > 0)
            {
                EditorSceneManager.playModeStartScene = job.PreviousStartScene == "-"
                    ? null
                    : AssetDatabase.LoadAssetAtPath<SceneAsset>(job.PreviousStartScene);
                job.PreviousStartScene = string.Empty;
                Save();
            }
            PlaytestFile playtest;
            List<PlaytestStep> steps;
            try
            {
                playtest = PlaytestFile.Load(job.File);
                steps = playtest.Expand(job.Flow);
            }
            catch (Exception e)
            {
                Deliver($"Playtest '{job.Flow}' could not start: {e.Message}", false);
                return;
            }
            _run = new PlaytestFlow(new EditorHost(job.Folder), new PlaytestFlowOptions { Flow = job.Flow, Speed = job.Speed, Cursor = job.Cursor },
                playtest, steps);
            Application.onBeforeRender -= Tick;
            Application.onBeforeRender += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            PlaySessionGuard.Arm(Abandon);
        }

        static void Tick()
        {
            if (_run == null || _run.Done)
                return;
            try { _run.Step(); }
            catch (Exception e)
            {
                // tagged, so the run does not also count its own crash as a game console error
                _run.Fail($"runner error: {e.GetType().Name}: {e.Message}");
                Debug.LogError($"{LogTag} runner error: {e}");
            }
        }

        static void OnLog(string message, string stackTrace, LogType type)
        {
            if (type is LogType.Error or LogType.Exception or LogType.Assert && !message.StartsWith(LogTag, StringComparison.Ordinal))
                _run?.AddConsoleError(message);
        }

        // the flow is over: report written, then play mode ends and Complete runs in edit mode
        static string Finish()
        {
            var run = _run!;
            _run = null;
            Application.onBeforeRender -= Tick;
            Application.logMessageReceived -= OnLog;
            run.Close();
            var job = _job!;
            job.Pending = true;
            job.Passed = run.Passed;
            job.FinishedSeconds = new TimeSpan(DateTime.UtcNow.Ticks - job.StartTicks).TotalSeconds;
            Save();
            if (job.EnteredPlay && !job.Stay && EditorApplication.isPlaying)
            {
                EditorEvents.NextTick(() => EditorApplication.isPlaying = false);
                return $"Playtest '{job.Flow}' finished; the report follows once play mode has ended.";
            }
            return Complete();
        }

        static string Complete()
        {
            var job = _job!;
            var reportPath = Path.Combine(job.Folder, "report.md");
            var text = new StringBuilder(File.Exists(reportPath) ? File.ReadAllText(reportPath) : $"Playtest '{job.Flow}': report missing.\n");
            try
            {
                text.Append(job.Protect
                    ? PlaytestSaveGuard.Compare(restore: !EditorApplication.isPlaying)
                    : "\n## Side effects\n\nNot checked: `protect-prefs: off`.\n");
            }
            catch (Exception e)
            {
                text.Append($"\n## Side effects\n\nCheck failed: {e.Message}\n");
            }
            try
            {
                var title = text.ToString().Split('\n')[0].TrimEnd('.') + $" ({DateTime.Now:yyyy-MM-dd HH:mm})";
                var sheets = PlaytestCapture.BuildSheets(job.Folder, title);
                text.Append(sheets.Count == 0 ? "\nContact sheet: none (no shots in this flow).\n" : "\n");
                foreach (var sheet in sheets)
                    text.Append($"Contact sheet: {Relative(sheet)}\n![]({sheet.Replace('\\', '/')})\n");
            }
            catch (Exception e)
            {
                text.Append($"\nContact sheet failed: {e.Message}\n");
            }
            try
            {
                Directory.CreateDirectory(job.Folder);
                File.WriteAllText(reportPath, text.ToString());
                text.Append($"Report: {Relative(reportPath)}\n");
            }
            catch (Exception e)
            {
                text.Append($"Report not written: {e.Message}\n");
            }
            return Deliver(text.ToString(), job.Passed);
        }

        // play mode ended without the Pump seeing it (another package's handler threw first)
        static void Abandon()
        {
            if (_run != null)
            {
                _run.Fail("play mode ended during the run");
                Finish();
            }
        }

        // the end of a device run: no play mode or saves on this side, only the answer
        public static void DeliverDevice(string report, bool passed, string requestId)
        {
            SessionState.SetString(LastKey, report);
            if (requestId.Length > 0)
            {
                var response = (passed ? ResponseCallTool.Success(report) : ResponseCallTool.Error(report)).SetRequestID(requestId);
                _ = MatrixPluginEditor.NotifyToolRequestCompleted(new RequestToolCompletedData { RequestId = requestId, Result = response });
            }
        }

        static string Deliver(string report, bool passed)
        {
            var job = _job;
            _job = null;
            SessionState.EraseString(JobKey);
            SessionState.SetString(LastKey, report);
            EditorApplication.update -= Pump;
            if (job == null)
                return report;
            if (job.PreviousStartScene.Length > 0)
                EditorSceneManager.playModeStartScene = job.PreviousStartScene == "-" ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(job.PreviousStartScene);
            if (job.EnteredPlay && !job.Stay && EditorApplication.isPlaying)
                EditorEvents.NextTick(() => EditorApplication.isPlaying = false);
            if (job.RequestId.Length > 0)
            {
                var response = (passed ? ResponseCallTool.Success(report) : ResponseCallTool.Error(report)).SetRequestID(job.RequestId);
                _ = MatrixPluginEditor.NotifyToolRequestCompleted(new RequestToolCompletedData { RequestId = job.RequestId, Result = response });
            }
            return report;
        }

        static void Save() => SessionState.SetString(JobKey, _job?.Serialize() ?? string.Empty);

        static string Relative(string path) => path.Replace('\\', '/').Replace(ProjectRoot.Replace('\\', '/') + "/", string.Empty);

        #endregion

        // the Game view of this Editor: UI Toolkit cursor over the window, frames read from the Game view
        sealed class EditorHost : IPlaytestHost
        {
            readonly string _folder;
            PlaytestCursorTheme? _theme;
            bool _maximized;

            public EditorHost(string folder) => _folder = folder;

            public PlaytestCursorState Cursor => PlaytestCursorOverlay.State;
            public bool CursorVisible => PlaytestCursorOverlay.Visible;

            public PlaytestCursorTheme ShowCursor(bool show, string themePath, string onlyLayers)
            {
                if (show)
                    PlaytestCursorOverlay.Show(themePath, onlyLayers);
                return _theme = PlaytestCursorOverlay.Theme ?? PlaytestCursorTheme.Load(themePath);
            }

            public void RefreshCursor() => PlaytestCursorOverlay.Refresh();

            public float PointsPerScreenPixel() => PlaytestCursorOverlay.PointsPerScreenPixel();

            public void Begin(PlaytestFile file)
            {
                var view = PlaytestCursorOverlay.GameViews().FirstOrDefault();
                if (view == null)
                    return;
                view.Focus();
                if (file.GetBool("maximize", false) && !view.maximized)
                {
                    view.maximized = true;
                    _maximized = true;
                }
            }

            public void End()
            {
                if (!_maximized)
                    return;
                var view = PlaytestCursorOverlay.GameViews().FirstOrDefault();
                if (view != null)
                    view.maximized = false;
            }

            public Color32[]? Capture(bool withCursor, out int width, out int height, out string error) =>
                PlaytestCapture.Frame(withCursor ? Cursor : null, withCursor ? _theme : null, out width, out height, out error);

            public string SaveShot(string name, Color32[] pixels, int width, int height, bool tile)
            {
                var path = Path.Combine(_folder, name);
                if (tile)
                    PlaytestCapture.SaveTile(path, pixels, width, height);
                else
                    PlaytestCapture.SavePng(path, pixels, width, height);
                return Relative(path);
            }

            public void WriteReport(string report, List<PlaytestTile> tiles)
            {
                try
                {
                    Directory.CreateDirectory(_folder);
                    File.WriteAllText(Path.Combine(_folder, "report.md"), report);
                    PlaytestCapture.WriteManifest(_folder, tiles);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{LogTag} report not written: {e.Message}");
                }
            }
        }

        sealed class Job
        {
            const char Sep = '\u001f';
            const int Parts = 14;

            public string RequestId = string.Empty;
            public string File = string.Empty;
            public string Flow = string.Empty;
            public float Speed = 1f;
            public bool Cursor = true;
            public bool Stay;
            public bool EnteredPlay;
            public long StartTicks;
            // "-" means none was set; empty means nothing to restore
            public string PreviousStartScene = string.Empty;
            public bool Protect;
            public string Folder = string.Empty;
            // the flow is over and only the edit-mode part is left
            public bool Pending;
            public bool Passed;
            public double FinishedSeconds;

            public string Serialize() => string.Join(Sep.ToString(), RequestId, File, Flow,
                Speed.ToString(CultureInfo.InvariantCulture), Flag(Cursor), Flag(Stay), Flag(EnteredPlay),
                StartTicks.ToString(CultureInfo.InvariantCulture), PreviousStartScene, Flag(Protect), Folder, Flag(Pending), Flag(Passed),
                FinishedSeconds.ToString(CultureInfo.InvariantCulture));

            static string Flag(bool value) => value ? "1" : "0";

            public static Job? Parse(string raw)
            {
                var parts = raw.Split(Sep);
                if (parts.Length != Parts)
                    return null;
                return new Job
                {
                    RequestId = parts[0],
                    File = parts[1],
                    Flow = parts[2],
                    Speed = float.Parse(parts[3], CultureInfo.InvariantCulture),
                    Cursor = parts[4] == "1",
                    Stay = parts[5] == "1",
                    EnteredPlay = parts[6] == "1",
                    StartTicks = long.Parse(parts[7], CultureInfo.InvariantCulture),
                    PreviousStartScene = parts[8],
                    Protect = parts[9] == "1",
                    Folder = parts[10],
                    Pending = parts[11] == "1",
                    Passed = parts[12] == "1",
                    FinishedSeconds = double.Parse(parts[13], CultureInfo.InvariantCulture),
                };
            }
        }
    }
}
