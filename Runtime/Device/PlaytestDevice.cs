#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Feeder.MCP.Editor.Playtest;
using Feeder.MCP.Player;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Scripting;

namespace Feeder.MCP.Device
{
    // the device half of 'playtest device=true': the Editor sends the Playtest.md text and the cursor theme, the
    // flow runs here with the same PlaytestFlow as in the Editor, and the Editor reads back the report and the shots
    static class PlaytestDevice
    {
        const string LogTag = "[Matrix Playtest]";
        // a full shot is sent over Wi-Fi; its long edge is capped
        const int MaxShotEdge = 1600;
        // invented default: a Game view's short side is around 900 points
        const float ShortSidePoints = 900f;

        static DeviceHost? _host;
        static PlaytestFlow? _flow;
        static PlaytestRunner? _runner;
        static string _report = string.Empty;
        static bool _passed;

        public sealed class Status
        {
            public string State { get; set; } = "idle";
            public string Progress { get; set; } = string.Empty;
            public bool Passed { get; set; }
        }

        public sealed class Report
        {
            public string Text { get; set; } = string.Empty;
            public bool Passed { get; set; }
            public string[] Manifest { get; set; } = Array.Empty<string>();
            public string[] Shots { get; set; } = Array.Empty<string>();
        }

        [Preserve]
        [MatrixRuntimeTool("playtest-device", Title = "Runtime / Playtest Device", ReadOnly = true,
            Description = "Device half of 'playtest' with device=true; the Editor drives it. Call 'playtest' instead.")]
        static object Run(
            [Description("start | status | report | shot | stop")] string action = "status",
            [Description("start: the Playtest.md text.")] string? playtest = null,
            [Description("start: the flow name.")] string? flow = null,
            [Description("start: speed multiplier.")] float speed = 1f,
            [Description("start: draw the cursor.")] bool cursor = true,
            [Description("start: how the report names the run folder.")] string? folder = null,
            [Description("start: PlaytestCursorTheme as JsonUtility JSON.")] string? theme = null,
            [Description("start: idle cursor PNG, base64.")] string? idle = null,
            [Description("start: click sheet PNG, base64.")] string? click = null,
            [Description("shot: file name from the report's shot list.")] string? name = null)
        {
            switch (action.Trim().ToLowerInvariant())
            {
                case "start":
                    return Start(playtest ?? string.Empty, flow ?? string.Empty, speed, cursor, folder ?? string.Empty, theme, idle, click);
                case "status":
                    return new Status
                    {
                        // done once the report is written, which follows the last step
                        State = _flow == null ? "idle" : _report.Length > 0 ? "done" : "running",
                        Progress = _flow == null ? string.Empty : _report.Length > 0 ? _report.Split('\n')[0] : _flow.Progress(),
                        Passed = _passed,
                    };
                case "report":
                    return new Report
                    {
                        Text = _report,
                        Passed = _passed,
                        Manifest = _host?.Tiles.Select(t => t.Serialize()).ToArray() ?? Array.Empty<string>(),
                        Shots = _host?.Files.Keys.ToArray() ?? Array.Empty<string>(),
                    };
                case "shot":
                    if (_host == null || name == null || !_host.Files.TryGetValue(name, out var png))
                        throw new ArgumentException($"no shot '{name}' in the last run");
                    return Convert.ToBase64String(png);
                case "stop":
                    if (_flow != null && !_flow.Done)
                        _flow.Fail("stopped by action=stop");
                    return "stopped";
                default:
                    throw new ArgumentException($"Unknown action '{action}'. Use start, status, report, shot or stop.");
            }
        }

        static string Start(string text, string flowName, float speed, bool cursor, string folder, string? themeJson, string? idle, string? click)
        {
            if (_flow != null && !_flow.Done)
                throw new InvalidOperationException($"A playtest is already running on this device: {_flow.Progress()}");
            var file = PlaytestFile.Parse(text.Replace("\r\n", "\n").Split('\n'), "Playtest.md");
            if (file.Errors.Count > 0)
                throw new FormatException(file.Describe());
            var steps = file.Expand(flowName);

            var theme = ScriptableObject.CreateInstance<PlaytestCursorTheme>();
            if (!string.IsNullOrEmpty(themeJson))
                JsonUtility.FromJsonOverwrite(themeJson, theme);
            theme.idle = Texture(idle);
            theme.clickSheet = Texture(click);

            _report = string.Empty;
            _passed = false;
            _host = new DeviceHost(theme, folder);
            _flow = new PlaytestFlow(_host, new PlaytestFlowOptions { Flow = flowName, Speed = speed > 0 ? speed : 1f, Cursor = cursor }, file, steps);
            if (_runner == null)
            {
                var go = new GameObject("Matrix Playtest") { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.Object.DontDestroyOnLoad(go);
                _runner = go.AddComponent<PlaytestRunner>();
            }
            _runner.Play(_flow);
            return $"Playtest '{flowName}' started on the device ({steps.Count} steps).";
        }

        static Texture2D? Texture(string? base64)
        {
            if (string.IsNullOrEmpty(base64))
                return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            texture.LoadImage(Convert.FromBase64String(base64));
            return texture;
        }

        static void Finished(PlaytestFlow flow)
        {
            flow.Close();
            _passed = flow.Passed;
        }

        // steps run at the end of the frame, after OnGUI drew the cursor, so a capture is the frame the phone shows
        sealed class PlaytestRunner : MonoBehaviour
        {
            static readonly WaitForEndOfFrame EndOfFrame = new WaitForEndOfFrame();
            PlaytestFlow? _flow;

            public void Play(PlaytestFlow flow)
            {
                _flow = flow;
                StopAllCoroutines();
                StartCoroutine(Loop(flow));
            }

            void OnEnable() => Application.logMessageReceived += OnLog;
            void OnDisable() => Application.logMessageReceived -= OnLog;

            void OnLog(string message, string stackTrace, LogType type)
            {
                if (type is LogType.Error or LogType.Exception or LogType.Assert && !message.StartsWith(LogTag, StringComparison.Ordinal)
                    && _flow != null && !_flow.Done)
                    _flow.AddConsoleError(message);
            }

            IEnumerator Loop(PlaytestFlow flow)
            {
                while (!flow.Done)
                {
                    yield return EndOfFrame;
                    try { flow.Step(); }
                    catch (Exception e)
                    {
                        flow.Fail($"runner error: {e.GetType().Name}: {e.Message}");
                        Debug.LogError($"{LogTag} runner error: {e}");
                    }
                }
                Finished(flow);
                // an OnGUI left alive costs the game an IMGUI pass and garbage every frame
                _runner = null;
                Destroy(gameObject);
            }

            void OnGUI()
            {
                if (_host != null && _host.CursorVisible)
                    _host.Draw();
            }
        }

        // the phone screen: the cursor drawn with IMGUI on top of the game, shots read from the screen and kept in memory
        sealed class DeviceHost : IPlaytestHost
        {
            readonly PlaytestCursorTheme _theme;
            readonly string _folder;
            GUIStyle? _caption;
            GUIStyle? _blocked;

            public DeviceHost(PlaytestCursorTheme theme, string folder)
            {
                _theme = theme;
                _folder = folder;
            }

            public PlaytestCursorState Cursor { get; } = new PlaytestCursorState();
            public bool CursorVisible { get; private set; }
            public Dictionary<string, byte[]> Files { get; } = new Dictionary<string, byte[]>();
            public List<PlaytestTile> Tiles { get; } = new List<PlaytestTile>();

            public PlaytestCursorTheme ShowCursor(bool show, string themePath, string onlyLayers)
            {
                CursorVisible = show;
                return _theme;
            }

            public void RefreshCursor() { }

            // theme sizes are points; the short side of the screen counts ShortSidePoints, so the cursor covers the
            // same share of a phone as of a typical Game view
            public float PointsPerScreenPixel() => ShortSidePoints / Mathf.Max(1, Mathf.Min(Screen.width, Screen.height));

            public void Begin(PlaytestFile file) { }
            public void End() { }

            public Color32[]? Capture(bool withCursor, out int width, out int height, out string error)
            {
                error = string.Empty;
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                try
                {
                    width = texture.width;
                    height = texture.height;
                    return texture.GetPixels32();
                }
                finally
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }

            public string SaveShot(string name, Color32[] pixels, int width, int height, bool tile)
            {
                var edge = tile ? PlaytestPixels.TileWidth : Mathf.Min(width, MaxShotEdge * width / Mathf.Max(width, height));
                if (edge < width)
                    pixels = PlaytestPixels.Shrink(pixels, width, height, edge, out height);
                else
                    edge = width;
                Files[name] = ImageConversion.EncodeArrayToPNG(pixels, GraphicsFormat.R8G8B8A8_UNorm, (uint)edge, (uint)height);
                return _folder.Length > 0 ? _folder + "/" + name : name;
            }

            public void WriteReport(string report, List<PlaytestTile> tiles)
            {
                _report = report;
                Tiles.Clear();
                Tiles.AddRange(tiles);
            }

            public void Draw()
            {
                GUI.depth = -10000;
                var px = 1f / PointsPerScreenPixel();
                var state = Cursor;
                if (state.Target.HasValue)
                {
                    var r = state.Target.Value;
                    var line = Mathf.Max(2f, 2f * px);
                    var area = new Rect(r.x * Screen.width - line, r.y * Screen.height - line, r.width * Screen.width + 2 * line, r.height * Screen.height + 2 * line);
                    Frame(area, line, _theme.accent);
                }
                var clicking = state.ClickFrame >= 0 && _theme.clickSheet != null;
                var texture = clicking ? _theme.clickSheet : _theme.idle;
                if (texture != null)
                {
                    var uv = new Rect(0, 0, 1, 1);
                    if (clicking)
                    {
                        var col = state.ClickFrame % _theme.columns;
                        var row = state.ClickFrame / _theme.columns;
                        uv = new Rect((float)col / _theme.columns, 1f - (row + 1f) / _theme.rows, 1f / _theme.columns, 1f / _theme.rows);
                    }
                    var size = _theme.size * px;
                    var tip = new Vector2(state.Position.x * Screen.width, state.Position.y * Screen.height);
                    GUI.DrawTextureWithTexCoords(new Rect(tip.x - size * _theme.hotspot.x, tip.y - size * _theme.hotspot.y, size, size), texture, uv);
                }
                var font = Mathf.RoundToInt(12 * px);
                if (state.Caption.Length > 0)
                {
                    _caption ??= Style(_theme.accent, new Color(0f, 0f, 0f, 0.7f), font);
                    var content = new GUIContent(state.Caption);
                    var at = _caption.CalcSize(content);
                    GUI.Label(new Rect(6 * px, Screen.height - at.y - 6 * px, at.x, at.y), content, _caption);
                }
                if (state.Blocked.Length > 0)
                {
                    _blocked ??= Style(Color.white, _theme.warning, font);
                    var content = new GUIContent(state.Blocked);
                    var at = _blocked.CalcSize(content);
                    GUI.Label(new Rect(state.Position.x * Screen.width + 16 * px, state.Position.y * Screen.height + 16 * px, at.x, at.y), content, _blocked);
                }
            }

            static void Frame(Rect area, float line, Color color)
            {
                var previous = GUI.color;
                GUI.color = color;
                GUI.DrawTexture(new Rect(area.x, area.y, area.width, line), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(area.x, area.yMax - line, area.width, line), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(area.x, area.y, line, area.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(area.xMax - line, area.y, line, area.height), Texture2D.whiteTexture);
                GUI.color = previous;
            }

            static GUIStyle Style(Color text, Color background, int fontSize)
            {
                var fill = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                fill.SetPixel(0, 0, background);
                fill.Apply();
                var padding = Mathf.RoundToInt(fontSize * 0.5f);
                return new GUIStyle
                {
                    fontSize = fontSize,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = text, background = fill },
                    padding = new RectOffset(padding, padding, padding / 3, padding / 3),
                };
            }
        }
    }
}
