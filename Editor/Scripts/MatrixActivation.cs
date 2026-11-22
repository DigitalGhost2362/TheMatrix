#nullable enable
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor
{
    public static class MatrixActivation
    {
        const string WindowRequestedKey = "Feeder_Matrix_WindowRequested";

        // machine-local on purpose: UserSettings can be committed and this must never reach teammates
        static string EnabledKey => $"Feeder_Matrix_Enabled_{Application.dataPath}";

        public static bool IsEnabled => EditorPrefs.GetBool(EnabledKey, false);

        public static bool IsInUse => IsEnabled || SessionState.GetBool(WindowRequestedKey, false);

        public static void RequestWindow() => SessionState.SetBool(WindowRequestedKey, true);

        public static void Enable()
        {
            EditorPrefs.SetBool(EnabledKey, true);
            MatrixPluginEditor.KeepConnected = true;
            MatrixPluginEditor.KeepServerRunning = true;
            MatrixPluginEditor.Instance.Save();
            McpServerManager.Activate();
            Startup.Activate();
            _ = MatrixPluginEditor.ConnectIfNeeded();
        }

        public static void Disable()
        {
            EditorPrefs.DeleteKey(EnabledKey);
            MatrixPluginEditor.KeepConnected = false;
            MatrixPluginEditor.KeepServerRunning = false;
            MatrixPluginEditor.Instance.Save();
            if (MatrixPluginEditor.Instance.HasToolHostInstance)
                _ = MatrixPluginEditor.Instance.Disconnect();
            McpServerManager.StopServer();
        }

        internal static bool CloseIfDormant(EditorWindow window)
        {
            if (IsInUse)
                return false;

            void CloseOnce()
            {
                EditorApplication.update -= CloseOnce;
                if (window != null)
                    window.Close();
            }

            EditorApplication.update += CloseOnce;
            return true;
        }
    }
}
