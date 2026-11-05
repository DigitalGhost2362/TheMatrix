#nullable enable
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Feeder.MCP.Editor.UI;
using Feeder.MCP.Runtime.Utils;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Feeder.MCP.Editor.Utils
{
    // Once a day, when Matrix Bridge was installed from a Git URL, compares the installed version with the newest
    // version tag of that repository (git ls-remote --tags) and shows one popup per new version.
    public static class UpdateCheck
    {
        public const string UserOptOutKey = "Feeder_MCP_UpdateCheck_Off";
        public const string ProjectOptOutFile = "Feeder_MCP_NoUpdateCheck";
        const string LastDayKey = "Feeder_MCP_UpdateCheck_Day";
        const string LatestKey = "Feeder_MCP_UpdateCheck_Latest";
        const string NotifiedKey = "Feeder_MCP_UpdateCheck_Notified";
        const int GitTimeoutMs = 20000;

        static volatile CheckOutcome? _pending;
        static bool _running;

        sealed class CheckOutcome
        {
            public string Installed = string.Empty;
            public string? Latest;
            public string? Error;
            public bool Force;
        }

        #region Settings
        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath)!;

        // scoped per project: EditorPrefs are shared by every project of the user
        static string Scoped(string key) => key + "_" + StableHash(ProjectRoot).ToString("x");

        static uint StableHash(string text)
        {
            var h = 2166136261u;
            foreach (var c in text)
                h = (h ^ c) * 16777619u;
            return h;
        }

        static string OptOutFilePath => Path.Combine(ProjectRoot, "ProjectSettings", ProjectOptOutFile);

        public static bool UserEnabled
        {
            get => !EditorPrefs.GetBool(UserOptOutKey, false);
            set => EditorPrefs.SetBool(UserOptOutKey, !value);
        }

        public static bool ProjectEnabled
        {
            get => !File.Exists(OptOutFilePath);
            set
            {
                if (value) { if (File.Exists(OptOutFilePath)) File.Delete(OptOutFilePath); }
                else File.WriteAllText(OptOutFilePath, "Delete this file to let Matrix Bridge check for new versions again.\n");
            }
        }
        #endregion

        #region Pure logic
        // 'com.pkg@https://host/org/repo.git?path=/Packages/x#rev' -> 'https://host/org/repo.git'
        internal static string? ParseGitUrl(string? packageId)
        {
            if (string.IsNullOrEmpty(packageId))
                return null;
            var at = packageId!.IndexOf('@');
            if (at < 0)
                return null;
            var url = packageId.Substring(at + 1);
            var cut = url.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0)
                url = url.Substring(0, cut);
            if (url.StartsWith("git+", StringComparison.OrdinalIgnoreCase))
                url = url.Substring(4);
            return url.Length == 0 || url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? null : url;
        }

        static readonly Regex _tag = new Regex(@"(?:^|[^0-9])v?(\d+)\.(\d+)\.(\d+)$", RegexOptions.Compiled);

        internal static bool TryParseVersion(string? text, out Version version)
        {
            version = new Version(0, 0, 0);
            if (string.IsNullOrWhiteSpace(text))
                return false;
            var m = _tag.Match(text!.Trim());
            if (!m.Success)
                return false;
            version = new Version(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
            return true;
        }

        // highest stable x.y.z tag in 'git ls-remote --tags' output; null when there is none
        internal static string? LatestTagVersion(string lsRemoteOutput)
        {
            Version? best = null;
            foreach (var line in (lsRemoteOutput ?? string.Empty).Split('\n'))
            {
                const string marker = "refs/tags/";
                var i = line.IndexOf(marker, StringComparison.Ordinal);
                if (i < 0)
                    continue;
                var name = line.Substring(i + marker.Length).Trim();
                if (name.EndsWith("^{}", StringComparison.Ordinal))
                    name = name.Substring(0, name.Length - 3);
                if (TryParseVersion(name, out var v) && (best == null || v > best))
                    best = v;
            }
            return best == null ? null : $"{best.Major}.{best.Minor}.{best.Build}";
        }

        internal static bool IsNewer(string? latest, string? installed)
            => TryParseVersion(latest, out var l) && TryParseVersion(StripSuffix(installed), out var i) && l > i;

        static string? StripSuffix(string? v)
        {
            var dash = v?.IndexOf('-') ?? -1;
            return dash > 0 ? v!.Substring(0, dash) : v;
        }

        internal static bool IsDue(string? lastDay, DateTime utcNow)
            => !string.Equals(lastDay, utcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        #endregion

        [InitializeOnLoadMethod]
        static void Start()
        {
            if (Application.isBatchMode || EnvironmentUtils.IsCi())
                return;
            EditorApplication.delayCall += () => Run(force: false);
        }

        // force: ignore the daily cache and the opt-outs (the menu item); the popup is shown even for a version already announced
        public static void Run(bool force)
        {
            if (_running)
                return;
            if (!force && (!UserEnabled || !ProjectEnabled || !IsDue(EditorPrefs.GetString(Scoped(LastDayKey), string.Empty), DateTime.UtcNow)))
                return;

            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UpdateCheck).Assembly);
            if (info == null)
                return;
            var url = info.source == PackageSource.Git ? ParseGitUrl(info.packageId) : null;
            if (url == null)
            {
                if (force)
                    Debug.Log($"[Matrix] Update check: Matrix Bridge {info.version} is not installed from a Git URL ({info.source}); nothing to compare.");
                return;
            }

            _running = true;
            var installed = info.version;
            EditorPrefs.SetString(Scoped(LastDayKey), DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Task.Run(() =>
            {
                var outcome = new CheckOutcome { Installed = installed, Force = force };
                try
                {
                    outcome.Latest = LatestTagVersion(RunGit(url));
                    if (outcome.Latest == null)
                        outcome.Error = "the repository has no x.y.z version tags";
                }
                catch (Exception ex)
                {
                    outcome.Error = ex.Message;
                }
                _pending = outcome;
            });
            EditorApplication.update += Poll;
        }

        static string RunGit(string url)
        {
            var psi = new ProcessStartInfo("git", $"ls-remote --tags --refs \"{url}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("git could not be started");
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(GitTimeoutMs))
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
                throw new TimeoutException("git ls-remote timed out");
            }
            if (process.ExitCode != 0)
                throw new InvalidOperationException("git ls-remote failed: " + process.StandardError.ReadToEnd().Trim());
            return output.Result;
        }

        static void Poll()
        {
            var outcome = _pending;
            if (outcome == null)
                return;
            _pending = null;
            _running = false;
            EditorApplication.update -= Poll;

            if (outcome.Error != null)
            {
                if (outcome.Force)
                    Debug.LogWarning($"[Matrix] Update check failed: {outcome.Error}");
                return;
            }

            EditorPrefs.SetString(Scoped(LatestKey), outcome.Latest!);
            if (!IsNewer(outcome.Latest, outcome.Installed))
            {
                if (outcome.Force)
                    Debug.Log($"[Matrix] Matrix Bridge {outcome.Installed} is up to date (newest tag {outcome.Latest}).");
                return;
            }

            if (!outcome.Force && EditorPrefs.GetString(Scoped(NotifiedKey), string.Empty) == outcome.Latest)
                return;
            EditorPrefs.SetString(Scoped(NotifiedKey), outcome.Latest!);
            NotificationPopupWindow.Show(
                windowTitle: "Matrix Bridge update",
                title: $"Matrix Bridge {outcome.Latest} is available",
                message: $"You have {outcome.Installed}. Update it in Window > Package Manager (Matrix Bridge > Update).\n\n" +
                    "To stop this check: Tools > Feeder > Update Check, for you or for this project.");
        }

        #region Menu
        [MenuItem("Tools/Feeder/Update Check/Check Now")]
        static void CheckNow() => Run(force: true);

        [MenuItem("Tools/Feeder/Update Check/Enabled For Me")]
        static void ToggleUser() => UserEnabled = !UserEnabled;

        [MenuItem("Tools/Feeder/Update Check/Enabled For Me", true)]
        static bool ToggleUserValidate()
        {
            Menu.SetChecked("Tools/Feeder/Update Check/Enabled For Me", UserEnabled);
            return true;
        }

        [MenuItem("Tools/Feeder/Update Check/Enabled For This Project")]
        static void ToggleProject() => ProjectEnabled = !ProjectEnabled;

        [MenuItem("Tools/Feeder/Update Check/Enabled For This Project", true)]
        static bool ToggleProjectValidate()
        {
            Menu.SetChecked("Tools/Feeder/Update Check/Enabled For This Project", ProjectEnabled);
            return true;
        }
        #endregion
    }
}
