#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.SceneManagement;

namespace Feeder.MCP.Editor.Watch
{
    // one play session of recording: input edges, pointer targets, player samples, scenes, logs and frames
    internal sealed class WatchRecorder
    {
        const double SampleInterval = 0.25;
        const double FrameInterval = 2.0;
        const double MinFrameGap = 0.4;
        const double FlushInterval = 1.0;
        const double PlayerSearchInterval = 2.0;
        const float DragThreshold = 0.03f;
        const int MaxWarnings = 30;
        const int MaxLogText = 400;

        struct WatchTick { }

        static WatchRecorder? _current;

        readonly string _folder;
        readonly string _playerHint;
        readonly double _start;
        readonly StreamWriter _writer;
        readonly WatchInput _input = new WatchInput();
        readonly Dictionary<string, double> _keyDown = new Dictionary<string, double>();
        readonly Dictionary<string, (int Id, int Count)> _logs = new Dictionary<string, (int Id, int Count)>();
        readonly Queue<(LogType Type, string Text, string Stack)> _logQueue = new Queue<(LogType, string, string)>();
        readonly RaycastHit[] _groundHits = new RaycastHit[8];

        int _frames;
        int _markers;
        int _warnings;
        int _logIdBase;
        int _tickErrors;
        double _nextSample;
        double _nextFrame;
        double _lastFrameAt = double.NegativeInfinity;
        double _nextFlush;
        double _nextPlayerSearch;
        double _sceneFrameAt = -1;
        string _lastFrame = string.Empty;
        float _timeScale;

        Transform? _player;
        CharacterController? _controller;
        Collider? _collider;
        Vector3 _lastPosition;
        double _lastPositionAt;

        bool _pointerActive;
        double _pointerAt;
        Vector2 _pointerStart;
        WatchTarget _pointerTarget;
        string _pointerFrame = string.Empty;

        public WatchRecorder(string folder, string playerHint, double start, bool resumed)
        {
            _folder = folder;
            _playerHint = playerHint;
            _start = start;
            Directory.CreateDirectory(Path.Combine(folder, "frames"));
            _frames = Directory.GetFiles(Path.Combine(folder, "frames"), "f*.jpg").Length;
            var eventsPath = Path.Combine(folder, WatchSummary.EventsFile);
            if (resumed && File.Exists(eventsPath))
            {
                // ids keep counting across a reload during play
                foreach (var line in File.ReadLines(eventsPath))
                {
                    if (line.Contains("\"k\":\"marker\""))
                        _markers++;
                    else if (line.Contains("\"k\":\"log\""))
                        _logIdBase++;
                }
            }
            _writer = new StreamWriter(eventsPath, append: true, new UTF8Encoding(false));
            _timeScale = Time.timeScale;
            try
            {
                _input.Begin();
            }
            catch
            {
                _writer.Dispose();
                throw;
            }

            var size = $"{Screen.width}x{Screen.height}";
            Write(new WatchEvent
            {
                t = Now,
                k = WatchEvent.Play,
                s = SceneManager.GetActiveScene().name,
                d = $"{size}|{_input.Backend}|{(resumed ? "resumed after reload" : Application.unityVersion)}"
            });
            FindPlayer();
            // the first frames after entering play mode are often not rendered yet
            _nextFrame = Now + 1.5;

            Application.logMessageReceivedThreaded += OnLog;
            SceneManager.sceneLoaded += OnSceneLoaded;
            _current = this;
            SetHook(true);
        }

        public int EventCount { get; private set; }
        public int FrameCount => _frames;
        public double Now => EditorApplication.timeSinceStartup - _start;

        // final = the session ends; otherwise a domain reload interrupts it and a new recorder appends later
        public void End(string reason, bool final)
        {
            SetHook(false);
            _current = null;
            Application.logMessageReceivedThreaded -= OnLog;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            try
            {
                DrainLogs();
                var now = Now;
                foreach (var pair in _keyDown.ToList())
                    WriteKey(pair.Key, pair.Value, now);
                _keyDown.Clear();
                foreach (var log in _logs.Values.Where(log => log.Count > 1))
                    Write(new WatchEvent { t = now, k = WatchEvent.LogCount, n = log.Id, v = log.Count });

                var trace = string.Empty;
                try
                {
                    trace = _input.End(_folder);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Matrix Watch] input trace not saved: {e.Message}");
                }
                if (final)
                    Write(new WatchEvent { t = now, k = WatchEvent.End, s = reason, d = trace });
            }
            finally
            {
                try
                {
                    WatchCapture.Flush();
                }
                finally
                {
                    _writer.Dispose();
                }
            }
        }

        void Tick()
        {
            var now = Now;
            _input.Poll();

            foreach (var key in _input.Pressed)
            {
                if (key == WatchInput.MarkerKey)
                    Write(new WatchEvent { t = now, k = WatchEvent.Marker, n = ++_markers, f = CaptureFrame(now) });
                else
                    _keyDown[key] = now;
            }
            foreach (var key in _input.Released)
            {
                if (_keyDown.TryGetValue(key, out var downAt))
                {
                    _keyDown.Remove(key);
                    WriteKey(key, downAt, now);
                }
            }

            TrackPointer(now);

            if (Math.Abs(Time.timeScale - _timeScale) > 0.001f)
            {
                _timeScale = Time.timeScale;
                Write(new WatchEvent { t = now, k = WatchEvent.TimeScale, v = _timeScale });
            }

            SamplePlayer(now);
            DrainLogs();

            if (_sceneFrameAt >= 0 && now >= _sceneFrameAt)
            {
                _sceneFrameAt = -1;
                CaptureFrame(now);
            }
            if (now >= _nextFrame)
                CaptureFrame(now);

            WatchCapture.ProcessDone();
            if (now >= _nextFlush)
            {
                _nextFlush = now + FlushInterval;
                _writer.Flush();
            }
        }

        void TrackPointer(double now)
        {
            var screen = new Vector2(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
            if (_input.PointerDown)
            {
                _pointerActive = true;
                _pointerAt = now;
                _pointerStart = _input.PointerPosition / screen;
                _pointerTarget = WatchTargets.Resolve(_input.PointerPosition);
                _pointerFrame = CaptureFrame(now);
            }
            if (_input.PointerUp && _pointerActive)
            {
                _pointerActive = false;
                var end = _input.PointerPosition / screen;
                var drag = (end - _pointerStart).magnitude > DragThreshold;
                Write(new WatchEvent
                {
                    t = _pointerAt,
                    k = drag ? WatchEvent.Drag : WatchEvent.Click,
                    s = _pointerTarget.Path,
                    d = _pointerTarget.Detail,
                    p = _pointerTarget.Point,
                    a = _pointerStart,
                    b = drag ? end : Vector2.zero,
                    v = (float)(now - _pointerAt),
                    f = _pointerFrame
                });
            }
        }

        void SamplePlayer(double now)
        {
            if (_player == null)
            {
                if (now < _nextPlayerSearch)
                    return;
                _nextPlayerSearch = now + PlayerSearchInterval;
                if (!FindPlayer())
                    return;
            }
            if (now < _nextSample || _player == null)
                return;
            _nextSample = now + SampleInterval;

            var position = _player.position;
            var elapsed = now - _lastPositionAt;
            var flat = new Vector2(position.x - _lastPosition.x, position.z - _lastPosition.z);
            var speed = elapsed > 0 && _lastPositionAt > 0 ? (float)(flat.magnitude / elapsed) : 0f;
            _lastPosition = position;
            _lastPositionAt = now;

            var flags = (Grounded(position) ? WatchEvent.FlagGrounded : 0) |
                        (_input.Move.sqrMagnitude > 0.04f && Time.timeScale > 0f ? WatchEvent.FlagMoveInput : 0);
            Write(new WatchEvent { t = now, k = WatchEvent.Pos, p = position, r = _player.eulerAngles.y, v = speed, n = flags });
        }

        bool Grounded(Vector3 position)
        {
            if (_controller != null && _controller.isGrounded)
                return true;
            var feet = _collider != null ? _collider.bounds.min.y : position.y;
            var count = Physics.RaycastNonAlloc(new Vector3(position.x, feet + 0.3f, position.z), Vector3.down, _groundHits,
                0.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
                if (_player != null && !_groundHits[i].collider.transform.IsChildOf(_player))
                    return true;
            return false;
        }

        bool FindPlayer()
        {
            GameObject? found = null;
            if (_playerHint.Length > 0)
                found = GameObject.Find(_playerHint);
            if (found == null)
                found = GameObject.FindWithTag("Player");
            if (found == null)
                found = UnityEngine.Object.FindAnyObjectByType<CharacterController>()?.gameObject;
            if (found == null)
                return false;

            _player = found.transform;
            _controller = found.GetComponent<CharacterController>();
            _collider = found.GetComponents<Collider>().FirstOrDefault(c => !c.isTrigger)
                        ?? found.GetComponentsInChildren<Collider>().FirstOrDefault(c => !c.isTrigger);
            _lastPosition = _player.position;
            _lastPositionAt = 0;
            Write(new WatchEvent
            {
                t = Now,
                k = WatchEvent.Player,
                s = WatchTargets.PathOf(found),
                d = string.Join(",", new[] { WatchTargets.GameComponents(found), _controller != null ? "CharacterController" : string.Empty }
                    .Where(part => part.Length > 0)),
                p = _player.position
            });
            return true;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Write(new WatchEvent { t = Now, k = WatchEvent.Scene, s = scene.name, d = mode.ToString() });
            _sceneFrameAt = Now + 0.6;
            if (_player == null)
                _nextPlayerSearch = Now + 0.5;
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log)
                return;
            lock (_logQueue)
                _logQueue.Enqueue((type, condition, stackTrace));
        }

        void DrainLogs()
        {
            while (true)
            {
                (LogType Type, string Text, string Stack) log;
                lock (_logQueue)
                {
                    if (_logQueue.Count == 0)
                        return;
                    log = _logQueue.Dequeue();
                }

                var caller = CallerLine(log.Stack);
                // the Editor's own messages are not part of the game under test
                if (caller.StartsWith("UnityEditor.", StringComparison.Ordinal))
                    continue;
                var key = $"{log.Type}: {First(log.Text, 120)}";
                if (_logs.TryGetValue(key, out var seen))
                {
                    _logs[key] = (seen.Id, seen.Count + 1);
                    continue;
                }
                var id = _logIdBase + _logs.Count + 1;
                _logs[key] = (id, 1);
                var warning = log.Type == LogType.Warning;
                if (warning && ++_warnings > MaxWarnings)
                    continue;

                var stack = warning ? string.Empty : caller;
                Write(new WatchEvent
                {
                    t = Now,
                    k = WatchEvent.Log,
                    s = log.Type.ToString(),
                    d = First(log.Text, MaxLogText) + (stack.Length > 0 ? " @ " + stack : string.Empty),
                    n = id,
                    f = warning ? string.Empty : CaptureFrame(Now)
                });
            }
        }

        string CaptureFrame(double now)
        {
            if (now - _lastFrameAt < MinFrameGap)
                return _lastFrame;
            var id = $"f{_frames + 1:0000}";
            if (!WatchCapture.Capture(Path.Combine(_folder, WatchSummary.FramePath(id))))
            {
                _nextFrame = now + FrameInterval;
                return string.Empty;
            }
            _frames++;
            _lastFrameAt = now;
            _lastFrame = id;
            _nextFrame = now + FrameInterval;
            Write(new WatchEvent { t = now, k = WatchEvent.Frame, f = id });
            return id;
        }

        void WriteKey(string key, double downAt, double now) =>
            Write(new WatchEvent { t = downAt, k = WatchEvent.Key, s = key, v = (float)(now - downAt) });

        void Write(WatchEvent e)
        {
            _writer.WriteLine(e.ToJson());
            EventCount++;
        }

        static string First(string text, int max)
        {
            var line = FirstLine(text);
            return line.Length > max ? line.Substring(0, max) + "..." : line;
        }

        // first stack line outside UnityEngine.Debug, i.e. the code that logged
        static string CallerLine(string stack) => stack.Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0 && !line.StartsWith("UnityEngine.Debug", StringComparison.Ordinal)) ?? string.Empty;

        static string FirstLine(string text)
        {
            var end = text.IndexOf('\n');
            return (end >= 0 ? text.Substring(0, end) : text).Trim();
        }

        static void OnTick()
        {
            var current = _current;
            if (current == null || !EditorApplication.isPlaying)
                return;
            try
            {
                current.Tick();
                current._tickErrors = 0;
            }
            catch (Exception e)
            {
                if (current._tickErrors++ == 0)
                    Debug.LogException(e);
                // a recorder that keeps failing ends the session instead of spamming every frame
                if (current._tickErrors == 30)
                {
                    void StopOnce()
                    {
                        EditorApplication.update -= StopOnce;
                        WatchSession.Stop("recorder error: " + e.Message);
                    }
                    EditorApplication.update += StopOnce;
                }
            }
        }

        static void SetHook(bool on)
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (loop.subSystemList == null)
                return;
            for (var i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(PostLateUpdate))
                    continue;
                var systems = (loop.subSystemList[i].subSystemList ?? Array.Empty<PlayerLoopSystem>())
                    .Where(system => system.type != typeof(WatchTick))
                    .ToList();
                if (on)
                    systems.Add(new PlayerLoopSystem { type = typeof(WatchTick), updateDelegate = OnTick });
                loop.subSystemList[i].subSystemList = systems.ToArray();
            }
            PlayerLoop.SetPlayerLoop(loop);
        }
    }
}
