#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.API.BuildRunner
{
    [Serializable]
    public sealed class BuildJobRecord
    {
        public string jobId = string.Empty;
        public string state = BuildJobStore.Queued;
        public string target = string.Empty;
        public string outputPath = string.Empty;
        public List<string> scenes = new();
        public bool development;
        public bool cancelRequested;
        public long startedTicks;
        public long finishedTicks;
        public string error = string.Empty;
        public string result = string.Empty;
        public int totalErrors;
        public int totalWarnings;
        public long totalSizeBytes;
        public double durationSec;
        public List<string> messages = new();

        public bool IsTerminal => state != BuildJobStore.Queued && state != BuildJobStore.Running;
    }

    // last build, kept in Library so a domain reload (platform switch) or a restart cannot lose it; read without the main thread
    public static class BuildJobStore
    {
        public const string Queued = "queued";
        public const string Running = "running";
        public const string Succeeded = "succeeded";
        public const string Failed = "failed";
        public const string Cancelled = "cancelled";
        public const string Interrupted = "interrupted";
        public const int RetryAfterMs = 3000;
        public const int MaxMessages = 20;

        public static string? PathOverride;
        public static string ProjectRoot = string.Empty;

        static readonly object _gate = new();

        static string FilePath => PathOverride ?? Path.Combine(ProjectRoot, "Library", "Feeder_MCP", "last-build.json");

        public static BuildJobRecord? Read()
        {
            lock (_gate)
            {
                try
                {
                    var path = FilePath;
                    if (!File.Exists(path))
                        return null;
                    var record = JsonUtility.FromJson<BuildJobRecord>(File.ReadAllText(path));
                    return string.IsNullOrEmpty(record?.jobId) ? null : record;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public static void Write(BuildJobRecord record)
        {
            lock (_gate)
            {
                try
                {
                    var path = FilePath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, JsonUtility.ToJson(record));
                }
                catch (Exception)
                {
                }
            }
        }

        public static BuildJobRecord Begin(BuildPlan plan)
        {
            var record = new BuildJobRecord
            {
                jobId = Guid.NewGuid().ToString("N").Substring(0, 12),
                target = plan.Target.ToString(),
                outputPath = plan.OutputPath,
                scenes = plan.Scenes.ToList(),
                development = plan.Development,
                startedTicks = DateTime.UtcNow.Ticks,
            };
            Write(record);
            return record;
        }

        public static void MarkRunning(string jobId)
        {
            var record = Read();
            if (record == null || record.jobId != jobId || record.IsTerminal)
                return;
            record.state = Running;
            Write(record);
        }

        public static bool RequestCancel(string jobId)
        {
            var record = Read();
            if (record == null || record.jobId != jobId || record.IsTerminal)
                return false;
            record.cancelRequested = true;
            if (record.state == Queued)
                Complete(record, Cancelled, "Cancelled before the build started.");
            else
                Write(record);
            return true;
        }

        public static void Complete(BuildJobRecord record, string state, string error)
        {
            record.state = state;
            record.error = error;
            record.finishedTicks = DateTime.UtcNow.Ticks;
            Write(record);
        }
    }

    public sealed class BuildPlan
    {
        public string[] Scenes = Array.Empty<string>();
        public string OutputPath = string.Empty;
        public BuildTarget Target;
        public bool Development;
    }

    public static class BuildRequest
    {
        public const int MaxScenes = 200;

        static readonly string[] ProtectedFolders = { "Assets", "Packages", "ProjectSettings", "Library" };

        public static BuildTarget ParseTarget(string? text, BuildTarget active)
        {
            var name = (text ?? string.Empty).Trim();
            if (name.Length == 0)
                return active;
            switch (name.ToLowerInvariant())
            {
                case "windows": case "win": case "win64": return BuildTarget.StandaloneWindows64;
                case "mac": case "macos": case "osx": return BuildTarget.StandaloneOSX;
                case "linux": case "linux64": return BuildTarget.StandaloneLinux64;
            }
            if (char.IsDigit(name[0]) || !Enum.TryParse<BuildTarget>(name, true, out var target) || target == BuildTarget.NoTarget)
                throw new ArgumentException($"Unknown build target '{name}'. Use a BuildTarget name such as StandaloneWindows64, StandaloneOSX, StandaloneLinux64, Android, iOS or WebGL.");
            return target;
        }

        public static string Extension(BuildTarget target) => target switch
        {
            BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => ".exe",
            BuildTarget.StandaloneOSX => ".app",
            BuildTarget.Android => ".apk",
            _ => string.Empty,
        };

        static string Slash(string path) => path.Replace('\\', '/');

        public static string NormalizeScene(string scene)
        {
            var path = Slash((scene ?? string.Empty).Trim());
            if (path.Length == 0)
                throw new ArgumentException("A scene path is empty.");
            var rooted = path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal);
            if (!rooted || !path.EndsWith(".unity", StringComparison.Ordinal) || path.Split('/').Contains(".."))
                throw new ArgumentException($"Scene '{path}' must be a project path like 'Assets/Scenes/Main.unity'.");
            return path;
        }

        public static string Sanitize(string name)
        {
            var bad = Path.GetInvalidFileNameChars();
            var clean = new string((name ?? string.Empty).Select(c => bad.Contains(c) || c == ' ' ? '_' : c).ToArray()).Trim('_', '.');
            return clean.Length == 0 ? "Game" : clean;
        }

        /// <param name="defaultScenes">Enabled scenes of the Build Settings, used when scenes is empty.</param>
        public static BuildPlan Validate
        (
            IReadOnlyList<string>? scenes,
            string? outputPath,
            string? target,
            bool development,
            string projectRoot,
            BuildTarget activeTarget,
            Func<string, bool> sceneExists,
            IReadOnlyList<string> defaultScenes,
            string productName
        )
        {
            var buildTarget = ParseTarget(target, activeTarget);

            var list = new List<string>();
            foreach (var scene in scenes != null && scenes.Count > 0 ? scenes : defaultScenes)
            {
                var path = NormalizeScene(scene);
                if (!list.Contains(path))
                    list.Add(path);
            }
            if (list.Count == 0)
                throw new ArgumentException("No scenes to build: pass scenes, or enable scenes in the Build Settings.");
            if (list.Count > MaxScenes)
                throw new ArgumentException($"At most {MaxScenes} scenes, got {list.Count}.");
            var missing = list.Where(s => !sceneExists(s)).ToList();
            if (missing.Count > 0)
                throw new ArgumentException("Scene not found: " + string.Join(", ", missing));

            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new ArgumentException("The project folder is unknown.");
            var root = Slash(Path.GetFullPath(projectRoot)).TrimEnd('/');
            var extension = Extension(buildTarget);
            var requested = string.IsNullOrWhiteSpace(outputPath)
                ? $"Builds/{buildTarget}/{Sanitize(productName)}{extension}"
                : outputPath!.Trim();

            string full;
            try
            {
                full = Slash(Path.GetFullPath(Path.IsPathRooted(requested) ? requested : Path.Combine(root, requested))).TrimEnd('/');
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                throw new ArgumentException($"outputPath '{requested}' is not a valid path.");
            }
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("outputPath cannot be the project folder.");
            foreach (var folder in ProtectedFolders)
            {
                var guarded = root + "/" + folder;
                if (full.Equals(guarded, StringComparison.OrdinalIgnoreCase) || full.StartsWith(guarded + "/", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"outputPath must not be inside '{folder}'. Use Builds/... or Temp/... or a folder outside the project.");
            }
            if (extension.Length > 0 && !full.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                full += extension;

            return new BuildPlan { Scenes = list.ToArray(), OutputPath = full, Target = buildTarget, Development = development };
        }
    }

    public static class BuildStatusFormat
    {
        public static string Format(BuildJobRecord? record, string? jobId, long nowTicks)
        {
            if (record == null)
                return "[no_build] No build has been recorded yet.";
            if (!string.IsNullOrEmpty(jobId) && jobId != record.jobId)
                return $"[unknown_job] No record for job '{jobId}'. The last build is '{record.jobId}'.";

            var end = record.finishedTicks > 0 ? record.finishedTicks : nowTicks;
            var elapsed = TimeSpan.FromTicks(Math.Max(0, end - record.startedTicks));
            var sb = new StringBuilder();
            sb.Append($"job_id={record.jobId} state={record.state} target={record.target} elapsed_s={(int)elapsed.TotalSeconds} output={record.outputPath}");
            if (record.cancelRequested && !record.IsTerminal)
                sb.Append(" cancel_requested=true");
            if (!record.IsTerminal)
            {
                sb.Append($" retry_after_ms={BuildJobStore.RetryAfterMs}");
                return sb.ToString();
            }

            sb.Append($"\nscenes: {string.Join(", ", record.scenes)}");
            if (record.state == BuildJobStore.Succeeded)
                sb.Append($"\nsummary: result={record.result} errors={record.totalErrors} warnings={record.totalWarnings} size_bytes={record.totalSizeBytes} duration_s={record.durationSec:0.###}");
            else if (record.totalErrors > 0 || record.totalWarnings > 0)
                sb.Append($"\nsummary: result={record.result} errors={record.totalErrors} warnings={record.totalWarnings}");
            if (!string.IsNullOrEmpty(record.error))
                sb.Append("\nerror: ").Append(record.error);
            foreach (var message in record.messages)
                sb.Append("\n- ").Append(message);
            return sb.ToString();
        }
    }
}
