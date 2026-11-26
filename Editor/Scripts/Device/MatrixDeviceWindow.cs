#nullable enable
using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Feeder.MCP.Editor.API;
using Feeder.MCP.Editor.API.BuildRunner;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Device
{
    // LAN on/off, the Android device build, the install QR and whether a phone is linked
    internal sealed class MatrixDeviceWindow : EditorWindow
    {
        const string DevelopmentKey = "Feeder.Matrix.Device.Development";
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        Texture2D? _qr;
        string _qrText = string.Empty;
        string _phone = "unknown";
        double _nextPoll;
        Vector2 _scroll;

        [MenuItem(UI.MenuItems.Root + "Device", priority = -17)]
        public static void ShowWindow()
        {
            var window = GetWindow<MatrixDeviceWindow>("Matrix Device");
            window.minSize = new Vector2(360, 520);
        }

        void OnEnable() => EditorApplication.update += Poll;
        void OnDisable()
        {
            EditorApplication.update -= Poll;
            if (_qr != null)
                DestroyImmediate(_qr);
        }

        void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll)
                return;
            _nextPoll = EditorApplication.timeSinceStartup + 2;
            _ = ReadPhoneAsync();
        }

        // the server's health lists player links; a device build is one
        async Task ReadPhoneAsync()
        {
            string text;
            try { text = await Http.GetStringAsync($"http://127.0.0.1:{MatrixPluginEditor.Port}/healthz"); }
            catch (Exception) { text = string.Empty; }
            var players = Regex.Match(text, "\"playerLinks\":\\[(.*?)\\]\\}?$", RegexOptions.Singleline).Groups[1].Value;
            var phone = text.Length == 0 ? "server not reachable"
                : Regex.IsMatch(players, "\"connected\":true") ? "linked"
                : "not linked";
            if (phone != _phone)
            {
                _phone = phone;
                Activity.EditorEvents.NextTick(Repaint);
            }
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.HelpBox("Run 'playtest' flows on an Android phone on the same Wi-Fi as this PC. " +
                                    "No developer options or USB debugging on the phone.", MessageType.None);

            EditorGUILayout.LabelField("1. LAN", EditorStyles.boldLabel);
            var lan = MatrixDeviceSettings.LanEnabled;
            using (new EditorGUI.DisabledScope(lan))
                MatrixDeviceSettings.LanPort = EditorGUILayout.IntField("LAN port", MatrixDeviceSettings.LanPort);
            var addresses = MatrixDeviceSettings.Addresses();
            if (addresses.Count == 0)
                EditorGUILayout.HelpBox("No network address found: connect this PC to the Wi-Fi network of the phone.", MessageType.Warning);
            else
            {
                var index = Math.Max(0, addresses.IndexOf(MatrixDeviceSettings.Host));
                var picked = EditorGUILayout.Popup("PC address", index, addresses.ToArray());
                if (picked != index)
                    MatrixDeviceSettings.Host = addresses[picked];
            }
            var wanted = EditorGUILayout.Toggle("LAN on", lan);
            if (wanted != lan)
                MatrixDeviceSettings.SetLan(wanted);
            EditorGUILayout.LabelField(lan
                ? "The server restarts with the LAN listener. The first time, Windows asks to allow it: pick Private networks."
                : "Off: the server listens on this PC only.", EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("2. Build", EditorStyles.boldLabel);
            var development = EditorGUILayout.ToggleLeft("Development Build (Profiler over Wi-Fi)", EditorPrefs.GetBool(DevelopmentKey, false));
            EditorPrefs.SetBool(DevelopmentKey, development);
            using (new EditorGUI.DisabledScope(!lan || BuildPipeline.isBuildingPlayer))
                if (GUILayout.Button("Build Android", GUILayout.Height(28)))
                {
                    try { Debug.Log("[Matrix device] " + Tool_Build.Start(development: development, device: true)); }
                    catch (Exception e) { Debug.LogError("[Matrix device] build not started: " + e.Message); }
                }
            var job = BuildJobStore.Read();
            if (job != null && job.device)
                EditorGUILayout.LabelField($"Last device build: {job.state}{(job.error.Length > 0 ? " - " + job.error : string.Empty)}", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Open APK folder"))
            {
                System.IO.Directory.CreateDirectory(MatrixDeviceSettings.Folder);
                EditorUtility.RevealInFinder(MatrixDeviceSettings.Folder + "/");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("3. Install on the phone", EditorStyles.boldLabel);
            if (!lan)
                EditorGUILayout.LabelField("Turn LAN on to get the install QR.", EditorStyles.wordWrappedMiniLabel);
            else
            {
                var url = MatrixDeviceSettings.PageUrl;
                DrawQr(url);
                EditorGUILayout.LabelField("Scan with the phone camera, tap Download APK, then Install. " +
                                           "If the page does not open: same Wi-Fi? Windows firewall allowed? Some office Wi-Fi blocks devices from each other (use a hotspot).",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.SelectableLabel(url, EditorStyles.textField, GUILayout.Height(18));
                if (GUILayout.Button("Open the page on this PC"))
                    Application.OpenURL(url);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("4. Phone", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Player link: " + _phone);
            EditorGUILayout.LabelField("Then ask the agent: playtest action=run flow=<name> device=true", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
        }

        void DrawQr(string text)
        {
            if (_qr == null || _qrText != text)
            {
                if (_qr != null)
                    DestroyImmediate(_qr);
                _qr = Render(QrCode.Encode(text));
                _qrText = text;
            }
            var side = Mathf.Min(position.width - 24, 260);
            var rect = GUILayoutUtility.GetRect(side, side, GUILayout.ExpandWidth(false));
            rect.x = (position.width - side) / 2;
            GUI.DrawTexture(rect, _qr, ScaleMode.ScaleToFit, false);
        }

        // one pixel per module plus a 4-module quiet zone; point filtering keeps the edges sharp
        static Texture2D Render(bool[,] modules)
        {
            const int Quiet = 4;
            var size = modules.GetLength(0);
            var side = size + Quiet * 2;
            var texture = new Texture2D(side, side, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[side * side];
            for (var y = 0; y < side; y++)
                for (var x = 0; x < side; x++)
                {
                    int my = y - Quiet, mx = x - Quiet;
                    var dark = my >= 0 && mx >= 0 && my < size && mx < size && modules[my, mx];
                    // texture rows go bottom-up
                    pixels[(side - 1 - y) * side + x] = dark ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255);
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
