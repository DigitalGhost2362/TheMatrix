#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Feeder.MCP.Editor.Playtest
{
    internal sealed class PlaytestTile
    {
        public int Step;
        public string Label = string.Empty;
        // relative to the run folder, or absolute in reports written before device runs
        public string Path = string.Empty;
        // a click's frames start a row of their own, so before / over / press / result line up
        public bool NewRow;

        public string Serialize() => string.Join("\t", Step.ToString(CultureInfo.InvariantCulture), NewRow ? "1" : "0", Label.Replace('\t', ' '), Path);

        public static PlaytestTile? Parse(string line)
        {
            var parts = line.Split('\t');
            return parts.Length == 4 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var step)
                ? new PlaytestTile { Step = step, NewRow = parts[1] == "1", Label = parts[2], Path = parts[3] }
                : null;
        }
    }

    // where a flow runs: the Editor's Game view, or the screen of a device build
    internal interface IPlaytestHost
    {
        PlaytestCursorState Cursor { get; }
        bool CursorVisible { get; }
        // shows the cursor and answers its theme; also called with the cursor off, for the theme alone
        PlaytestCursorTheme ShowCursor(bool show, string themePath, string onlyLayers);
        void RefreshCursor();
        float PointsPerScreenPixel();
        void Begin(PlaytestFile file);
        void End();
        // bottom row first; null with an error when there is no frame
        Color32[]? Capture(bool withCursor, out int width, out int height, out string error);
        // name is relative to the run folder; answers how the report shows the file
        string SaveShot(string name, Color32[] pixels, int width, int height, bool tile);
        void WriteReport(string report, List<PlaytestTile> tiles);
    }

    internal sealed class PlaytestFlowOptions
    {
        public string Flow = string.Empty;
        public float Speed = 1f;
        public bool Cursor = true;
    }

    // one flow of Playtest.md played step by step, one Step() per frame, the same on the Editor and on a device
    internal sealed class PlaytestFlow
    {
        enum Phase { Start, Seek, Move, Hit, Settle }

        sealed class Row
        {
            public string Step = string.Empty;
            public string Result = "skip";
            public double Ms;
            public string Note = string.Empty;
        }

        const int MaxConsoleErrors = 5;
        const double ShowBlockerAfter = 0.4;
        const int StableFrames = 3;

        readonly IPlaytestHost _host;
        readonly PlaytestFlowOptions _options;
        readonly List<PlaytestStep> _steps;
        readonly List<Row> _rows;
        readonly PlaytestCursorTheme _theme;
        readonly PlaytestCursorState _cursor;
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

        public PlaytestFlow(IPlaytestHost host, PlaytestFlowOptions options, PlaytestFile file, List<PlaytestStep> steps)
        {
            _host = host;
            _options = options;
            _steps = steps;
            _rows = steps.Select(s => new Row { Step = s.Raw }).ToList();
            _cursor = host.Cursor;
            _timeout = file.GetFloat("timeout", 5f);
            _settleMax = file.GetFloat("shot-settle", 2f);
            _autoshot = file.Get("autoshot", "off").Equals("clicks", StringComparison.OrdinalIgnoreCase);
            _failOnConsoleErrors = file.GetBool("fail-on-console-errors", true);
            _started = Now;
            PlaytestScene.Reset();
            host.Begin(file);
            // a cursor already on screen in this play session stays and keeps moving
            _useCursor = options.Cursor || host.CursorVisible;
            _theme = host.ShowCursor(_useCursor, file.Get("cursor-theme", string.Empty), file.Get("cursor-layers", string.Empty));
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
            ? $"Playtest '{_options.Flow}' running: step {_index + 1}/{_steps.Count} `{_steps[_index].Raw}` ({_phase}), {Now - _started:0.0} s."
            : $"Playtest '{_options.Flow}' finishing.";

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
                _host.RefreshCursor();
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
                        _cursor.Target = Normalize(rect);
                        _moveFrom = _cursor.Position;
                        var to = Normalize(_targetScreen);
                        var points = Vector2.Distance(Denormalize(_moveFrom), Denormalize(to));
                        var ms = Mathf.Clamp(_theme.moveMinMs + points * _theme.moveMsPerPoint, _theme.moveMinMs, _theme.moveMaxMs);
                        _moveSeconds = ms / 1000.0 / _options.Speed;
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
                    var to = Normalize(_targetScreen);
                    if (_target != null && PlaytestScene.Blocker(_target, out var screen, out var rect) == null)
                    {
                        _targetScreen = screen;
                        to = Normalize(screen);
                        _cursor.Target = Normalize(rect);
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

        int ClickFrame(double now) => _clickStart < 0 ? -1 : (int)((now - _clickStart) * _theme.fps * _options.Speed);

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

        Vector2 Denormalize(Vector2 normalized) => new Vector2(normalized.x * Screen.width, normalized.y * Screen.height) * _host.PointsPerScreenPixel();

        // screen point (pixels, bottom-left origin) -> 0..1 of the game screen, top-left origin
        static Vector2 Normalize(Vector2 screen) => new Vector2(
            Screen.width > 0 ? screen.x / Screen.width : 0.5f,
            Screen.height > 0 ? 1f - screen.y / Screen.height : 0.5f);

        static Rect Normalize(Rect screen)
        {
            var min = Normalize(new Vector2(screen.xMin, screen.yMax));
            var max = Normalize(new Vector2(screen.xMax, screen.yMin));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        static float Ease(float x) => x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) / 2f;

        static float ParseFloat(string value) => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

        string StepPrefix => (_index + 1).ToString("000", CultureInfo.InvariantCulture);

        void Shot(string name, string settle)
        {
            var pixels = _host.Capture(_useCursor, out var width, out var height, out var error);
            if (pixels == null)
            {
                _rows[_index].Note = "no capture: " + error;
                return;
            }
            // the step number keeps a flow that runs twice from overwriting its first shot
            var file = $"{StepPrefix}_{name}.png";
            var shown = _host.SaveShot(file, pixels, width, height, false);
            _tiles.Add(new PlaytestTile { Step = _index + 1, Label = $"#{_index + 1}  {name}", Path = file });
            _rows[_index].Note = shown + (settle.Length > 0 ? $" ({settle})" : string.Empty);
        }

        // autoshot: clicks; one row per click: before, cursor over the target, press, result
        void AutoFrame(string label, PlaytestStep step)
        {
            if (!_autoshot || !_stepFrames.Add(label) || (!_useCursor && label is "over" or "press"))
                return;
            var pixels = _host.Capture(_useCursor, out var width, out var height, out _);
            if (pixels == null)
                return;
            var file = $"frames/{StepPrefix}_{label}.png";
            _host.SaveShot(file, pixels, width, height, true);
            var target = step.Args[0].TrimEnd('/');
            target = target.Substring(target.LastIndexOf('/') + 1);
            _tiles.Add(new PlaytestTile { Step = _index + 1, Label = $"#{_index + 1} {label}  {target}", Path = file, NewRow = label == "before" });
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
            var type = FindType(args[0], "call", "Method", out var methodName);
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

        // the report and the frame list; the host adds the side effects and the contact sheet
        public void Close()
        {
            // the cursor stays on screen, idle, until play mode ends
            _cursor.ClickFrame = -1;
            _cursor.Target = null;
            _cursor.Caption = _cursor.Blocked = string.Empty;
            if (_useCursor)
                _host.RefreshCursor();
            Time.timeScale = _timeScale;
            _host.End();
            var seconds = Now - _started;
            var okCount = _rows.Count(r => r.Result is "ok" or "absent");
            var verdict = Passed ? "PASS" : _failure.Length == 0 ? $"FAIL (console errors: {_consoleErrorCount})" : "FAIL";
            var text = new StringBuilder();
            text.Append($"Playtest '{_options.Flow}' {verdict}: {okCount}/{_rows.Count} steps in {seconds:0.00} s, {_consoleErrorCount} console errors (speed {_options.Speed:0.##}, cursor {(_useCursor ? "on" : "off")}).\n");
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
            _host.WriteReport(text.ToString(), _tiles);
        }

        #region Members

        // the type by full name first, then by short name
        static Type FindType(string member, string verb, string kind, out string memberName)
        {
            var dot = member.LastIndexOf('.');
            if (dot <= 0)
                throw new ArgumentException($"{verb} '{member}': write Type.{kind}");
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
            var type = FindType(member, verb, "Member", out var name);
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

        internal static IEnumerable<Type> SafeTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null)!; }
        }

        #endregion
    }
}
