#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Feeder.MCP.Editor.Activity;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace Feeder.MCP.Editor.Device
{
    // 'device action=profile': a Unity Profiler capture recorded on the phone, copied to the PC, loaded into the
    // Profiler window and summarized as frame times and the heaviest main thread markers
    internal static class DeviceProfile
    {
        const string Tool = "device-profiler";
        const int TopMarkers = 15;

        [Serializable] sealed class Args { public string action = "capture"; public int seconds = 5; public int index; }
        [Serializable] sealed class CaptureInfo { public string state = string.Empty; public long bytes; public int chunks; public string error = string.Empty; }

        static int _running;

        public static async Task<string> RunAsync(int seconds, CancellationToken ct)
        {
            if (Interlocked.Exchange(ref _running, 1) == 1)
                throw new InvalidOperationException("a device profile is already running.");
            try
            {
                await DeviceLink.CallAsync(Tool, JsonUtility.ToJson(new Args { action = "capture", seconds = seconds }), ct);
                await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
                CaptureInfo info;
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
                while (true)
                {
                    info = JsonUtility.FromJson<CaptureInfo>(await DeviceLink.CallAsync(Tool, JsonUtility.ToJson(new Args { action = "capture-status" }), ct));
                    if (info.state == "ready")
                        break;
                    if (info.state != "capturing")
                        throw new InvalidOperationException($"the capture {info.state}: {info.error}");
                    if (DateTime.UtcNow > deadline)
                        throw new TimeoutException("the capture did not end.");
                    await Task.Delay(500, ct);
                }

                var folder = Path.Combine(Playtest.PlaytestRunner.ProjectRoot, "Docs", "Playtest_Runs", $"{DateTime.Now:yyyyMMdd-HHmmss}-profile-device");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, "profile.raw");
                using (var file = File.Create(path))
                    for (var i = 0; i < info.chunks; i++)
                    {
                        var bytes = Convert.FromBase64String(await DeviceLink.CallAsync(Tool, JsonUtility.ToJson(new Args { action = "chunk", index = i }), ct));
                        await file.WriteAsync(bytes, 0, bytes.Length, ct);
                    }

                var done = new TaskCompletionSource<string>();
                EditorEvents.NextTick(() =>
                {
                    try { done.SetResult(Analyze(path, info.bytes)); }
                    catch (Exception e) { done.SetException(e); }
                });
                return await done.Task;
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }

        // the Profiler window shows the capture afterwards; its earlier data is replaced
        static string Analyze(string path, long bytes)
        {
            if (!ProfilerDriver.LoadProfile(path, false))
                throw new InvalidOperationException($"the Profiler could not open {path}.");
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            var frameMs = new List<float>();
            var self = new Dictionary<string, (double ms, double gc, long calls)>();
            var children = new List<int>();
            for (var frame = first; frame >= 0 && frame <= last; frame++)
            {
                using (var view = ProfilerDriver.GetHierarchyFrameDataView(frame, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnSelfTime, false))
                {
                    if (!view.valid)
                        continue;
                    frameMs.Add(view.frameTimeMs);
                    var stack = new Stack<int>();
                    stack.Push(view.GetRootItemID());
                    while (stack.Count > 0)
                    {
                        var id = stack.Pop();
                        view.GetItemChildren(id, children);
                        foreach (var child in children)
                            stack.Push(child);
                        if (id == view.GetRootItemID())
                            continue;
                        var name = view.GetItemName(id);
                        self.TryGetValue(name, out var sum);
                        self[name] = (sum.ms + view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime),
                                      sum.gc + view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnGcMemory),
                                      sum.calls + (long)view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls));
                    }
                }
            }
            if (frameMs.Count == 0)
                throw new InvalidOperationException("the capture holds no frames.");

            var sorted = frameMs.OrderBy(v => v).ToArray();
            float P(float p) => sorted[Mathf.Clamp(Mathf.CeilToInt(p * sorted.Length) - 1, 0, sorted.Length - 1)];
            var n = frameMs.Count;
            var report = new StringBuilder();
            report.Append(F($"Device profile: {n} frames, {bytes / 1048576.0:0.0} MB capture, open in the Profiler window now.\n"));
            report.Append(F($"Frame ms: avg {frameMs.Average():0.00}, p50 {P(0.5f):0.00}, p95 {P(0.95f):0.00}, max {sorted[n - 1]:0.00} ({1000f / frameMs.Average():0.0} fps)\n\n"));
            report.Append("| Main thread marker | self ms/frame | calls/frame | GC KB/frame |\n|---|---|---|---|\n");
            foreach (var pair in self.OrderByDescending(p => p.Value.ms).Take(TopMarkers))
                report.Append(F($"| {pair.Key} | {pair.Value.ms / n:0.000} | {pair.Value.calls / (double)n:0.#} | {pair.Value.gc / n / 1024:0.00} |\n"));
            var allocators = self.Where(p => p.Value.gc > 0).OrderByDescending(p => p.Value.gc).Take(5).ToList();
            if (allocators.Count > 0)
                report.Append("\nTop GC allocations: ").Append(string.Join(", ", allocators.Select(p => F($"{p.Key} {p.Value.gc / n / 1024:0.00} KB/frame")))).Append('\n');
            report.Append("\nCapture: ").Append(path.Replace('\\', '/').Replace(Playtest.PlaytestRunner.ProjectRoot.Replace('\\', '/') + "/", string.Empty)).Append('\n');
            return report.ToString();
        }

        static string F(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
    }
}
