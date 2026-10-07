#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Feeder.MCP.Editor.HyperTesting
{
    // experimental and off by default: only a human clicking in HyperTestingWindow can turn it on
    public static class HyperTestingActivation
    {
        public const string ModeName = "Hyper Testing Mode (Experimental)";
        internal const string LogPrefix = "[Hyper Testing]";
        const string KeyPrefix = "Feeder_HyperTesting_Enabled_";

        static readonly string[] ActivationMarkers = { "HyperTestingActivation", "HyperTestingWindow", KeyPrefix, "Feeder_HyperTesting" };

        public static bool IsSupportedPlatform => Application.platform == RuntimePlatform.WindowsEditor;

        public static bool IsEnabled => IsEnabledFor(Application.dataPath);

        // the stored value is the package version, so every package update turns the mode off again
        internal static bool IsEnabledFor(string dataPath)
        {
            var stored = EditorPrefs.GetString(KeyFor(dataPath), string.Empty);
            return IsSupportedPlatform && stored.Length > 0 && stored == PackageVersion;
        }

        internal static string PackageVersion =>
            UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(HyperTestingActivation).Assembly)?.version ?? "unknown";

        static string KeyFor(string dataPath) => KeyPrefix + Path.GetFullPath(dataPath).Replace('\\', '/').ToLowerInvariant();

        internal static bool TryEnableFromHumanClick(string typedConfirmation, out string error)
        {
            error = string.Empty;
            if (!IsSupportedPlatform)
            {
                error = "Hyper Testing Mode needs Windows (it uses directory junctions).";
                return false;
            }
            if (!IsHumanGuiCall())
            {
                error = "Hyper Testing Mode can only be enabled by clicking in the Hyper Testing window.";
                Debug.LogError($"{LogPrefix} refused an enable call that did not come from the window. Agents must ask the user to enable it.");
                return false;
            }
            if (!string.Equals(typedConfirmation.Trim(), ExpectedConfirmation, StringComparison.Ordinal))
            {
                error = $"Type the project folder name '{ExpectedConfirmation}' exactly to confirm.";
                return false;
            }
            if (!MatrixActivation.IsEnabled)
            {
                error = "Turn Matrix on first (Tools > Feeder > Setup...).";
                return false;
            }

            EditorPrefs.SetString(KeyFor(Application.dataPath), PackageVersion);
            HyperTestingWorkspace.Scaffold();
            Debug.Log($"{LogPrefix} enabled for this project on this machine (package {PackageVersion}). It turns off again on the next package update.");
            return true;
        }

        internal static void Disable()
        {
            EditorPrefs.DeleteKey(KeyFor(Application.dataPath));
            HyperTestingWorkspace.RemoveAgentFiles();
            Debug.Log($"{LogPrefix} disabled. Running clones keep running until you stop them; their bridges refuse runner work from now on.");
        }

        internal static string ExpectedConfirmation => new DirectoryInfo(MatrixPluginEditor.ProjectRootPath).Name;

        // a HyperTestingWindow GUI event on the stack and no dynamically compiled (script-execute) frame
        static bool IsHumanGuiCall()
        {
            if (Event.current == null)
                return false;

            var fromWindow = false;
            foreach (var frame in new StackTrace(false).GetFrames() ?? Array.Empty<StackFrame>())
            {
                var type = frame.GetMethod()?.DeclaringType;
                var assembly = type?.Assembly;
                if (assembly == null)
                    continue;
                if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
                    return false;
                if (type == typeof(HyperTestingWindow) && frame.GetMethod()?.Name == "OnGUI")
                    fromWindow = true;
            }
            return fromWindow;
        }

        internal static bool LooksLikeActivationAttempt(string? code)
        {
            if (string.IsNullOrEmpty(code))
                return false;
            foreach (var marker in ActivationMarkers)
            {
                if (code!.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        internal const string ActivationRefusal =
            "Refused: this code touches Hyper Testing Mode activation. Hyper Testing Mode is experimental and only the user may enable it, " +
            "from Tools > Feeder > Hyper Testing (Experimental). Ask the user instead.";
    }
}
