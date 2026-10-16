#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Feeder.MCP.Editor.Vfx
{
    // deterministic contact sheet: fixed seed, fixed camera fitted to the union of all sampled frames,
    // one png grid labelled with time, plus a text summary that is often enough on its own.
    public static class VfxPreview
    {
        const int IsolationLayer = 31;
        const int StatSamples = 24;
        const float Fov = 30f;
        const int MaxFramePoints = 20000;
        static readonly Regex GrabPassPattern = new Regex(@"\bGrabPass\s*\{");
        static readonly Dictionary<Shader, bool> GrabShaders = new Dictionary<Shader, bool>();

        public sealed class Options
        {
            public float[]? Times;
            public int Cols = 4;
            public int Frames = 8;
            public int Cell = 192;
            public string View = "front";
            public Color Background = new Color(0.12f, 0.12f, 0.12f);
            public float FrameRadius;
            public Vector3? FrameCenter;
            public float Duration;
            public int Seed = 1;
            public int Budget = 300;
            public bool Image = true;
            public GameObject? Context;
        }

        public sealed class Result
        {
            public byte[]? Png;
            public string Stats = "";
            public float FrameRadius;
            public Vector3 FrameCenter;
        }

        sealed class Sample
        {
            public float Time;
            public int Total;
            public int[] Counts = Array.Empty<int>();
            public float AreaSum;
        }

        #region API

        public static Result Run(GameObject prefab, Options o)
        {
            var inst = Object.Instantiate(prefab);
            inst.name = prefab.name;
            var ctx = o.Context != null ? Object.Instantiate(o.Context) : null;
            if (ctx != null)
            {
                ctx.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                inst.transform.SetParent(ctx.transform, false);
            }
            var top = ctx != null ? ctx : inst;
            GameObject? camGo = null;
            RenderTexture? rt = null;
            var prevActive = RenderTexture.active;
            try
            {
                foreach (var t in top.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.hideFlags = HideFlags.HideAndDontSave;
                    t.gameObject.layer = IsolationLayer;
                }
                if (ctx == null) inst.transform.position = Vector3.zero;
                var systems = inst.GetComponentsInChildren<ParticleSystem>(true);
                for (var i = 0; i < systems.Length; i++)
                {
                    systems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    systems[i].useAutoRandomSeed = false;
                    systems[i].randomSeed = (uint)(o.Seed * 7919 + i);
                }
                var tops = systems.Where(p => !HasSystemAncestor(p, inst.transform)).ToArray();
                var duration = o.Duration > 0f ? o.Duration : EstimateDuration(systems);

                var samples = new List<Sample>();
                var points = new List<Vector4>();
                var maxSize = 0f;
                var particles = new ParticleSystem.Particle[256];
                for (var s = 0; s < StatSamples; s++)
                {
                    var time = duration * (s + 0.5f) / StatSamples;
                    Simulate(tops, time);
                    var sample = new Sample { Time = time, Counts = new int[systems.Length] };
                    for (var i = 0; i < systems.Length; i++)
                    {
                        var ps = systems[i];
                        var count = ps.particleCount;
                        sample.Counts[i] = count;
                        sample.Total += count;
                        if (count == 0 || !ps.GetComponent<ParticleSystemRenderer>().enabled) continue;
                        if (particles.Length < count) particles = new ParticleSystem.Particle[count];
                        var n = ps.GetParticles(particles, count);
                        var local = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
                        for (var k = 0; k < n; k++)
                        {
                            var pos = local ? ps.transform.TransformPoint(particles[k].position) : particles[k].position;
                            var size3 = particles[k].GetCurrentSize3D(ps);
                            var size = Mathf.Max(size3.x, Mathf.Max(size3.y, size3.z)) * ps.transform.lossyScale.x;
                            maxSize = Mathf.Max(maxSize, size);
                            sample.AreaSum += size * size;
                            if (points.Count < MaxFramePoints) points.Add(new Vector4(pos.x, pos.y, pos.z, size));
                        }
                    }
                    samples.Add(sample);
                }

                if (ctx != null)
                    foreach (var r in ctx.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r is ParticleSystemRenderer) continue;
                        var b = r.bounds;
                        for (var c = 0; c < 8; c++)
                        {
                            var corner = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z);
                            for (var k = 0; k < 40; k++) points.Add(new Vector4(corner.x, corner.y, corner.z, 0f));
                        }
                    }
                var center = o.FrameCenter ?? MedianCenter(points);
                var outside = 0f;
                var radius = o.FrameRadius > 0f ? o.FrameRadius : FitRadius(points, center);
                if (points.Count > 0) outside = points.Count(p => Vector3.Distance(p, center) > radius) / (float)points.Count;
                var result = new Result { FrameCenter = center, FrameRadius = radius };
                result.Stats = BuildStats(systems, inst.transform, samples, duration, center, radius, maxSize, outside, o);

                if (!o.Image) return result;

                var lastAlive = samples.LastOrDefault(sm => sm.Total > 0);
                var activeEnd = lastAlive == null ? duration : Mathf.Min(duration, lastAlive.Time + duration / StatSamples);
                var times = o.Times ?? DefaultTimes(activeEnd, o.Frames);
                var cols = Mathf.Clamp(o.Cols, 1, times.Length);
                var rows = Mathf.CeilToInt(times.Length / (float)cols);
                camGo = new GameObject("vfx-preview-camera") { hideFlags = HideFlags.HideAndDontSave };
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = o.Background;
                cam.cullingMask = 1 << IsolationLayer;
                cam.fieldOfView = Fov;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                Place(cam, center, radius, o.View);
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = radius * 20f + 100f;

                rt = new RenderTexture(o.Cell, o.Cell, 24, RenderTextureFormat.ARGB32);
                var sheet = new Texture2D(cols * o.Cell, rows * o.Cell, TextureFormat.RGB24, false);
                try
                {
                    var fill = sheet.GetPixels32();
                    for (var i = 0; i < fill.Length; i++) fill[i] = new Color32(40, 40, 40, 255);
                    sheet.SetPixels32(fill);
                    foreach (var r in top.GetComponentsInChildren<Renderer>(true))
                        if (UsesGrabPass(r)) r.enabled = false;
                    cam.targetTexture = rt;
                    for (var i = 0; i < times.Length; i++)
                    {
                        Simulate(tops, times[i]);
                        cam.Render();
                        RenderTexture.active = rt;
                        var col = i % cols;
                        var row = i / cols;
                        var x = col * o.Cell;
                        var y = (rows - 1 - row) * o.Cell;
                        sheet.ReadPixels(new Rect(0, 0, o.Cell, o.Cell), x, y);
                        Label(sheet, x + 3, y + o.Cell - 3, times[i].ToString("0.00", CultureInfo.InvariantCulture) + "s");
                    }
                    sheet.Apply();
                    result.Png = sheet.EncodeToPNG();
                }
                finally
                {
                    cam.targetTexture = null;
                    Object.DestroyImmediate(sheet);
                }
                return result;
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (rt != null) Object.DestroyImmediate(rt);
                if (camGo != null) Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(top);
            }
        }

        // renders an instance already placed in the open scene through a scene camera, so post-processing
        // (bloom, tonemapping) and the surrounding model match what players see; seeds are left untouched
        // to avoid writing prefab overrides, so frames differ slightly between calls
        public static Result RunInScene(GameObject prefab, Camera cam, Options o)
        {
            var inst = Object.FindObjectsOfType<Transform>(true)
                .Select(t => t.gameObject)
                .FirstOrDefault(g => PrefabUtility.IsAnyPrefabInstanceRoot(g) && PrefabUtility.GetCorrespondingObjectFromSource(g) == prefab)
                ?? throw new ArgumentException($"no instance of '{prefab.name}' in the open scene - place one (e.g. under the model it decorates) to use camera mode");
            var systems = inst.GetComponentsInChildren<ParticleSystem>(true);
            var tops = systems.Where(p => !HasSystemAncestor(p, inst.transform)).ToArray();
            var duration = o.Duration > 0f ? o.Duration : EstimateDuration(systems);
            var times = o.Times ?? DefaultTimes(duration, o.Frames);
            var cols = Mathf.Clamp(o.Cols, 1, times.Length);
            var rows = Mathf.CeilToInt(times.Length / (float)cols);
            var rt = new RenderTexture(o.Cell, o.Cell, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var sheet = new Texture2D(cols * o.Cell, rows * o.Cell, TextureFormat.RGB24, false);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            var counts = new List<string>();
            try
            {
                cam.targetTexture = rt;
                for (var i = 0; i < times.Length; i++)
                {
                    Simulate(tops, times[i]);
                    counts.Add(systems.Sum(p => p.particleCount).ToString(CultureInfo.InvariantCulture));
                    cam.Render();
                    RenderTexture.active = rt;
                    var x = i % cols * o.Cell;
                    var y = (rows - 1 - i / cols) * o.Cell;
                    sheet.ReadPixels(new Rect(0, 0, o.Cell, o.Cell), x, y);
                    Label(sheet, x + 3, y + o.Cell - 3, times[i].ToString("0.00", CultureInfo.InvariantCulture) + "s");
                }
                sheet.Apply();
                return new Result
                {
                    Png = o.Image ? sheet.EncodeToPNG() : null,
                    Stats = $"camera '{cam.name}' instance '{inst.name}' T={VfxCodec.Num(duration)}s | alive " + string.Join(" ", counts)
                };
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                foreach (var ps in tops) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                Object.DestroyImmediate(sheet);
                Object.DestroyImmediate(rt);
            }
        }

        public static float[] DefaultTimes(float duration, int frames)
        {
            frames = Mathf.Clamp(frames, 1, 16);
            if (frames == 1) return new[] { duration * 0.25f };
            var times = new float[frames];
            for (var i = 0; i < frames; i++)
            {
                var u = (i + 0.5f) / frames;
                times[i] = duration * u * u * 0.6f + duration * u * 0.4f;
            }
            return times;
        }

        public static float EstimateDuration(IReadOnlyList<ParticleSystem> systems)
        {
            float emitEnd = 0f, life = 0f;
            var loops = false;
            foreach (var ps in systems)
            {
                var main = ps.main;
                var emits = ps.emission.enabled && ps.GetComponent<ParticleSystemRenderer>().enabled;
                if (!emits && !IsSubEmitterTarget(ps, systems)) continue;
                loops |= main.loop;
                emitEnd = Mathf.Max(emitEnd, Max(main.startDelay) + main.duration);
                life = Mathf.Max(life, Max(main.startLifetime));
            }
            var total = emitEnd + life;
            if (loops) total = Mathf.Min(total, Mathf.Max(2f, emitEnd * 2f));
            return Mathf.Clamp(total, 0.2f, 10f);
        }

        // a built-in GrabPass shader cannot run under a render pipeline: every render logs
        // "_GrabTexture can't be added to this global property sheet", so previews skip those renderers
        public static bool UsesGrabPass(Renderer r) =>
            GraphicsSettings.currentRenderPipeline != null && r.sharedMaterials.Any(m => m != null && m.shader != null && UsesGrabPass(m.shader));

        static bool UsesGrabPass(Shader shader)
        {
            if (GrabShaders.TryGetValue(shader, out var grab)) return grab;
            var path = AssetDatabase.GetAssetPath(shader);
            grab = path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) && File.Exists(path) && GrabPassPattern.IsMatch(File.ReadAllText(path));
            return GrabShaders[shader] = grab;
        }

        #endregion

        #region Logic

        // median center + 95th percentile reach keeps a few far-flung sparks from shrinking the whole effect
        static Vector3 MedianCenter(List<Vector4> points)
        {
            if (points.Count == 0) return Vector3.zero;
            float Median(Func<Vector4, float> axis)
            {
                var values = points.Select(axis).OrderBy(v => v).ToList();
                return values[values.Count / 2];
            }
            return new Vector3(Median(p => p.x), Median(p => p.y), Median(p => p.z));
        }

        static float FitRadius(List<Vector4> points, Vector3 center)
        {
            if (points.Count == 0) return 1f;
            var reach = points.Select(p => Vector3.Distance(p, center) + p.w * 0.5f).OrderBy(d => d).ToList();
            return Mathf.Max(0.1f, reach[Mathf.Min(reach.Count - 1, Mathf.FloorToInt(reach.Count * 0.95f))] * 1.08f);
        }

        static bool IsSubEmitterTarget(ParticleSystem target, IReadOnlyList<ParticleSystem> systems)
        {
            foreach (var ps in systems)
            {
                var sub = ps.subEmitters;
                if (!sub.enabled) continue;
                for (var i = 0; i < sub.subEmittersCount; i++)
                    if (sub.GetSubEmitterSystem(i) == target) return true;
            }
            return false;
        }

        static float Max(ParticleSystem.MinMaxCurve c) => c.mode switch
        {
            ParticleSystemCurveMode.Constant => c.constant,
            ParticleSystemCurveMode.TwoConstants => Mathf.Max(c.constantMin, c.constantMax),
            ParticleSystemCurveMode.Curve => c.curveMultiplier * c.curve.keys.Max(k => k.value),
            _ => c.curveMultiplier * Mathf.Max(c.curveMin.keys.Max(k => k.value), c.curveMax.keys.Max(k => k.value)),
        };

        static bool HasSystemAncestor(ParticleSystem ps, Transform root)
        {
            for (var t = ps.transform.parent; t != null; t = t.parent)
            {
                if (t.GetComponent<ParticleSystem>() != null) return true;
                if (t == root) break;
            }
            return false;
        }

        // manual 60hz stepping: fixedTimeStep=true drops the t=0 burst for times below one fixed step
        static void Simulate(ParticleSystem[] tops, float time)
        {
            const float step = 1f / 60f;
            foreach (var ps in tops)
            {
                ps.Simulate(0f, true, true, false);
                for (var t = 0f; t < time - 1e-5f; t += step)
                    ps.Simulate(Mathf.Min(step, time - t), true, false, false);
            }
        }

        static void Place(Camera cam, Vector3 center, float radius, string view)
        {
            var dist = radius / Mathf.Sin(Fov * 0.5f * Mathf.Deg2Rad);
            Vector3 forward;
            var up = Vector3.up;
            switch (view)
            {
                case "front": forward = Vector3.forward; break;
                case "back": forward = Vector3.back; break;
                case "side": forward = Vector3.left; break;
                case "top": forward = Vector3.down; up = Vector3.forward; break;
                case "iso": forward = new Vector3(0.45f, -0.45f, 0.77f).normalized; break;
                default: throw new ArgumentOutOfRangeException(nameof(view), view, "valid views: front|back|side|top|iso");
            }
            cam.transform.position = center - forward * dist;
            cam.transform.rotation = Quaternion.LookRotation(forward, up);
        }

        static string BuildStats(ParticleSystem[] systems, Transform root, List<Sample> samples, float duration,
            Vector3 center, float radius, float maxSize, float outside, Options o)
        {
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;
            var frameArea = 4f * radius * radius;
            var peak = samples.OrderByDescending(s => s.Total).First();
            var fillPeak = samples.OrderByDescending(s => s.AreaSum).First();
            var last = samples.LastOrDefault(s => s.Total > 0);
            sb.Append("T=").Append(VfxCodec.Num(duration)).Append("s frame=").Append(FrameString(radius, center));
            sb.Append(" | peak ").Append(peak.Total).Append(" @").Append(VfxCodec.Num(peak.Time)).Append('s');
            sb.Append(" | fill ").Append(VfxCodec.Num(fillPeak.AreaSum / frameArea)).Append('x');
            sb.Append(" | ends ").Append(last == null ? "-" : VfxCodec.Num(last.Time) + "s").AppendLine();
            sb.Append("alive ").AppendLine(string.Join(" ", samples.Where((_, i) => i % 2 == 0).Select(s => s.Total.ToString(inv))));

            var warnings = new List<string>();
            var loopers = new List<string>();
            sb.Append("emitters ");
            for (var i = 0; i < systems.Length; i++)
            {
                var ps = systems[i];
                if (ps.transform == root && !ps.emission.enabled) continue;
                var counts = samples.Select(s => s.Counts[i]).ToArray();
                var max = counts.Max();
                var first = Array.FindIndex(counts, c => c > 0);
                var lastIdx = Array.FindLastIndex(counts, c => c > 0);
                sb.Append(ps.name).Append(' ').Append(max);
                if (max > 0) sb.Append('[').Append(VfxCodec.Num(samples[first].Time)).Append('-').Append(VfxCodec.Num(samples[lastIdx].Time)).Append(']');
                sb.Append("; ");
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (max == 0 && renderer.enabled) warnings.Add($"'{ps.name}' never emits in {VfxCodec.Num(duration)}s");
                if (renderer.enabled && renderer.sharedMaterial == null && renderer.renderMode != ParticleSystemRenderMode.None)
                    warnings.Add($"'{ps.name}' has no material");
                if (ps.main.loop) loopers.Add(ps.name);
            }
            sb.AppendLine();
            if (loopers.Count > 0) warnings.Add($"looping ({string.Join(",", loopers)}) - preview covers the first {VfxCodec.Num(duration)}s");
            if (peak.Total > o.Budget) warnings.Add($"peak {peak.Total} particles exceeds budget {o.Budget}");
            if (fillPeak.AreaSum / frameArea > 4f) warnings.Add("heavy overdraw: fill > 4x frame area");
            if (outside > 0.02f) warnings.Add($"{Mathf.RoundToInt(outside * 100f)}% of particle samples fall outside the frame");
            if (maxSize > 0f && maxSize < radius * 0.02f) warnings.Add("particles are tiny relative to the frame");
            if (warnings.Count > 0) sb.Append("warn ").AppendLine(string.Join("; ", warnings.Distinct()));
            return sb.ToString().TrimEnd();
        }

        public static string FrameString(float radius, Vector3 center) =>
            $"{VfxCodec.Num(radius)}@{VfxCodec.Num(center.x)},{VfxCodec.Num(center.y)},{VfxCodec.Num(center.z)}";

        public static void ParseFrame(string s, Options o)
        {
            var at = s.IndexOf('@');
            o.FrameRadius = VfxCodec.ParseFloat(at < 0 ? s : s.Substring(0, at));
            if (at < 0) return;
            var p = s.Substring(at + 1).Split(',');
            if (p.Length != 3) throw new FormatException($"frame '{s}' must be radius or radius@x,y,z");
            o.FrameCenter = new Vector3(VfxCodec.ParseFloat(p[0]), VfxCodec.ParseFloat(p[1]), VfxCodec.ParseFloat(p[2]));
        }

        #endregion

        #region Label

        static readonly Dictionary<char, string> Glyphs = new Dictionary<char, string>
        {
            ['0'] = "111101101101111", ['1'] = "010110010010111", ['2'] = "111001111100111", ['3'] = "111001111001111",
            ['4'] = "101101111001001", ['5'] = "111100111001111", ['6'] = "111100111101111", ['7'] = "111001001001001",
            ['8'] = "111101111101111", ['9'] = "111101111001111", ['.'] = "000000000000010", ['s'] = "000011100001110",
        };

        static void Label(Texture2D tex, int left, int top, string text)
        {
            const int scale = 2;
            var width = text.Length * 4 * scale + scale;
            var height = 5 * scale + 2 * scale;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                SetPixel(tex, left + x, top - y, Color.black);
            for (var c = 0; c < text.Length; c++)
            {
                if (!Glyphs.TryGetValue(text[c], out var g)) continue;
                for (var gy = 0; gy < 5; gy++)
                for (var gx = 0; gx < 3; gx++)
                {
                    if (g[gy * 3 + gx] != '1') continue;
                    for (var sy = 0; sy < scale; sy++)
                    for (var sx = 0; sx < scale; sx++)
                        SetPixel(tex, left + scale + c * 4 * scale + gx * scale + sx, top - scale - gy * scale - sy, Color.white);
                }
            }
        }

        static void SetPixel(Texture2D tex, int x, int y, Color c)
        {
            if (x >= 0 && y >= 0 && x < tex.width && y < tex.height) tex.SetPixel(x, y, c);
        }

        #endregion
    }
}
