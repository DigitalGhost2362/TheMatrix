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
        static Run? _run;

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
        public static bool Start(string? file, string flow, float speed, bool? cursor, bool stay, string? requestId, out string message)
        {
            if (_job != null)
                throw new InvalidOperationException($"A playtest is already running (flow '{_job.Flow}'). Call action=stop first.");
            var playtest = LoadFile(file);
            if (playtest.Errors.Count > 0)
                throw new FormatException(playtest.Describe());
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
            _run = new Run(job, playtest, steps);
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

        #region Members

        // the type by full name first, then by short name
        static Type FindType(string member, string verb, out string memberName)
        {
            var dot = member.LastIndexOf('.');
            if (dot <= 0)
                throw new ArgumentException($"{verb} '{member}': write Type.Member");
            var typeName = member.Substring(0, dot);
            memberName = member.Substring(dot + 1);
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(typeName, false))
                .FirstOrDefault(t => t != null)
                ?? AppDomain.CurrentDomain.GetAssemblies().SelectMany(SafeTypes).FirstOrDefault(t => t.Name == typeName)
                ?? throw new ArgumentException($"{verb}: no type '{typeName}'");
        }

        // a static field, property or parameterless method; a throwing getter or method surfaces as TargetInvocationException
        internal static object? ReadMember(string member, string verb)
        {
            const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var type = FindType(member, verb, out var name);
            var field = type.GetField(name, Static);
            if (field != null)
                return field.GetValue(null);
            var property = type.GetProperty(name, Static);
            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(null);
            var method = type.GetMethod(name, Static, null, Type.EmptyTypes, null)
                ?? throw new ArgumentException($"{verb}: no static field, property or parameterless method {member}");
            return method.Invoke(null, null);
        }

        // what a wait compares: invariant text, "null" for null
        internal static string MemberText(object? value) => value == null ? "null" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";

        static IEnumerable<Type> SafeTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null)!; }
        }

        #endregion

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

        sealed class Row
        {
            public string Step = string.Empty;
            public string Result = "skip";
            public double Ms;
            public string Note = string.Empty;
        }

        sealed class Run
        {
            enum Phase { Start, Seek, Move, Hit, Settle }

            const int MaxConsoleErrors = 5;
            const double ShowBlockerAfter = 0.4;
            const int StableFrames = 3;

            readonly Job _job;
            readonly List<PlaytestStep> _steps;
            readonly List<Row> _rows;
            readonly PlaytestCursorTheme _theme;
            readonly PlaytestCursorState _cursor = PlaytestCursorOverlay.State;
            readonly bool _useCursor;
            readonly bool _autoshot;
            readonly bool _failOnConsoleErrors;
            readonly float _settleMax;
            readonly float _timeout;
            readonly float _timeScale = Time.timeScale;
            readonly List<string> _hidden = new List<string>();
            readonly List<string> _consoleErrors = new List<string>();
            readonly List<PlaytestTile> _tiles = new List<PlaytestTile>();
            readonly HashSet<string> _stepFrames = new HashSet<string>();
            readonly double _started;
            readonly bool _maximized;
            int _consoleErrorCount;
            int _index;
            Phase _phase;
            double _stepStart;
            int _frames;
            GameObject? _target;
            Vector2 _targetScreen;
            Vector2 _moveFrom;
            double _moveStart;
            double _moveSeconds;
            double _clickStart = -1;
            double _settleStart;
            long _lastSignature;
            int _stable;
            string _failure = string.Empty;

            public Run(Job job, PlaytestFile file, List<PlaytestStep> steps)
            {
                _job = job;
                _steps = steps;
                _rows = steps.Select(s => new Row { Step = s.Raw }).ToList();
                _timeout = file.GetFloat("timeout", 5f);
                _settleMax = file.GetFloat("shot-settle", 2f);
                _autoshot = file.Get("autoshot", "off").Equals("clicks", StringComparison.OrdinalIgnoreCase);
                _failOnConsoleErrors = file.GetBool("fail-on-console-errors", true);
                _started = Now;
                PlaytestScene.Reset();
                var view = PlaytestCursorOverlay.GameViews().FirstOrDefault();
                if (view != null)
                {
                    view.Focus();
                    if (file.GetBool("maximize", false) && !view.maximized)
                    {
                        view.maximized = true;
                        _maximized = true;
                    }
                }
                // a cursor already on screen in this play session stays and keeps moving
                _useCursor = job.Cursor || PlaytestCursorOverlay.Visible;
                var themePath = file.Get("cursor-theme", string.Empty);
                if (_useCursor)
                    PlaytestCursorOverlay.Show(themePath, file.Get("cursor-layers", string.Empty));
                _theme = PlaytestCursorOverlay.Theme ?? PlaytestCursorTheme.Load(themePath);
            }

            static double Now => Time.realtimeSinceStartupAsDouble;

            public bool Done { get; private set; }
            public bool Passed => _failure.Length == 0 && Done && !(_failOnConsoleErrors && _consoleErrorCount > 0);

            public void AddConsoleError(string message)
            {
                _consoleErrorCount++;
                if (_consoleErrors.Count < MaxConsoleErrors)
                    _consoleErrors.Add($"step {Math.Min(_index + 1, _steps.Count)}: {message.Split('\n')[0]}");
            }

            public string Progress() => _index < _steps.Count
                ? $"Playtest '{_job.Flow}' running: step {_index + 1}/{_steps.Count} `{_steps[_index].Raw}` ({_phase}), {Now - _started:0.0} s."
                : $"Playtest '{_job.Flow}' finishing.";

            public void Fail(string reason)
            {
                if (_index < _rows.Count)
                {
                    _rows[_index].Result = "FAIL";
                    _rows[_index].Note = reason;
                    _rows[_index].Ms = (Now - _stepStart) * 1000;
                }
                _failure = $"step {_index + 1} `{(_index < _steps.Count ? _steps[_index].Raw : "?")}`: {reason}";
                Done = true;
            }

            public void Step()
            {
                var now = Now;
                // a burst of instant steps (hide, call, timescale) runs in one frame
                for (var guard = 0; guard < 64 && !Done; guard++)
                {
                    if (_index >= _steps.Count)
                    {
                        // the last click plays to its end before the cursor rests
                        Done = !_useCursor || _clickStart < 0;
                        if (Done)
                            _cursor.Caption = string.Empty;
                        break;
                    }
                    if (_phase == Phase.Start)
                    {
                        _phase = Phase.Seek;
                        _stepStart = now;
                        _frames = 0;
                        _stepFrames.Clear();
                        _cursor.Caption = $"{_index + 1}/{_steps.Count}  {_steps[_index].Raw}";
                        _cursor.Blocked = string.Empty;
                    }
                    if (!Execute(_steps[_index], now))
                        break;
                    _rows[_index].Result = _rows[_index].Result == "skip" ? "ok" : _rows[_index].Result;
                    _rows[_index].Ms = (now - _stepStart) * 1000;
                    _index++;
                    _phase = Phase.Start;
                    _cursor.Target = null;
                }
                AnimateClick(now);
                if (_useCursor)
                    PlaytestCursorOverlay.Refresh();
            }

            // true when the step is finished
            bool Execute(PlaytestStep step, double now)
            {
                var waited = now - _stepStart;
                switch (step.Verb)
                {
                    case "click":
                        return Click(step, now, waited);
                    case "expect-active":
                    case "expect-inactive":
                    {
                        var go = PlaytestScene.Find(step.Args[0]);
                        var active = go != null && go.activeInHierarchy;
                        if (active == (step.Verb == "expect-active"))
                            return true;
                        if (waited > Timeout(step, 1))
                            Fail(go == null ? "not found" : active ? "still active" : "not active");
                        return false;
                    }
                    case "expect-visible":
                    case "expect-hidden":
                    {
                        var go = PlaytestScene.Find(step.Args[0]);
                        var why = go == null ? "not found" : PlaytestScene.Hidden(go);
                        if ((why == null) == (step.Verb == "expect-visible"))
                            return true;
                        if (waited > Timeout(step, 1))
                            Fail(why ?? "still visible");
                        return false;
                    }
                    case "wait":
                        return waited >= ParseFloat(step.Args[0]);
                    case "hide":
                    {
                        var go = PlaytestScene.Find(step.Args[0]);
                        if (go == null || !go.activeSelf)
                        {
                            _rows[_index].Result = "absent";
                            return true;
                        }
                        go.SetActive(false);
                        _hidden.Add(step.Args[0]);
                        return true;
                    }
                    case "hide-type":
                    {
                        var before = _hidden.Count;
                        PlaytestScene.HideType(step.Args[0], _hidden);
                        if (_hidden.Count == before)
                            _rows[_index].Result = "absent";
                        else
                            _rows[_index].Note = $"hid {_hidden.Count - before}";
                        return true;
                    }
                    case "shot":
                    {
                        // at least two frames, so what the previous step opened is drawn, then until the UI stops moving
                        if (_frames++ == 0)
                            BeginSettle(now);
                        if (_frames < 3)
                            return false;
                        if (!Settled(now, step.Args.Length > 1 ? ParseFloat(step.Args[1]) : _settleMax, out var settle))
                            return false;
                        Shot(step.Args[0], settle);
                        return true;
                    }
                    case "call":
                        return Reflected(step, () =>
                        {
                            _rows[_index].Note = Call(step.Args);
                            return true;
                        });
                    case "wait-until":
                        return Reflected(step, () =>
                        {
                            var value = MemberText(ReadMember(step.Args[0], step.Verb));
                            if (string.Equals(value, step.Args[1], StringComparison.OrdinalIgnoreCase))
                                return true;
                            if (waited > Timeout(step, 2))
                                Fail($"still {value}");
                            return false;
                        });
                    case "timescale":
                        Time.timeScale = ParseFloat(step.Args[0]);
                        return true;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(step.Verb), step.Verb, "verb without a runner");
                }
            }

            bool Click(PlaytestStep step, double now, double waited)
            {
                switch (_phase)
                {
                    case Phase.Seek:
                    {
                        _target = PlaytestScene.Find(step.Args[0]);
                        var blocker = _target == null ? "not found" : PlaytestScene.Blocker(_target, out _targetScreen, out var rect);
                        if (blocker == null)
                        {
                            _cursor.Blocked = string.Empty;
                            AutoFrame("before", step);
                            if (!_useCursor)
                            {
                                PlaytestScene.Click(_target!, _targetScreen);
                                return AfterClick(now);
                            }
                            PlaytestScene.Blocker(_target!, out _, out rect);
                            _cursor.Target = PlaytestCursorOverlay.Normalize(rect);
                            _moveFrom = _cursor.Position;
                            var to = PlaytestCursorOverlay.Normalize(_targetScreen);
                            var points = Vector2.Distance(Denormalize(_moveFrom), Denormalize(to));
                            var ms = Mathf.Clamp(_theme.moveMinMs + points * _theme.moveMsPerPoint, _theme.moveMinMs, _theme.moveMaxMs);
                            _moveSeconds = ms / 1000.0 / _job.Speed;
                            _moveStart = now;
                            _phase = Phase.Move;
                            return false;
                        }
                        if (waited > ShowBlockerAfter && blocker.StartsWith("covered", StringComparison.Ordinal))
                            _cursor.Blocked = blocker;
                        if (waited > Timeout(step, 1))
                            Fail(blocker);
                        return false;
                    }
                    case Phase.Move:
                    {
                        var to = PlaytestCursorOverlay.Normalize(_targetScreen);
                        if (_target != null && PlaytestScene.Blocker(_target, out var screen, out var rect) == null)
                        {
                            _targetScreen = screen;
                            to = PlaytestCursorOverlay.Normalize(screen);
                            _cursor.Target = PlaytestCursorOverlay.Normalize(rect);
                        }
                        var k = Math.Min(1.0, (now - _moveStart) / _moveSeconds);
                        _cursor.Position = Vector2.Lerp(_moveFrom, to, Ease((float)k));
                        if (k < 1.0)
                            return false;
                        AutoFrame("over", step);
                        _clickStart = now;
                        _phase = Phase.Hit;
                        return false;
                    }
                    case Phase.Hit:
                    {
                        if (ClickFrame(now) < _theme.hitFrame)
                            return false;
                        var blocker = _target == null ? "gone" : PlaytestScene.Blocker(_target, out _targetScreen, out _);
                        if (blocker != null)
                        {
                            _phase = Phase.Seek;
                            return false;
                        }
                        _cursor.ClickFrame = ClickFrame(now);
                        AutoFrame("press", step);
                        PlaytestScene.Click(_target!, _targetScreen);
                        return AfterClick(now);
                    }
                    case Phase.Settle:
                    {
                        if (!Settled(now, _settleMax, out _))
                            return false;
                        AutoFrame("result", step);
                        return true;
                    }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(_phase), _phase, null);
                }
            }

            // with autoshot the step waits for the screen it opened, for the result frame
            bool AfterClick(double now)
            {
                if (!_autoshot)
                    return true;
                BeginSettle(now);
                _phase = Phase.Settle;
                return false;
            }

            void BeginSettle(double now)
            {
                _settleStart = now;
                _stable = 0;
                _lastSignature = long.MinValue;
            }

            bool Settled(double now, float max, out string note)
            {
                note = string.Empty;
                if (max <= 0)
                    return true;
                var signature = PlaytestScene.UiSignature();
                _stable = signature == _lastSignature && PlaytestScene.FiniteTweensPlaying() == 0 ? _stable + 1 : 0;
                _lastSignature = signature;
                var waited = now - _settleStart;
                if (_stable >= StableFrames && waited >= 0.1)
                {
                    note = $"settled {waited:0.00} s";
                    return true;
                }
                if (waited < max)
                    return false;
                note = $"still moving after {max:0.#} s";
                return true;
            }

            float Timeout(PlaytestStep step, int argIndex) => step.Args.Length > argIndex ? ParseFloat(step.Args[argIndex]) : _timeout;

            int ClickFrame(double now) => _clickStart < 0 ? -1 : (int)((now - _clickStart) * _theme.fps * _job.Speed);

            void AnimateClick(double now)
            {
                var frame = ClickFrame(now);
                if (frame >= _theme.frames)
                {
                    _clickStart = -1;
                    frame = -1;
                }
                _cursor.ClickFrame = frame;
            }

            Vector2 Denormalize(Vector2 normalized) => new Vector2(normalized.x * Screen.width, normalized.y * Screen.height) * PlaytestCursorOverlay.PointsPerScreenPixel();

            static float Ease(float x) => x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) / 2f;

            static float ParseFloat(string value) => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

            string StepPrefix => (_index + 1).ToString("000", CultureInfo.InvariantCulture);

            Color32[]? Capture(out int width, out int height, out string error) =>
                PlaytestCapture.Frame(_useCursor ? _cursor : null, _useCursor ? _theme : null, out width, out height, out error);

            void Shot(string name, string settle)
            {
                var pixels = Capture(out var width, out var height, out var error);
                if (pixels == null)
                {
                    _rows[_index].Note = "no capture: " + error;
                    return;
                }
                // the step number keeps a flow that runs twice from overwriting its first shot
                var path = Path.Combine(_job.Folder, $"{StepPrefix}_{name}.png");
                PlaytestCapture.SavePng(path, pixels, width, height);
                _tiles.Add(new PlaytestTile { Step = _index + 1, Label = $"#{_index + 1}  {name}", Path = path });
                _rows[_index].Note = Relative(path) + (settle.Length > 0 ? $" ({settle})" : string.Empty);
            }

            // autoshot: clicks; one row per click: before, cursor over the target, press, result
            void AutoFrame(string label, PlaytestStep step)
            {
                if (!_autoshot || !_stepFrames.Add(label) || (!_useCursor && label is "over" or "press"))
                    return;
                var pixels = Capture(out var width, out var height, out _);
                if (pixels == null)
                    return;
                var path = Path.Combine(_job.Folder, "frames", $"{StepPrefix}_{label}.png");
                PlaytestCapture.SaveTile(path, pixels, width, height);
                var target = step.Args[0].TrimEnd('/');
                target = target.Substring(target.LastIndexOf('/') + 1);
                _tiles.Add(new PlaytestTile { Step = _index + 1, Label = $"#{_index + 1} {label}  {target}", Path = path, NewRow = label == "before" });
            }

            // a called check or read that throws is a failed step of the flow, not a runner crash: no console entry
            bool Reflected(PlaytestStep step, Func<bool> run)
            {
                try
                {
                    return run();
                }
                catch (TargetInvocationException e) when (e.InnerException != null)
                {
                    Fail($"{step.Args[0]}: {e.InnerException.GetType().Name}: {e.InnerException.Message}");
                    return false;
                }
                catch (Exception e) when (e is ArgumentException or FormatException or InvalidCastException or OverflowException)
                {
                    Fail($"{step.Args[0]}: {e.Message}");
                    return false;
                }
            }

            static string Call(string[] args)
            {
                var type = FindType(args[0], "call", out var methodName);
                var values = args.Skip(1).ToArray();
                var method = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == values.Length)
                    ?? throw new ArgumentException($"call: no static {args[0]} with {values.Length} parameters");
                var parameters = method.GetParameters();
                var converted = new object?[values.Length];
                for (var i = 0; i < values.Length; i++)
                {
                    var target = parameters[i].ParameterType;
                    var raw = values[i].Trim('"');
                    converted[i] = target.IsEnum ? Enum.Parse(target, raw, true) : Convert.ChangeType(raw, target, CultureInfo.InvariantCulture);
                }
                var result = method.Invoke(null, converted);
                return result == null ? string.Empty : $"returned {result}";
            }

            // writes report.md and the frame list; the side effects and the contact sheet are added after play mode
            public void Close()
            {
                // the cursor stays on screen, idle, until play mode ends
                _cursor.ClickFrame = -1;
                _cursor.Target = null;
                _cursor.Caption = _cursor.Blocked = string.Empty;
                PlaytestCursorOverlay.Refresh();
                Time.timeScale = _timeScale;
                if (_maximized)
                {
                    var view = PlaytestCursorOverlay.GameViews().FirstOrDefault();
                    if (view != null)
                        view.maximized = false;
                }
                var seconds = Now - _started;
                var okCount = _rows.Count(r => r.Result is "ok" or "absent");
                var verdict = Passed ? "PASS" : _failure.Length == 0 ? $"FAIL (console errors: {_consoleErrorCount})" : "FAIL";
                var text = new StringBuilder();
                text.Append($"Playtest '{_job.Flow}' {verdict}: {okCount}/{_rows.Count} steps in {seconds:0.00} s, {_consoleErrorCount} console errors (speed {_job.Speed:0.##}, cursor {(_useCursor ? "on" : "off")}).\n");
                if (_failure.Length > 0)
                    text.Append("Failed at ").Append(_failure).Append('\n');
                text.Append("\n| # | step | result | ms | note |\n|---|---|---|---|---|\n");
                for (var i = 0; i < _rows.Count; i++)
                {
                    var row = _rows[i];
                    text.Append($"| {i + 1} | `{row.Step.Replace("|", "/")}` | {row.Result} | {(row.Result == "skip" ? "" : row.Ms.ToString("0"))} | {row.Note.Replace("|", "/")} |\n");
                }
                text.Append($"\nConsole errors during the run: {_consoleErrorCount}{(_failOnConsoleErrors && _consoleErrorCount > 0 ? " (the run fails on them; fail-on-console-errors: off to allow)" : string.Empty)}\n");
                foreach (var error in _consoleErrors)
                    text.Append("- ").Append(error).Append('\n');
                if (_hidden.Count > 0)
                    text.Append($"Hidden for the run: {string.Join(", ", _hidden)}\n");
                try
                {
                    Directory.CreateDirectory(_job.Folder);
                    File.WriteAllText(Path.Combine(_job.Folder, "report.md"), text.ToString());
                    PlaytestCapture.WriteManifest(_job.Folder, _tiles);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{LogTag} report not written: {e.Message}");
                }
            }
        }
    }
}
