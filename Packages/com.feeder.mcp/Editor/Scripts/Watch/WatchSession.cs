#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace Feeder.MCP.Editor.Watch
{
    // the armed watch outlives domain reloads in SessionState; recording runs only in play mode and ends
    // on whichever comes first: exiting play mode or the STOP button
    [InitializeOnLoad]
    internal static class WatchSession
    {
        public const string RootFolder = "MatrixWatch";
        public const string ReasonExitPlay = "exit play mode";
        public const string ReasonStopButton = "stop button";
        public const string ReasonAgent = "watch stop";

        const string FolderKey = "Feeder.Matrix.Watch.Folder";
        const string PlayerKey = "Feeder.Matrix.Watch.Player";
        const string StartKey = "Feeder.Matrix.Watch.Start";

        static WatchRecorder? _recorder;

        static WatchSession()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            // update, not delayCall: delayCall waits until the Editor is focused again, update keeps running
            if (IsArmed)
                EditorApplication.update += Resume;
        }

        public static string Folder => SessionState.GetString(FolderKey, string.Empty);
        public static bool IsArmed => Folder.Length > 0;
        public static WatchRecorder? Recorder => _recorder;
        public static string Root => Path.Combine(MatrixPluginEditor.ProjectRootPath, RootFolder);

        public static string Arm(string name, string player)
        {
            if (IsArmed)
                throw new InvalidOperationException($"Already watching into {Folder}. Stop it first.");

            Directory.CreateDirectory(Root);
            var ignore = Path.Combine(Root, ".gitignore");
            if (!File.Exists(ignore))
                File.WriteAllText(ignore, "*\n", new UTF8Encoding(false));

            var stem = $"{DateTime.Now:yyyyMMdd-HHmm}-{Slug(name)}";
            var folder = Path.Combine(Root, stem);
            for (var i = 2; Directory.Exists(folder); i++)
                folder = Path.Combine(Root, $"{stem}-{i}");
            Directory.CreateDirectory(Path.Combine(folder, "frames"));

            SessionState.SetString(FolderKey, folder);
            SessionState.SetString(PlayerKey, player.Trim());
            SessionState.EraseString(StartKey);
            if (EditorApplication.isPlaying)
                BeginRecording();
            WatchFrame.Show(EditorApplication.isPlaying);
            WatchFrame.Keep();
            return folder;
        }

        // returns the finished folder with its summary, or null when nothing was recorded
        public static string? Stop(string reason)
        {
            if (!IsArmed)
                return null;
            var folder = Folder;
            try
            {
                _recorder?.End(reason, final: true);
            }
            finally
            {
                _recorder = null;
                SessionState.EraseString(FolderKey);
                SessionState.EraseString(PlayerKey);
                SessionState.EraseString(StartKey);
                WatchFrame.Hide();
            }

            if (!File.Exists(Path.Combine(folder, WatchSummary.EventsFile)))
            {
                Directory.Delete(folder, recursive: true);
                return null;
            }
            try
            {
                WatchSummary.Write(folder);
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(folder, WatchSummary.SummaryFile),
                    $"# Watch {Path.GetFileName(folder)}\n\nSummary failed: {e.Message}. Read `{WatchSummary.EventsFile}`.\n");
                UnityEngine.Debug.LogException(e);
            }
            return folder;
        }

        public static string? LatestFolder()
        {
            if (!Directory.Exists(Root))
                return null;
            return Directory.GetDirectories(Root)
                .Where(dir => File.Exists(Path.Combine(dir, WatchSummary.SummaryFile)))
                .OrderByDescending(dir => File.GetLastWriteTimeUtc(Path.Combine(dir, WatchSummary.SummaryFile)))
                .FirstOrDefault();
        }

        static void BeginRecording()
        {
            if (_recorder != null)
                return;
            var resumed = double.TryParse(SessionState.GetString(StartKey, string.Empty), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var start);
            if (!resumed)
            {
                start = EditorApplication.timeSinceStartup;
                SessionState.SetString(StartKey, start.ToString("R", CultureInfo.InvariantCulture));
            }
            _recorder = new WatchRecorder(Folder, SessionState.GetString(PlayerKey, string.Empty), start, resumed);
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!IsArmed)
                return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                BeginRecording();
                WatchFrame.Show(true);
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Stop(ReasonExitPlay);
            }
        }

        // a script recompile while playing reloads the domain; the session file is closed and reopened after
        static void OnBeforeAssemblyReload()
        {
            _recorder?.End("domain reload", final: false);
            _recorder = null;
        }

        static void Resume()
        {
            EditorApplication.update -= Resume;
            if (!IsArmed)
                return;
            if (EditorApplication.isPlaying)
                BeginRecording();
            WatchFrame.Keep();
        }

        static string Slug(string name)
        {
            var plain = new string(name.Trim().ToLowerInvariant().Replace('\u0111', 'd').Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
            var slug = Regex.Replace(plain, "[^a-z0-9]+", "-").Trim('-');
            if (slug.Length > 40)
                slug = slug.Substring(0, 40).Trim('-');
            return slug.Length > 0 ? slug : "session";
        }
    }
}
