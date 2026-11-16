#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Feeder.Json.Nodes;
using Feeder.Reflect.Utils;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;
using UnityEngine;
using UnityEngine.Profiling.Memory.Experimental;
using UnityProfiler = UnityEngine.Profiling.Profiler;
using Object = UnityEngine.Object;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Profiler
    {
        public const string ProfilerMemoryToolId = "profiler-memory";

        internal const string SnapshotFolder = "Library/MatrixMemory";

        [AiTool(ProfilerMemoryToolId, Title = "Profiler / Memory", Enabled = false)]
        [AiSkillDescription("Named memory snapshots: take, list, delete, or compare two. A light snapshot (default) stores the memory counters and the loaded UnityEngine.Object count and size per type as JSON; mode=full also writes a native .snap file for the Memory Profiler package.")]
        [AiSkillBody("# Profiler / Memory\n\n`action=take` stores `Library/MatrixMemory/<name>.json` with the Profiler memory counters plus, per object type (keyed by full type name), how many instances are loaded and their runtime size. Take one before and one after a scene load, a Play session or a suspected leak, then `action=compare name=<earlier> against=<later>`. `mode=full` additionally asks Unity for a native snapshot (`<name>.snap`, written asynchronously a moment later; open it in the Memory Profiler window). `action=list` shows the stored names, `action=delete` removes one. Names are letters, digits, `-`, `_`, `.`.\n\n`compare` prints the counter deltas (`n/a` when a snapshot lacks a counter) and the object types sorted by size change: `type  countA->countB (+n)  sizeA->sizeB MB (+x)`. Leaks show up as types whose count keeps growing between snapshots taken after the same action. `against=current` (default) takes a throw-away snapshot of the live state (nothing stored). `top` limits the rows, `minDeltaKB` hides tiny changes.")]
        [Description("Memory snapshots: action=take (name, mode=light|full) | list | delete (name) | compare (name, against, top, minDeltaKB).")]
        public ResponseCallTool Memory
        (
            [Description("take | list | delete | compare")]
            string action = "take",
            [Description("Snapshot name, e.g. before-load. take: default is a timestamp. compare: the earlier snapshot.")]
            string? name = null,
            [Description("take: light = counters + per-type object table (JSON); full = also a native .snap file.")]
            string mode = "light",
            [Description("compare: later snapshot name, or 'current' for the live state.")]
            string against = "current",
            [Description("compare: max rows of the type table (1-200).")]
            int top = 25,
            [Description("compare: hide types whose size changed less than this many KB and whose count did not change.")]
            float minDeltaKB = 0f
        )
        {
            return MainThread.Instance.Run(() =>
            {
                try
                {
                    switch (action)
                    {
                        case "list":
                        {
                            if (!Directory.Exists(SnapshotFolder)) return ResponseCallTool.Success("no snapshots");
                            var files = new DirectoryInfo(SnapshotFolder).GetFiles("*.json").OrderBy(f => f.LastWriteTime).ToList();
                            return ResponseCallTool.Success(files.Count == 0 ? "no snapshots" : string.Join("\n", files.Select(f =>
                                $"{Path.GetFileNameWithoutExtension(f.Name)} {f.LastWriteTime:yyyy-MM-dd HH:mm:ss}" + (File.Exists(Path.ChangeExtension(f.FullName, ".snap")) ? " +snap" : ""))));
                        }
                        case "delete":
                        {
                            var file = FileOf(CleanName(name, false));
                            if (!File.Exists(file)) return ResponseCallTool.Error($"snapshot '{name}' not found");
                            File.Delete(file);
                            var snap = Path.ChangeExtension(file, ".snap");
                            if (File.Exists(snap)) File.Delete(snap);
                            return ResponseCallTool.Success($"deleted {name}");
                        }
                        case "take":
                        {
                            if (mode != "light" && mode != "full") return ResponseCallTool.Error($"unknown mode '{mode}'. Valid: light|full");
                            var clean = CleanName(name, true);
                            Directory.CreateDirectory(SnapshotFolder);
                            var doc = BuildSnapshot(clean);
                            File.WriteAllText(FileOf(clean), doc.ToJsonString());
                            var text = $"{clean}: types={((JsonObject)doc["types"]!).Count} objects={Num(doc["objects"]):0} objectsMB={Num(doc["objectsMB"]):0.0} allocatedMB={Num(doc["counters"]!["allocatedMB"]):0.0}";
                            if (mode == "full")
                            {
                                var snapPath = Path.ChangeExtension(FileOf(clean), ".snap");
                                Unity.Profiling.Memory.MemoryProfiler.TakeSnapshot(snapPath, (path, ok) =>
                                {
                                    if (!ok) Debug.LogWarning($"[Matrix] memory snapshot '{path}' failed");
                                });
                                text += $"\nnative snapshot requested: {snapPath} (written asynchronously)";
                            }
                            return ResponseCallTool.Success(text);
                        }
                        case "compare":
                        {
                            if (string.IsNullOrWhiteSpace(name)) return ResponseCallTool.Error("name is required");
                            return ResponseCallTool.Success(Diff(name!, Load(name!), against, Load(against), Mathf.Clamp(top, 1, 200), minDeltaKB));
                        }
                        default:
                            return ResponseCallTool.Error($"unknown action '{action}'. Valid: take|list|delete|compare");
                    }
                }
                catch (Exception ex) { return ResponseCallTool.Error(ex.Message); }
            });
        }

        #region Snapshot logic

        static string CleanName(string? name, bool allowDefault)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                if (allowDefault) return DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                throw new ArgumentException("name is required");
            }
            if (name!.Any(c => !(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')) || name.Contains(".."))
                throw new ArgumentException($"name '{name}' may only use letters, digits, '-', '_' and '.'");
            return name;
        }

        static string FileOf(string name) => Path.Combine(SnapshotFolder, name + ".json");

        static JsonObject Load(string name)
        {
            if (name == "current") return BuildSnapshot("current");
            var file = FileOf(CleanName(name, false));
            if (!File.Exists(file)) throw new ArgumentException($"snapshot '{name}' not found");
            return (JsonObject)JsonNode.Parse(File.ReadAllText(file))!;
        }

        internal static JsonObject BuildSnapshot(string name)
        {
            var types = new Dictionary<string, (int Count, long Bytes)>();
            long totalBytes = 0;
            var all = Resources.FindObjectsOfTypeAll<Object>();
            foreach (var obj in all)
            {
                if (obj == null) continue;
                var key = obj.GetType().FullName ?? obj.GetType().Name;
                var size = UnityProfiler.GetRuntimeMemorySizeLong(obj);
                types.TryGetValue(key, out var entry);
                types[key] = (entry.Count + 1, entry.Bytes + size);
                totalBytes += size;
            }
            var typeNode = new JsonObject();
            foreach (var kv in types.OrderByDescending(k => k.Value.Bytes))
                typeNode[kv.Key] = new JsonArray { kv.Value.Count, kv.Value.Bytes };
            return new JsonObject
            {
                ["name"] = name,
                ["time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ["playing"] = Application.isPlaying,
                ["objects"] = all.Length,
                ["objectsMB"] = Math.Round(totalBytes / 1048576.0, 3),
                ["counters"] = new JsonObject
                {
                    ["reservedMB"] = Math.Round(UnityProfiler.GetTotalReservedMemoryLong() / 1048576.0, 3),
                    ["allocatedMB"] = Math.Round(UnityProfiler.GetTotalAllocatedMemoryLong() / 1048576.0, 3),
                    ["monoHeapMB"] = Math.Round(UnityProfiler.GetMonoHeapSizeLong() / 1048576.0, 3),
                    ["monoUsedMB"] = Math.Round(UnityProfiler.GetMonoUsedSizeLong() / 1048576.0, 3),
                    ["graphicsMB"] = Math.Round(UnityProfiler.GetAllocatedMemoryForGraphicsDriver() / 1048576.0, 3),
                },
                ["types"] = typeNode
            };
        }

        internal static string Diff(string nameA, JsonObject a, string nameB, JsonObject b, int top, float minDeltaKB)
        {
            var sb = new StringBuilder();
            sb.Append(nameA).Append(" (").Append((string?)a["time"]).Append(") -> ").Append(nameB).Append(" (").Append((string?)b["time"]).AppendLine(")");
            sb.Append("objects ").Append(Delta(Num(a["objects"]), Num(b["objects"]), "0")).Append("  objectsMB ").AppendLine(Delta(Num(a["objectsMB"]), Num(b["objectsMB"]), "0.0"));
            var ca = (JsonObject)a["counters"]!;
            var cb = (JsonObject)b["counters"]!;
            foreach (var key in ca.Select(k => k.Key).Union(cb.Select(k => k.Key)))
                sb.Append(key).Append(' ').Append(ca[key] == null || cb[key] == null ? "n/a" : Delta(Num(ca[key]), Num(cb[key]), "0.0")).Append("  ");
            sb.AppendLine();

            var ta = (JsonObject)a["types"]!;
            var tb = (JsonObject)b["types"]!;
            var rows = new List<(string Type, int CountA, int CountB, double MbA, double MbB)>();
            foreach (var key in ta.Select(k => k.Key).Union(tb.Select(k => k.Key)))
            {
                var (countA, bytesA) = Read(ta[key]);
                var (countB, bytesB) = Read(tb[key]);
                if (countA == countB && Math.Abs(bytesB - bytesA) / 1024.0 <= minDeltaKB) continue;
                rows.Add((key, countA, countB, bytesA / 1048576.0, bytesB / 1048576.0));
            }
            if (rows.Count == 0)
                return sb.Append("no type changed").ToString();
            sb.AppendLine("type  count  sizeMB");
            foreach (var r in rows.OrderByDescending(r => Math.Abs(r.MbB - r.MbA)).ThenByDescending(r => Math.Abs(r.CountB - r.CountA)).Take(top))
                sb.Append(r.Type).Append("  ").Append(r.CountA).Append("->").Append(r.CountB).Append(" (").Append((r.CountB - r.CountA).ToString("+0;-0;0", CultureInfo.InvariantCulture)).Append(")  ")
                  .Append(r.MbA.ToString("0.00", CultureInfo.InvariantCulture)).Append("->").Append(r.MbB.ToString("0.00", CultureInfo.InvariantCulture)).Append(" (")
                  .Append((r.MbB - r.MbA).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)).AppendLine(")");
            if (rows.Count > top) sb.Append("... ").Append(rows.Count - top).Append(" more changed types");
            return sb.ToString().TrimEnd();
        }

        static double Num(JsonNode? node) => node == null ? 0 : double.Parse(node.ToJsonString(), CultureInfo.InvariantCulture);

        static (int Count, long Bytes) Read(JsonNode? node) =>
            node is JsonArray arr && arr.Count == 2 ? ((int)Num(arr[0]), (long)Num(arr[1])) : (0, 0L);

        static string Delta(double from, double to, string format) =>
            $"{from.ToString(format, CultureInfo.InvariantCulture)}->{to.ToString(format, CultureInfo.InvariantCulture)} ({(to - from).ToString("+" + format + ";-" + format + ";0", CultureInfo.InvariantCulture)})";

        #endregion
    }
}
