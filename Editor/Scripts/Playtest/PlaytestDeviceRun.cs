#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Feeder.MCP.Editor.API;
using Feeder.ToolHost.Common.Model;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Playtest
{
    // 'playtest device=true': the flow runs on a linked device build through its 'playtest-device' tool; this side sends
    // the file and the cursor theme, follows the run, then writes the same report folder and contact sheet as a local run
    internal static class PlaytestDeviceRun
    {
        const string DeviceTool = "playtest-device";
        const int PollMs = 500;
        static readonly TimeSpan GiveUp = TimeSpan.FromMinutes(20);

        static CancellationTokenSource? _life;
        static string _progress = string.Empty;

        public static bool Running => _life != null;
        public static string Progress => _progress;

        [Serializable]
        sealed class Args
        {
            public string action = "status";
            public string playtest = string.Empty;
            public string flow = string.Empty;
            public float speed = 1f;
            public bool cursor = true;
            public string folder = string.Empty;
            public string theme = string.Empty;
            public string idle = string.Empty;
            public string click = string.Empty;
            public string name = string.Empty;
        }

        [Serializable] sealed class Status { public string state = string.Empty; public string progress = string.Empty; public bool passed; }
        [Serializable] sealed class Report { public string text = string.Empty; public bool passed; public string[] manifest = Array.Empty<string>(); public string[] shots = Array.Empty<string>(); }

        // true when the result comes later through requestId
        public static bool Start(PlaytestFile playtest, string flow, float speed, bool? cursor, string? requestId, out string message)
        {
            if (_life != null)
                throw new InvalidOperationException($"A device playtest is already running ({_progress}). Call action=stop first.");
            playtest.Expand(flow);
            var theme = PlaytestCursorTheme.Load(playtest.Get("cursor-theme", string.Empty));
            var folder = Path.Combine(Path.GetDirectoryName(playtest.Path)!, "Playtest_Runs", $"{DateTime.Now:yyyyMMdd-HHmmss}-{flow}-device");
            var args = new Args
            {
                action = "start",
                playtest = File.ReadAllText(playtest.Path),
                flow = flow,
                speed = speed > 0 ? speed : playtest.GetFloat("speed", 1f),
                cursor = cursor ?? playtest.GetBool("cursor", true),
                folder = Relative(folder),
                theme = JsonUtility.ToJson(theme),
                idle = Png(theme.idle),
                click = Png(theme.clickSheet),
            };
            _life = new CancellationTokenSource();
            _progress = $"Playtest '{flow}' starting on the device.";
            _ = RunAsync(args, folder, requestId ?? string.Empty, _life.Token);
            message = $"Playtest '{flow}' sent to the device; the report follows when it ends.";
            return !string.IsNullOrEmpty(requestId);
        }

        public static string Stop()
        {
            if (_life == null)
                return "No device playtest is running.";
            _ = CallAsync(new Args { action = "stop" }, CancellationToken.None);
            return "Stop sent to the device; the report follows.";
        }

        static async Task RunAsync(Args start, string folder, string requestId, CancellationToken ct)
        {
            var report = string.Empty;
            var passed = false;
            try
            {
                _progress = await CallAsync(start, ct);
                var deadline = DateTime.UtcNow + GiveUp;
                while (true)
                {
                    await Task.Delay(PollMs, ct);
                    var status = JsonUtility.FromJson<Status>(await CallAsync(new Args(), ct));
                    _progress = status.progress;
                    if (status.state == "done")
                        break;
                    if (status.state == "idle")
                        throw new InvalidOperationException("the device app restarted during the run.");
                    if (DateTime.UtcNow > deadline)
                        throw new TimeoutException($"no end after {GiveUp.TotalMinutes:0} minutes.");
                }
                var result = JsonUtility.FromJson<Report>(await CallAsync(new Args { action = "report" }, ct));
                Directory.CreateDirectory(folder);
                foreach (var shot in result.shots)
                {
                    var png = Convert.FromBase64String(await CallAsync(new Args { action = "shot", name = shot }, ct));
                    var path = Path.Combine(folder, shot);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, png);
                }
                File.WriteAllLines(Path.Combine(folder, PlaytestCapture.ManifestName), result.manifest);
                report = Finish(result.text, folder);
                passed = result.passed;
            }
            catch (Exception e)
            {
                report = $"Playtest on the device failed: {(e is OperationCanceledException ? "cancelled" : e.Message)}\n" +
                         "Check that the app runs on the phone and Matrix Device shows the player as linked.";
            }
            finally
            {
                _life?.Dispose();
                _life = null;
                _progress = string.Empty;
            }
            PlaytestRunner.DeliverDevice(report, passed, requestId);
        }

        static string Finish(string text, string folder)
        {
            var report = new StringBuilder(text);
            report.Append("\n## Side effects\n\nNot checked: the run was on a device (protect-prefs works in the Editor only).\n");
            var title = text.Split('\n')[0].TrimEnd('.') + $" (device, {DateTime.Now:yyyy-MM-dd HH:mm})";
            try
            {
                var sheets = PlaytestCapture.BuildSheets(folder, title);
                report.Append(sheets.Count == 0 ? "\nContact sheet: none (no shots in this flow).\n" : "\n");
                foreach (var sheet in sheets)
                    report.Append($"Contact sheet: {Relative(sheet)}\n![]({sheet.Replace('\\', '/')})\n");
            }
            catch (Exception e)
            {
                report.Append($"\nContact sheet failed: {e.Message}\n");
            }
            var path = Path.Combine(folder, "report.md");
            File.WriteAllText(path, report.ToString());
            report.Append($"Report: {Relative(path)}\n");
            return report.ToString();
        }

        static Task<string> CallAsync(Args args, CancellationToken ct) => Device.DeviceLink.CallAsync(DeviceTool, JsonUtility.ToJson(args), ct);

        static string Png(Texture2D? texture)
        {
            var path = texture == null ? string.Empty : AssetDatabase.GetAssetPath(texture);
            return path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(path)
                ? Convert.ToBase64String(File.ReadAllBytes(path))
                : string.Empty;
        }

        static string Relative(string path) => path.Replace('\\', '/').Replace(PlaytestRunner.ProjectRoot.Replace('\\', '/') + "/", string.Empty);
    }
}
