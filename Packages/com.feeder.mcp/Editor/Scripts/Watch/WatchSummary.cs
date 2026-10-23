#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Feeder.MCP.Editor.Watch
{
    // turns events.jsonl into summary.md: one line per meaningful action, movement folded into segments,
    // stuck / airborne flags, grounded safe points and the frames picked for contact sheets
    internal static class WatchSummary
    {
        public const string EventsFile = "events.jsonl";
        public const string SummaryFile = "summary.md";

        const float MoveSpeed = 0.5f;
        const double RestToSplit = 1.0;
        const float TeleportJump = 15f;
        const float StallDistance = 0.5f;
        const double StallTime = 2.0;
        const double AirborneTime = 1.0;
        const float FallDrop = 5f;
        const float SafePointSpacing = 10f;
        const int MaxSafePoints = 40;
        const double MaxSheetGap = 6.0;
        const double TapMerge = 1.5;

        public static string FramePath(string id) => $"frames/{id}.jpg";

        public static string Write(string folder)
        {
            var events = Read(Path.Combine(folder, EventsFile));
            var sheets = new List<(string Sheet, List<string> Frames)>();
            try
            {
                sheets = WatchCapture.BuildSheets(folder, SheetFrames(events).Select(FramePath).ToList());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Matrix Watch] contact sheets not written: {e.Message}");
            }
            var traces = Directory.GetFiles(folder, "input*.inputtrace").Select(Path.GetFileName)
                .OrderBy(file => file.Length).ThenBy(file => file, StringComparer.Ordinal).ToList();
            var text = Build(Path.GetFileName(folder), events, sheets, traces);
            File.WriteAllText(Path.Combine(folder, SummaryFile), text, new UTF8Encoding(false));
            return text;
        }

        static List<WatchEvent> Read(string path)
        {
            var events = new List<WatchEvent>();
            if (!File.Exists(path))
                return events;
            foreach (var line in File.ReadLines(path))
            {
                if (line.Length == 0)
                    continue;
                try
                {
                    events.Add(JsonUtility.FromJson<WatchEvent>(line));
                }
                catch (ArgumentException)
                {
                    // a line cut by a crash; the rest of the file still reads
                }
            }
            return events.Select((e, i) => (e, i)).OrderBy(x => x.e.t).ThenBy(x => x.i).Select(x => x.e).ToList();
        }

        static string Build(string name, List<WatchEvent> events, List<(string Sheet, List<string> Frames)> sheets, List<string> traces)
        {
            var play = events.FirstOrDefault(e => e.k == WatchEvent.Play);
            var end = events.LastOrDefault(e => e.k == WatchEvent.End);
            var player = events.FirstOrDefault(e => e.k == WatchEvent.Player);
            var samples = events.Where(e => e.k == WatchEvent.Pos).ToList();
            var frameCount = events.Count(e => e.k == WatchEvent.Frame);
            var duration = events.Count > 0 ? events[events.Count - 1].t : 0;
            var meta = (play?.d ?? string.Empty).Split('|');

            var sb = new StringBuilder();
            sb.Append("# Watch ").AppendLine(name);
            sb.AppendLine();
            sb.Append("scene ").Append(play?.s ?? "?")
              .Append(" | ").Append(meta.Length > 0 ? meta[0] : "?")
              .Append(" | input ").Append(meta.Length > 1 ? meta[1] : "?")
              .Append(" | player ").Append(player != null ? $"{player.s} ({player.d})" : "not found")
              .Append(" | ").Append(Clock(duration))
              .Append(" | ").Append(events.Count).Append(" events, ").Append(frameCount).AppendLine(" frames");
            sb.Append("ended by ").AppendLine(end?.s ?? "a crash or reload (no end event)");
            sb.AppendLine();
            sb.AppendLine("Read this file first. Frames: `frames/<id>.jpg` (640 px). Sheets: `sheets/sheet-NN.jpg`, 3x3, left to right, top to bottom.");
            sb.AppendLine("Raw: `events.jsonl` (grep a time window, never read it whole)" +
                          (traces.Count > 0 ? $", Input System trace {string.Join(", ", traces.Select(file => $"`{file}`"))} (in order)." : "."));
            sb.AppendLine("Click lines: `path \"label\" -> persistent listeners [game components] @screen(x,y 0-1)`. Listeners added in code are not visible; read the components' scripts.");
            sb.AppendLine();

            sb.AppendLine("## Timeline");
            sb.AppendLine();
            sb.AppendLine("```");
            foreach (var line in Timeline(events, samples))
                sb.AppendLine(line);
            sb.AppendLine("```");
            sb.AppendLine();

            var flags = Flags(samples);
            sb.AppendLine("## Flags");
            sb.AppendLine();
            if (flags.Count == 0)
                sb.AppendLine(samples.Count == 0 ? "- no player samples (pass `player=<path>` to `watch start`)" : "- none");
            foreach (var flag in flags)
                sb.Append("- ").AppendLine(flag);
            sb.AppendLine();

            var safe = SafePoints(samples);
            if (safe.Count > 0)
            {
                sb.AppendLine("## Safe points (grounded, >= 10 m apart, for teleports)");
                sb.AppendLine();
                sb.AppendLine(string.Join(" ", safe.Select(Vec)));
                sb.AppendLine();
            }

            if (sheets.Count > 0)
            {
                sb.AppendLine("## Sheets");
                sb.AppendLine();
                foreach (var (sheet, cells) in sheets)
                {
                    var ids = cells.Select(Path.GetFileNameWithoutExtension);
                    sb.Append("- `").Append(sheet).Append("`: ").AppendLine(string.Join(" ", ids.Select(id => $"{id}@{Clock(FrameTime(events, id))}")));
                }
            }
            return sb.ToString();
        }

        #region Timeline

        static IEnumerable<string> Timeline(List<WatchEvent> events, List<WatchEvent> samples)
        {
            var hideMoveKeys = samples.Count > 0;
            var lines = new List<(double T, string Text)>();

            foreach (var segment in Segments(samples))
                lines.Add((segment.T, segment.Text(events)));

            string? lastKey = null;
            var lastKeyIndex = -1;
            var lastKeyAt = double.NegativeInfinity;
            var taps = 1;
            foreach (var e in events)
            {
                switch (e.k)
                {
                    case WatchEvent.Play:
                        lines.Add((e.t, $"play scene {e.s} ({e.d.Replace('|', ' ')})"));
                        break;
                    case WatchEvent.Player:
                        lines.Add((e.t, $"player {e.s} [{e.d}] at {Vec(e.p)}"));
                        break;
                    case WatchEvent.Scene:
                        lines.Add((e.t, $"scene {e.s} ({e.d})"));
                        break;
                    case WatchEvent.Click:
                    case WatchEvent.Drag:
                        lines.Add((e.t, Pointer(e)));
                        break;
                    case WatchEvent.Key:
                        if (hideMoveKeys && WatchInput.IsMoveKey(e.s))
                            break;
                        if (e.s == lastKey && e.t - lastKeyAt < TapMerge && e.v < 0.5f && lastKeyIndex >= 0)
                        {
                            taps++;
                            lines[lastKeyIndex] = (lines[lastKeyIndex].T, $"key {e.s} x{taps}");
                        }
                        else
                        {
                            taps = 1;
                            lines.Add((e.t, $"key {e.s} ({e.v:0.0}s)"));
                            lastKeyIndex = lines.Count - 1;
                        }
                        lastKey = e.s;
                        lastKeyAt = e.t;
                        break;
                    case WatchEvent.Marker:
                        lines.Add((e.t, $"MARKER #{e.n} {e.f}"));
                        break;
                    case WatchEvent.Log:
                        var repeats = events.FirstOrDefault(x => x.k == WatchEvent.LogCount && x.n == e.n);
                        lines.Add((e.t, $"{e.s.ToLowerInvariant()} {e.d}{(repeats != null ? $" (x{(int)repeats.v})" : string.Empty)} {e.f}".TrimEnd()));
                        break;
                    case WatchEvent.TimeScale:
                        lines.Add((e.t, $"timeScale {e.v:0.##}"));
                        break;
                    case WatchEvent.End:
                        lines.Add((e.t, $"end ({e.s})"));
                        break;
                }
            }
            return lines.Select((line, i) => (line, i)).OrderBy(x => x.line.T).ThenBy(x => x.i)
                .Select(x => $"{Clock(x.line.T)}  {x.line.Text}");
        }

        static string Pointer(WatchEvent e)
        {
            var parts = e.d.Split('|');
            var kind = parts.Length > 0 ? parts[0] : string.Empty;
            var label = parts.Length > 1 && parts[1].Length > 0 ? $" \"{parts[1]}\"" : string.Empty;
            var listeners = parts.Length > 2 && parts[2].Length > 0 ? $" -> {parts[2]}" : string.Empty;
            var components = parts.Length > 3 && parts[3].Length > 0 ? $" [{parts[3]}]" : string.Empty;
            var target = kind == "none" || e.s.Length == 0
                ? "(nothing hit)"
                : $"{(kind == "ui" ? "ui" : kind)} {e.s}{label}{listeners}{components}{(kind != "ui" ? $" point {Vec(e.p)}" : string.Empty)}";
            var where = e.k == WatchEvent.Drag ? $"{ScreenPos(e.a)}->{ScreenPos(e.b)} {e.v:0.0}s" : ScreenPos(e.a);
            return $"{e.k} {target} @{where} {e.f}".TrimEnd();
        }

        sealed class Segment
        {
            public double T;
            public double EndT;
            public Vector3 From;
            public Vector3 To;
            public float Length;
            public int Airborne;
            public int Samples;
            public bool Jump;

            public string Text(List<WatchEvent> events)
            {
                if (Jump)
                    return $"jump {Vec(From)} -> {Vec(To)} {Length:0}m (teleport or respawn?)";
                var keys = events.Where(e => e.k == WatchEvent.Key && WatchInput.IsMoveKey(e.s) && e.t < EndT && e.t + e.v > T)
                    .Select(e => e.s).Distinct().ToList();
                var frames = events.Where(e => e.k == WatchEvent.Frame && e.t >= T && e.t <= EndT).Select(e => e.f).ToList();
                var air = Samples > 0 ? Airborne * 100 / Samples : 0;
                return $"move {Vec(From)} -> {Vec(To)} {Length:0.#}m {EndT - T:0.0}s" +
                       (air > 0 ? $" air {air}%" : string.Empty) +
                       (keys.Count > 0 ? $" keys {string.Join(",", keys)}" : string.Empty) +
                       (frames.Count > 0 ? $" {frames[0]}" + (frames.Count > 1 ? $"..{frames[frames.Count - 1]}" : string.Empty) : string.Empty);
            }
        }

        static List<Segment> Segments(List<WatchEvent> samples)
        {
            var segments = new List<Segment>();
            Segment? current = null;
            double restSince = -1;
            for (var i = 1; i < samples.Count; i++)
            {
                var previous = samples[i - 1];
                var sample = samples[i];
                var step = Vector3.Distance(previous.p, sample.p);
                if (step > TeleportJump)
                {
                    current = null;
                    segments.Add(new Segment { T = sample.t, EndT = sample.t, From = previous.p, To = sample.p, Length = step, Jump = true });
                    continue;
                }

                if (sample.v >= MoveSpeed)
                {
                    restSince = -1;
                    if (current == null)
                    {
                        current = new Segment { T = previous.t, From = previous.p };
                        segments.Add(current);
                    }
                }
                else if (current != null)
                {
                    if (restSince < 0)
                        restSince = sample.t;
                    if (sample.t - restSince >= RestToSplit)
                    {
                        current = null;
                        continue;
                    }
                }

                if (current == null)
                    continue;
                current.Length += step;
                if (sample.v >= MoveSpeed)
                {
                    current.EndT = sample.t;
                    current.To = sample.p;
                }
                current.Samples++;
                if ((sample.n & WatchEvent.FlagGrounded) == 0)
                    current.Airborne++;
            }
            segments.RemoveAll(segment => !segment.Jump && segment.Length < 1f);
            return segments;
        }

        #endregion

        #region Flags

        static List<string> Flags(List<WatchEvent> samples)
        {
            var flags = new List<(double T, string Text)>();
            for (var i = 1; i < samples.Count; i++)
                if (Vector3.Distance(samples[i - 1].p, samples[i].p) > TeleportJump)
                    flags.Add((samples[i].t, $"{Clock(samples[i].t)} position jumped {Vector3.Distance(samples[i - 1].p, samples[i].p):0}m {Vec(samples[i - 1].p)} -> {Vec(samples[i].p)} (teleport or respawn?)"));

            // stuck: move input held while the player stays inside a small circle
            WatchEvent? anchor = null;
            WatchEvent? last = null;
            var stalled = false;
            foreach (var sample in samples)
            {
                var input = (sample.n & WatchEvent.FlagMoveInput) != 0;
                // a gap in samples means the editor was paused: start a new window
                if (last != null && sample.t - last.t > 1.0)
                {
                    if (stalled && anchor != null)
                        flags.Add((anchor.t, $"{Clock(anchor.t)} STUCK {last.t - anchor.t:0.0}s at {Vec(anchor.p)} with move input held"));
                    stalled = false;
                    anchor = null;
                }
                if (anchor != null && input && Vector3.Distance(anchor.p, sample.p) < StallDistance)
                {
                    if (sample.t - anchor.t >= StallTime)
                        stalled = true;
                }
                else
                {
                    if (stalled && anchor != null && last != null)
                        flags.Add((anchor.t, $"{Clock(anchor.t)} STUCK {last.t - anchor.t:0.0}s at {Vec(anchor.p)} with move input held"));
                    stalled = false;
                    anchor = input ? sample : null;
                }
                last = sample;
            }
            if (stalled && anchor != null && last != null)
                flags.Add((anchor.t, $"{Clock(anchor.t)} STUCK {last.t - anchor.t:0.0}s at {Vec(anchor.p)} until the end"));

            // airborne / fall; skipped when the ground check never works (2D, custom physics)
            var grounded = samples.Count(sample => (sample.n & WatchEvent.FlagGrounded) != 0);
            if (samples.Count > 0 && grounded * 10 >= samples.Count)
            {
                WatchEvent? takeoff = null;
                WatchEvent? lastGround = null;
                for (var i = 0; i < samples.Count; i++)
                {
                    var sample = samples[i];
                    var onGround = (sample.n & WatchEvent.FlagGrounded) != 0;
                    if (!onGround && takeoff == null)
                        takeoff = lastGround ?? sample;
                    if ((onGround || i == samples.Count - 1) && takeoff != null)
                    {
                        var time = sample.t - takeoff.t;
                        var drop = takeoff.p.y - sample.p.y;
                        if (time >= AirborneTime)
                            flags.Add((takeoff.t, $"{Clock(takeoff.t)} {(drop >= FallDrop ? "FELL" : "airborne")} {time:0.0}s, dropped {drop:0.#}m {Vec(takeoff.p)} -> {Vec(sample.p)}{(onGround ? string.Empty : " (never landed)")}"));
                        takeoff = null;
                    }
                    if (onGround)
                        lastGround = sample;
                }
            }
            return flags.OrderBy(flag => flag.T).Select(flag => flag.Text).ToList();
        }

        static List<Vector3> SafePoints(List<WatchEvent> samples)
        {
            var points = new List<Vector3>();
            foreach (var sample in samples)
            {
                if ((sample.n & WatchEvent.FlagGrounded) == 0)
                    continue;
                if (points.Count > 0 && Vector3.Distance(points[points.Count - 1], sample.p) < SafePointSpacing)
                    continue;
                points.Add(sample.p);
                if (points.Count >= MaxSafePoints)
                    break;
            }
            return points;
        }

        #endregion

        #region Frames

        // every frame an action points at, plus periodic frames so no gap is longer than MaxSheetGap
        static List<string> SheetFrames(List<WatchEvent> events)
        {
            var frames = events.Where(e => e.k == WatchEvent.Frame).ToList();
            var cited = new HashSet<string>(events.Where(e => e.k != WatchEvent.Frame && e.f.Length > 0).Select(e => e.f));
            var picked = new List<string>();
            var lastAt = double.NegativeInfinity;
            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                var isLast = i == frames.Count - 1;
                if (cited.Contains(frame.f) || frame.t - lastAt >= MaxSheetGap || isLast || picked.Count == 0)
                {
                    picked.Add(frame.f);
                    lastAt = frame.t;
                }
            }
            return picked;
        }

        static double FrameTime(List<WatchEvent> events, string id) =>
            events.FirstOrDefault(e => e.k == WatchEvent.Frame && e.f == id)?.t ?? 0;

        #endregion

        static string Clock(double seconds)
        {
            if (seconds < 0)
                seconds = 0;
            var minutes = (int)(seconds / 60);
            return $"{minutes:00}:{(seconds - minutes * 60).ToString("00.0", CultureInfo.InvariantCulture)}";
        }

        static string Vec(Vector3 v) => $"({v.x.ToString("0.#", CultureInfo.InvariantCulture)},{v.y.ToString("0.#", CultureInfo.InvariantCulture)},{v.z.ToString("0.#", CultureInfo.InvariantCulture)})";

        static string ScreenPos(Vector2 v) => $"({v.x.ToString("0.00", CultureInfo.InvariantCulture)},{v.y.ToString("0.00", CultureInfo.InvariantCulture)})";
    }
}
