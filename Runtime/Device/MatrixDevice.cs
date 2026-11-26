#nullable enable
using System;
using Feeder.MCP.Player;
using UnityEngine;

namespace Feeder.MCP.Device
{
    /// <summary>
    /// A device build (scripting define MATRIX_DEVICE) links itself to the Matrix server of the PC it was built on,
    /// over the LAN listener, with the address and token the build wrote into <see cref="ConfigResource"/>.
    /// </summary>
    public static class MatrixDevice
    {
        public const string ConfigResource = "MatrixDevice";
        const float WarnAfterSeconds = 10f;

        [Serializable]
        public sealed class Config
        {
            public string host = string.Empty;
            public int port;
            public string token = string.Empty;
            public string project = string.Empty;
        }

        static Config? _config;

        public static string Address => _config == null ? string.Empty : _config.host + ":" + _config.port;
        public static string Host => _config?.host ?? string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoConnect()
        {
            // the Editor links play mode on its own (Server > Link Play Mode As Player)
            if (Application.isEditor || MatrixPlayer.IsConnected)
                return;
            var asset = Resources.Load<TextAsset>(ConfigResource);
            if (asset == null)
            {
                Debug.LogWarning("[Matrix device] no " + ConfigResource + " config in this build; build it from Matrix Device in Unity.");
                return;
            }
            _config = JsonUtility.FromJson<Config>(asset.text);
            MatrixPlayer.Connect(_config.host, _config.port, _config.token, _config.project);
            var status = new GameObject("Matrix Device Status") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(status);
            status.AddComponent<MatrixDeviceStatus>();
        }

        // a line in the corner while the PC cannot be reached, so the tester knows to check the network
        sealed class MatrixDeviceStatus : MonoBehaviour
        {
            GUIStyle? _style;

            void OnGUI()
            {
                if (MatrixPlayer.IsConnected || Time.realtimeSinceStartup < WarnAfterSeconds)
                    return;
                _style ??= new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(14, Screen.height / 60),
                    normal = { textColor = new Color(1f, 0.4f, 0.4f), background = Texture2D.whiteTexture },
                };
                var text = "Matrix: cannot reach the PC at " + Address;
                var size = _style.CalcSize(new GUIContent(text));
                var previous = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0f, 0f, 0f, 0.75f);
                GUI.Label(new Rect(8, 8, size.x + 8, size.y), text, _style);
                GUI.backgroundColor = previous;
            }
        }
    }
}
