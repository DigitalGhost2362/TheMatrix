#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Feeder.MCP.Player;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Scripting;
using UnityProfiler = UnityEngine.Profiling.Profiler;

namespace Feeder.MCP.Device
{
    // what an agent reads from the phone besides a playtest: its console and its performance
    static class DeviceDiagnostics
    {
        const int LogCapacity = 2000;
        const int FrameCapacity = 600;
        // a chunk of the capture file per call; its base64 stays well under the server's message limit
        const int ChunkBytes = 4 * 1024 * 1024;
        const int MaxCaptureSeconds = 30;

        sealed class Entry
        {
            public long Id;
            public string Type = string.Empty;
            public string Message = string.Empty;
            public string StackTrace = string.Empty;
            public float Time;
        }

        static readonly object LogGate = new object();
        static readonly Queue<Entry> Logs = new Queue<Entry>();
        static long _nextLogId;
        static float _time;

        static readonly float[] FrameMs = new float[FrameCapacity];
        static int _frameCount;
        static readonly List<(string label, string unit, ProfilerRecorder recorder)> Recorders = new List<(string, string, ProfilerRecorder)>();

        static string _captureState = "idle";
        static string _captureFile = string.Empty;
        static string _captureError = string.Empty;

        // as early as possible, so the logs of the first scene are kept
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            Application.logMessageReceivedThreaded += OnLog;
            lock (LogGate)
                Logs.Clear();
            _frameCount = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartSampling()
        {
            if (Recorders.Count == 0)
            {
                Add("main thread", "ms", ProfilerCategory.Internal, "Main Thread");
                Add("gc alloc in frame", "KB", ProfilerCategory.Memory, "GC Allocated In Frame");
                Add("draw calls", "", ProfilerCategory.Render, "Draw Calls Count");
                Add("batches", "", ProfilerCategory.Render, "Batches Count");
                Add("setpass calls", "", ProfilerCategory.Render, "SetPass Calls Count");
                Add("triangles", "", ProfilerCategory.Render, "Triangles Count");
                Add("vertices", "", ProfilerCategory.Render, "Vertices Count");
                Add("total used memory", "MB", ProfilerCategory.Memory, "Total Used Memory");
                Add("total reserved memory", "MB", ProfilerCategory.Memory, "Total Reserved Memory");
                Add("gc used memory", "MB", ProfilerCategory.Memory, "GC Used Memory");
                Add("gc reserved memory", "MB", ProfilerCategory.Memory, "GC Reserved Memory");
                Add("system used memory", "MB", ProfilerCategory.Memory, "System Used Memory");
            }
            var go = new GameObject("Matrix Device Diagnostics") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<FrameClock>();
        }

        static void Add(string label, string unit, ProfilerCategory category, string counter)
            => Recorders.Add((label, unit, ProfilerRecorder.StartNew(category, counter, FrameCapacity)));

        static void OnLog(string message, string stackTrace, LogType type)
        {
            lock (LogGate)
            {
                Logs.Enqueue(new Entry { Id = ++_nextLogId, Type = type.ToString(), Message = message, StackTrace = stackTrace, Time = _time });
                while (Logs.Count > LogCapacity)
                    Logs.Dequeue();
            }
        }

        public sealed class LogPage
        {
            public string NextCursor { get; set; } = "0";
            public int Returned { get; set; }
            public long Dropped { get; set; }
            /// <summary>The cursor came from an earlier run of the app (ids start again at 1): read from the oldest kept message.</summary>
            public bool Restarted { get; set; }
            public string[] Entries { get; set; } = Array.Empty<string>();
        }

        [Preserve]
        [MatrixRuntimeTool("device-logs", Title = "Runtime / Device Logs", ReadOnly = true,
            Description = "Console of the running game (the phone of a device build): the last 2000 messages with their type and time since start. " +
                          "Pass nextCursor back as afterCursor to read only newer ones.")]
        static LogPage ReadLogs(
            [Description("Only messages newer than this cursor (the nextCursor of an earlier call).")] string? afterCursor = null,
            [Description("Comma-separated types: Error, Exception, Assert, Warning, Log. Empty = all.")] string? types = null,
            [Description("Regular expression the message must match.")] string? include = null,
            [Description("Most recent entries to return. Default 50.")] int maxEntries = 50,
            [Description("Add stack traces (errors and exceptions always carry the first lines).")] bool includeStackTrace = false)
        {
            long.TryParse(afterCursor, out var after);
            var wanted = string.IsNullOrWhiteSpace(types)
                ? null
                : new HashSet<string>(types!.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0), StringComparer.OrdinalIgnoreCase);
            var pattern = string.IsNullOrEmpty(include) ? null : new Regex(include!, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            Entry[] all;
            long last;
            lock (LogGate)
            {
                all = Logs.ToArray();
                last = _nextLogId;
            }
            var restarted = after > last;
            if (restarted)
                after = 0;
            var first = all.Length > 0 ? all[0].Id : last + 1;
            var picked = all.Where(e => e.Id > after && (wanted == null || wanted.Contains(e.Type)) && (pattern == null || pattern.IsMatch(e.Message)))
                .ToList();
            var take = picked.Skip(Math.Max(0, picked.Count - Math.Max(1, maxEntries))).ToList();
            return new LogPage
            {
                NextCursor = last.ToString(),
                Returned = take.Count,
                // messages after the cursor that fell out of the buffer before this call
                Dropped = Math.Max(0, first - after - 1),
                Restarted = restarted,
                Entries = take.Select(e => Format(e, includeStackTrace)).ToArray(),
            };
        }

        static string Format(Entry e, bool stackTrace)
        {
            var line = $"[{e.Time:0.0}s] {e.Type}: {e.Message}";
            var severe = e.Type is "Error" or "Exception" or "Assert";
            if (e.StackTrace.Length == 0 || !(stackTrace || severe))
                return line;
            var trace = e.StackTrace.TrimEnd();
            if (!stackTrace)
                trace = string.Join("\n", trace.Split('\n').Take(3));
            return line + "\n" + trace;
        }

        public sealed class DeviceStats
        {
            public string Device { get; set; } = string.Empty;
            public string App { get; set; } = string.Empty;
            public bool DevelopmentBuild { get; set; }
            public bool ProfilerSupported { get; set; }
            public string Address { get; set; } = string.Empty;
            public int Frames { get; set; }
            public string Fps { get; set; } = string.Empty;
            public string FrameMs { get; set; } = string.Empty;
            public int Hitches { get; set; }
            public string[] Counters { get; set; } = Array.Empty<string>();
            public string Capture { get; set; } = string.Empty;
        }

        [Preserve]
        [MatrixRuntimeTool("device-profiler", Title = "Runtime / Device Profiler", ReadOnly = true,
            Description = "Performance of the running game (the phone of a device build). action=stats: device, FPS and frame time percentiles, " +
                          "hitches, main thread, GC alloc, draw calls, batches, triangles and memory over the last frames. " +
                          "action=capture records a Unity Profiler capture on the phone (Development Build only); the Editor tool 'device' action=profile " +
                          "drives capture, download and analysis in one call.")]
        static object Profile(
            [Description("stats | capture | capture-status | chunk")] string action = "stats",
            [Description("stats: frames to summarize, up to 600. Default 120.")] int frames = 120,
            [Description("capture: seconds to record, 1-30. Default 5.")] int seconds = 5,
            [Description("chunk: index of the 4 MB piece of the capture file.")] int index = 0)
        {
            switch (action.Trim().ToLowerInvariant())
            {
                case "stats":
                    return Stats(Mathf.Clamp(frames, 1, FrameCapacity));
                case "capture":
                    return StartCapture(Mathf.Clamp(seconds, 1, MaxCaptureSeconds));
                case "capture-status":
                    return CaptureStatus();
                case "chunk":
                    return Chunk(index);
                default:
                    throw new ArgumentException($"Unknown action '{action}'. Use stats, capture, capture-status or chunk.");
            }
        }

        static DeviceStats Stats(int frames)
        {
            var count = Mathf.Min(frames, _frameCount);
            var samples = new float[count];
            for (var i = 0; i < count; i++)
                samples[i] = FrameMs[(_frameCount - 1 - i) % FrameCapacity];
            var sorted = samples.OrderBy(v => v).ToArray();
            var average = count > 0 ? samples.Average() : 0f;
            var counters = new List<string>();
            foreach (var (label, unit, recorder) in Recorders)
            {
                if (!recorder.Valid || recorder.Count == 0)
                {
                    counters.Add($"{label}: n/a{(Debug.isDebugBuild ? string.Empty : " (may need a Development Build)")}");
                    continue;
                }
                var n = Mathf.Min(recorder.Count, count > 0 ? count : recorder.Count);
                double sum = 0, max = 0;
                for (var i = 0; i < n; i++)
                {
                    double v = recorder.GetSample(recorder.Count - 1 - i).Value;
                    sum += v;
                    max = Math.Max(max, v);
                }
                counters.Add(unit switch
                {
                    "ms" => $"{label}: avg {sum / n * 1e-6:0.00} ms, max {max * 1e-6:0.00} ms",
                    "KB" => $"{label}: avg {sum / n / 1024:0.0} KB, max {max / 1024:0.0} KB",
                    "MB" => $"{label}: {recorder.LastValue / 1048576.0:0.0} MB",
                    _ => $"{label}: avg {sum / n:0}, max {max:0}",
                });
            }
            return new DeviceStats
            {
                Device = $"{SystemInfo.deviceModel}, {SystemInfo.operatingSystem}, {SystemInfo.processorType} x{SystemInfo.processorCount}, " +
                         $"{SystemInfo.systemMemorySize} MB RAM, {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), " +
                         $"screen {Screen.width}x{Screen.height} @{Screen.currentResolution.refreshRateRatio.value:0} Hz",
                App = $"{Application.productName} {Application.version}, Unity {Application.unityVersion}, targetFrameRate {Application.targetFrameRate}",
                DevelopmentBuild = Debug.isDebugBuild,
                ProfilerSupported = UnityProfiler.supported,
                Address = LocalAddress(),
                Frames = count,
                Fps = average > 0 ? (1000f / average).ToString("0.0") : "n/a",
                FrameMs = count == 0 ? "n/a" : $"avg {average:0.0}, p50 {Percentile(sorted, 0.5f):0.0}, p95 {Percentile(sorted, 0.95f):0.0}, p99 {Percentile(sorted, 0.99f):0.0}, max {sorted[count - 1]:0.0}",
                // a frame over twice the median shows as a stutter
                Hitches = count == 0 ? 0 : samples.Count(v => v > 2f * Percentile(sorted, 0.5f) && v > 20f),
                Counters = counters.ToArray(),
                Capture = _captureState,
            };
        }

        static float Percentile(float[] sorted, float p)
            => sorted.Length == 0 ? 0f : sorted[Mathf.Clamp(Mathf.CeilToInt(p * sorted.Length) - 1, 0, sorted.Length - 1)];

        // the address the phone uses to reach the PC; the Editor's Profiler connects to it
        static string LocalAddress()
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    var host = MatrixDevice.Host;
                    socket.Connect(host.Length > 0 && IPAddress.TryParse(host, out var ip) ? ip : IPAddress.Parse("8.8.8.8"), 9);
                    return ((IPEndPoint)socket.LocalEndPoint).Address.ToString();
                }
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        static string StartCapture(int seconds)
        {
            if (Application.isEditor)
                throw new InvalidOperationException("capture runs on a device build; in the Editor use the Profiler window.");
            if (!Debug.isDebugBuild || !UnityProfiler.supported)
                throw new InvalidOperationException("capture needs a Development Build: tick Development Build in Matrix Device, build and install again.");
            if (_captureState == "capturing")
                throw new InvalidOperationException("a capture is already running.");
            var basePath = Path.Combine(Application.temporaryCachePath, "matrix-profile");
            foreach (var old in new[] { basePath, basePath + ".raw" })
                if (File.Exists(old))
                    File.Delete(old);
            _captureError = string.Empty;
            _captureFile = string.Empty;
            _captureState = "capturing";
            FrameClock.Instance!.StartCoroutine(Capture(basePath, seconds));
            return $"capturing {seconds} s; poll action=capture-status";
        }

        static IEnumerator Capture(string basePath, int seconds)
        {
            try
            {
                UnityProfiler.logFile = basePath;
                UnityProfiler.enableBinaryLog = true;
                UnityProfiler.enabled = true;
            }
            catch (Exception e)
            {
                _captureError = e.Message;
                _captureState = "failed";
                yield break;
            }
            yield return new WaitForSecondsRealtime(seconds);
            UnityProfiler.enabled = false;
            UnityProfiler.enableBinaryLog = false;
            UnityProfiler.logFile = string.Empty;
            // the file is closed a frame later
            yield return null;
            yield return null;
            _captureFile = File.Exists(basePath + ".raw") ? basePath + ".raw" : File.Exists(basePath) ? basePath : string.Empty;
            _captureState = _captureFile.Length > 0 ? "ready" : "failed";
            if (_captureFile.Length == 0)
                _captureError = "the Profiler wrote no file";
        }

        public sealed class CaptureInfo
        {
            public string State { get; set; } = string.Empty;
            public long Bytes { get; set; }
            public int Chunks { get; set; }
            public string Error { get; set; } = string.Empty;
        }

        static CaptureInfo CaptureStatus()
        {
            var bytes = _captureState == "ready" ? new FileInfo(_captureFile).Length : 0;
            return new CaptureInfo
            {
                State = _captureState,
                Bytes = bytes,
                Chunks = (int)((bytes + ChunkBytes - 1) / ChunkBytes),
                Error = _captureError,
            };
        }

        static string Chunk(int index)
        {
            if (_captureState != "ready")
                throw new InvalidOperationException($"no capture to read (state {_captureState}).");
            using (var file = File.OpenRead(_captureFile))
            {
                var offset = (long)index * ChunkBytes;
                if (index < 0 || offset >= file.Length)
                    throw new ArgumentException($"chunk {index} is past the end of the capture.");
                file.Position = offset;
                var buffer = new byte[(int)Math.Min(ChunkBytes, file.Length - offset)];
                var read = 0;
                while (read < buffer.Length)
                    read += file.Read(buffer, read, buffer.Length - read);
                return Convert.ToBase64String(buffer);
            }
        }

        sealed class FrameClock : MonoBehaviour
        {
            public static FrameClock? Instance;

            void Awake() => Instance = this;

            void Update()
            {
                _time = Time.realtimeSinceStartup;
                FrameMs[_frameCount % FrameCapacity] = Time.unscaledDeltaTime * 1000f;
                _frameCount++;
            }
        }
    }
}
