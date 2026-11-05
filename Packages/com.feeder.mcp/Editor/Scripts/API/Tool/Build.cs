#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.API.BuildRunner;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public static partial class Tool_Build
    {
        public const string BuildStartToolId = "build-start";
        public const string BuildStatusToolId = "build-status";
        public const string BuildCancelToolId = "build-cancel";

        [AiTool
        (
            BuildStartToolId,
            Title = "Build / Start",
            Enabled = false
        )]
        [AiSkillDescription("Build a player from an explicit scene list into an output path (default: the Build Settings scenes into Builds/<target>/, never inside Assets) for a target (default: the active build target). " +
            "Starts a job and returns at once: poll 'build-status'. The Editor is busy until the build ends.")]
        [Description("Starts a player build as a job and returns a job id. Scenes are built from disk (unsaved changes are not included). " +
            "The Editor is blocked while Unity builds; 'build-status' still answers from the saved record, which survives domain reloads. " +
            "Editor-changing: follows the editor lock.")]
        public static string Start
        (
            [Description("Scene paths such as 'Assets/Scenes/Main.unity'. Default: the enabled scenes of the Build Settings.")]
            string[]? scenes = null,
            [Description("Output file or folder, project-relative or absolute. Default: Builds/<target>/<product name>. Never inside Assets, Packages, ProjectSettings or Library. A missing .exe/.app/.apk extension is added.")]
            string? outputPath = null,
            [Description("A BuildTarget name (StandaloneWindows64, StandaloneOSX, StandaloneLinux64, Android, iOS, WebGL...). Default: the active build target.")]
            string? target = null,
            [Description("Development build. Default: false")]
            bool development = false
        )
        {
            return MainThread.Instance.Run(() =>
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    return "[editor_busy retry_after_ms=5000] The Editor is compiling or importing; try again when it is done.";
                if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                    return "[editor_busy] Stop Play mode before building.";

                var current = BuildJobStore.Read();
                if (current != null && !current.IsTerminal)
                {
                    var age = TimeSpan.FromTicks(Math.Max(0, DateTime.UtcNow.Ticks - current.startedTicks));
                    if (BuildPipeline.isBuildingPlayer || age < TimeSpan.FromSeconds(60))
                        return $"[build_running retry_after_ms={BuildJobStore.RetryAfterMs}] Build job {current.jobId} is still {current.state}.";
                    BuildJobStore.Complete(current, BuildJobStore.Interrupted, "Replaced by a new build; this one never reported back.");
                }

                var root = BuildJobStore.ProjectRoot;
                var plan = BuildRequest.Validate(
                    scenes,
                    outputPath,
                    target,
                    development,
                    root,
                    EditorUserBuildSettings.activeBuildTarget,
                    path => File.Exists(Path.Combine(root, path)) && !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)),
                    EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList(),
                    PlayerSettings.productName);

                if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(plan.Target), plan.Target))
                    throw new InvalidOperationException($"Build target {plan.Target} is not installed in this Unity (add its build support module).");

                var record = BuildJobStore.Begin(plan);
                var jobId = record.jobId;
                BuildJobRunner.Schedule(jobId, plan);
                return BuildStatusFormat.Format(record, null, DateTime.UtcNow.Ticks) +
                       "\nThe build starts in a moment and blocks the Editor until it ends: poll 'build-status'.";
            });
        }

        [AiTool
        (
            BuildStatusToolId,
            Title = "Build / Status",
            ReadOnlyHint = true,
            IdempotentHint = true,
            Enabled = false
        )]
        [AiSkillDescription("State and result of the last 'build-start' (job id, state, output, errors and warnings with messages). Poll after 'build-start'.")]
        [Description("State and result of the last build job: queued, running, succeeded, failed, cancelled or interrupted. " +
            "While it is not finished the answer carries retry_after_ms. Read from a record in Library, so it also works while the Editor is busy building and after a domain reload.")]
        public static string Status
        (
            [Description("Job id returned by 'build-start' (optional; the last build is returned when omitted).")]
            string? jobId = null
        )
            => BuildStatusFormat.Format(BuildJobStore.Read(), jobId, DateTime.UtcNow.Ticks);

        [AiTool
        (
            BuildCancelToolId,
            Title = "Build / Cancel",
            Enabled = false
        )]
        [AiSkillDescription("Cancel the running or queued build job. A queued job never starts; a running build stops at the next scene it processes.")]
        [Description("Cancels a build job. A queued job is cancelled at once; a running one stops when Unity processes its next scene (a single-scene build cannot be interrupted). " +
            "Check 'build-status' for the final state.")]
        public static string Cancel
        (
            [Description("Job id (optional; the last build is used when omitted).")]
            string? jobId = null
        )
        {
            var record = BuildJobStore.Read();
            if (record == null)
                return "[no_build] No build has been recorded yet.";
            if (!string.IsNullOrEmpty(jobId) && jobId != record.jobId)
                return $"[unknown_job] No record for job '{jobId}'. The last build is '{record.jobId}'.";
            if (record.IsTerminal)
                return $"[not_running] Build job {record.jobId} already ended ({record.state}).";
            BuildJobStore.RequestCancel(record.jobId);
            return $"job_id={record.jobId} cancel_requested=true";
        }
    }

    static class BuildJobRunner
    {
        public static bool Executing;

        // an update one-shot plus a wake-up: delayCall can sleep while the Editor is unfocused
        public static void Schedule(string jobId, BuildPlan plan)
        {
            void Tick()
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                    return;
                }
                EditorApplication.update -= Tick;
                Execute(jobId, plan);
            }
            EditorApplication.update += Tick;
            EditorApplication.QueuePlayerLoopUpdate();
        }

        public static BuildPlan PlanOf(BuildJobRecord record) => new BuildPlan
        {
            Scenes = record.scenes.ToArray(),
            OutputPath = record.outputPath,
            Target = Enum.TryParse<BuildTarget>(record.target, out var target) ? target : EditorUserBuildSettings.activeBuildTarget,
            Development = record.development,
        };

        public static void Execute(string jobId, BuildPlan plan)
        {
            var record = BuildJobStore.Read();
            if (record == null || record.jobId != jobId || record.IsTerminal)
                return;
            if (record.cancelRequested)
            {
                BuildJobStore.Complete(record, BuildJobStore.Cancelled, "Cancelled before the build started.");
                return;
            }

            BuildJobStore.MarkRunning(jobId);
            Executing = true;
            try
            {
                var parent = Path.GetDirectoryName(plan.OutputPath);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = plan.Scenes,
                    locationPathName = plan.OutputPath,
                    target = plan.Target,
                    targetGroup = BuildPipeline.GetBuildTargetGroup(plan.Target),
                    options = plan.Development ? BuildOptions.Development : BuildOptions.None,
                });
                Finish(jobId, report);
            }
            catch (Exception e)
            {
                var latest = BuildJobStore.Read();
                if (latest != null && latest.jobId == jobId && !latest.IsTerminal)
                    BuildJobStore.Complete(latest, latest.cancelRequested ? BuildJobStore.Cancelled : BuildJobStore.Failed, e.Message);
            }
            finally
            {
                Executing = false;
            }
        }

        static void Finish(string jobId, BuildReport report)
        {
            var record = BuildJobStore.Read();
            if (record == null || record.jobId != jobId || record.IsTerminal)
                return;

            var summary = report.summary;
            record.result = summary.result.ToString();
            record.totalErrors = summary.totalErrors;
            record.totalWarnings = summary.totalWarnings;
            record.totalSizeBytes = (long)summary.totalSize;
            record.durationSec = summary.totalTime.TotalSeconds;
            record.messages.Clear();
            foreach (var step in report.steps)
                foreach (var message in step.messages)
                {
                    if (message.type != LogType.Error && message.type != LogType.Exception && message.type != LogType.Assert)
                        continue;
                    if (record.messages.Count >= BuildJobStore.MaxMessages)
                        break;
                    var text = message.content ?? string.Empty;
                    record.messages.Add(text.Length > 400 ? text.Substring(0, 400) + "..." : text);
                }

            var state = summary.result switch
            {
                BuildResult.Succeeded => BuildJobStore.Succeeded,
                BuildResult.Cancelled => BuildJobStore.Cancelled,
                _ => record.cancelRequested ? BuildJobStore.Cancelled : BuildJobStore.Failed,
            };
            BuildJobStore.Complete(record, state, state == BuildJobStore.Succeeded ? string.Empty : $"Build {summary.result}.");
        }
    }

    // a platform switch reloads the domain mid build and drops the call above: settle the record from what is on disk
    [InitializeOnLoad]
    static class BuildJobWatcher
    {
        const double PollSeconds = 2;
        const double IdleSeconds = 15;
        const double QueuedSeconds = 120;

        static double _nextPoll;
        static double _idleSince = -1;

        static BuildJobWatcher()
        {
            BuildJobStore.ProjectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            EditorApplication.update += Poll;
            // a reload between build-start and the build itself (e.g. the one after the previous build) must not lose the job
            var pending = BuildJobStore.Read();
            if (pending != null && pending.state == BuildJobStore.Queued && !pending.cancelRequested)
                BuildJobRunner.Schedule(pending.jobId, BuildJobRunner.PlanOf(pending));
        }

        static void Poll()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now < _nextPoll)
                return;
            _nextPoll = now + PollSeconds;

            var record = BuildJobStore.Read();
            if (record == null || record.IsTerminal || BuildJobRunner.Executing || BuildPipeline.isBuildingPlayer)
            {
                _idleSince = -1;
                return;
            }
            if (record.state == BuildJobStore.Queued)
            {
                _idleSince = -1;
                var queued = TimeSpan.FromTicks(Math.Max(0, DateTime.UtcNow.Ticks - record.startedTicks)).TotalSeconds;
                if (queued > QueuedSeconds)
                    BuildJobStore.Complete(record, BuildJobStore.Interrupted, "Queued for over 2 minutes and never started (the Editor was busy). Call build-start again.");
                return;
            }
            if (_idleSince < 0)
                _idleSince = now;
            if (now - _idleSince < IdleSeconds)
                return;

            _idleSince = -1;
            var output = record.outputPath;
            var written = File.Exists(output)
                ? File.GetLastWriteTimeUtc(output).Ticks >= record.startedTicks
                : Directory.Exists(output) && Directory.GetLastWriteTimeUtc(output).Ticks >= record.startedTicks;
            if (written && !record.cancelRequested)
            {
                record.result = nameof(BuildResult.Succeeded);
                BuildJobStore.Complete(record, BuildJobStore.Succeeded, "Finished across a domain reload; no build report was available.");
            }
            else
            {
                BuildJobStore.Complete(record, BuildJobStore.Interrupted, "The build never reported back (domain reload or Editor restart) and wrote no output.");
            }
        }
    }

    public sealed class BuildCancelHook : IProcessSceneWithReport
    {
        public int callbackOrder => int.MinValue;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null)
                return;
            var record = BuildJobStore.Read();
            if (record is { cancelRequested: true, state: BuildJobStore.Running })
                throw new BuildFailedException("Cancelled by build-cancel.");
        }
    }
}
