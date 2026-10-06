#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Feeder.ReflectorNet.Utils;
using Debug = UnityEngine.Debug;

namespace Feeder.MCP.Editor.HyperTesting
{
    // creates, launches, refreshes and stops the clone editors; every entry point refuses unless the user enabled the mode
    public static class HyperClonePool
    {
        // a warm asset import worker holds 1.5-2 GB of commit
        const long ImportWorkerCommitMb = 2048;
        const long MonoHeapRestartMb = 2048;

        static readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        static readonly string[] LibraryExcludedDirs = { "Bee", "MatrixHyperTesting", "Search" };
        static readonly string[] LibraryExcludedFiles = { "*-lock", "EditorInstance.json", "ilpp.pid", "LastSceneManagerSetup.txt" };

        sealed class Context
        {
            public string OriginRoot = string.Empty;
            public string OriginDataPath = string.Empty;
            public string UnityPath = string.Empty;
            public string PackageVersion = string.Empty;
            public string ClonesRoot = string.Empty;
            public long MonoHeapMb;
            public int VramMb;
            public bool OriginPlaying;
            public HyperTestingConfig Config = new HyperTestingConfig();
        }

        static Task<Context?> CaptureAsync() => MainThread.Instance.RunAsync<Context?>(() =>
        {
            if (!HyperTestingActivation.IsEnabled)
                return null;
            var context = new Context
            {
                OriginRoot = HyperTestingPaths.ProjectRoot,
                OriginDataPath = HyperTestingPaths.DataPath,
                UnityPath = EditorApplication.applicationPath,
                PackageVersion = HyperTestingActivation.PackageVersion,
                MonoHeapMb = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576,
                VramMb = SystemInfo.graphicsMemorySize,
                OriginPlaying = EditorApplication.isPlaying,
                Config = HyperTestingConfig.Load(),
            };
            context.ClonesRoot = context.Config.ResolvedClonesRoot(context.OriginRoot);
            return context;
        });

        public static async Task<bool> IsAvailableAsync() => await CaptureAsync() != null;

        static string Refusal =>
            File.Exists(Path.Combine(HyperTestingPaths.ProjectRoot, HyperTestingPaths.MarkerFileName))
                ? "This editor is itself a Hyper Testing clone. Pool commands run only in the original project."
                : $"{HyperTestingActivation.ModeName} is OFF for this project. Only the user can enable it in Tools > Feeder > Hyper Testing (Experimental). Do not try to enable it; ask the user.";

        public static async Task<string> UpAsync(int count)
        {
            var ctx = await CaptureAsync();
            if (ctx == null)
                return Refusal;
            count = Mathf.Clamp(count, 1, Math.Max(1, ctx.Config.maxClones));

            await _gate.WaitAsync();
            try
            {
                var log = new StringBuilder();
                var baseline = TakeOriginBaseline(ctx);
                if (baseline.Length > 0)
                    log.AppendLine("WARN " + baseline);
                foreach (var warning in LoadWarnings(HyperProcess.Snapshot(), HyperPoolState.Load()))
                    log.AppendLine(warning);

                for (var index = 1; index <= count; index++)
                {
                    var line = await Task.Run(() => BringUp(ctx, index));
                    log.AppendLine(line);
                    if (line.StartsWith("STOP"))
                        break;
                }
                HyperRamMonitor.EnsureRunning(ctx.Config.ramSampleIntervalSec, ctx.Config.ramReserveMb);
                await WriteAgentFilesAsync();
                var agentWarning = HyperTestingWorkspace.AgentFileWarning();
                if (agentWarning.Length > 0)
                    log.AppendLine("WARN " + agentWarning);
                return log.ToString() + "\n" + StatusText(ctx, HyperOriginGuard.Check(ctx.OriginRoot, 0));
            }
            finally
            {
                _gate.Release();
            }
        }

        // a card with at most vramOneCloneMaxMb of video memory carries one clone at a time
        internal static bool VramCapReached(HyperTestingConfig config, int vramMb, int liveClones) =>
            config.vramOneCloneMaxMb > 0 && vramMb > 0 && vramMb <= config.vramOneCloneMaxMb && liveClones >= 1;

        static string TakeOriginBaseline(Context ctx)
        {
            // the baseline of the first up of this run keeps holding while any clone is alive
            if (HyperPoolState.Load().clones.Any(c => LivePid(c) > 0))
                return string.Empty;
            return HyperOriginGuard.TakeBaseline(ctx.OriginRoot);
        }

        static string BringUp(Context ctx, int index)
        {
            var state = HyperPoolState.Load();
            var entry = state.Find(index);
            if (entry != null && LivePid(entry) > 0)
                return $"hc{index}: already running (pid {LivePid(entry)}, port {entry.port})";

            if (VramCapReached(ctx.Config, ctx.VramMb, state.clones.Count(c => LivePid(c) > 0)))
                return $"STOP hc{index}: VRAM gate: the GPU has {ctx.VramMb} MB (at most {ctx.Config.vramOneCloneMaxMb} MB means one clone) and a clone is already running. Two clones plus the original reset the driver on a 4 GB card.";

            var gate = CommitGate(ctx);
            if (gate != null)
            {
                HyperJournal.AppendAuto(HyperTestingPaths.RamNotes, $"commit gate stopped starting hc{index}: {gate}");
                return $"STOP hc{index}: {gate}";
            }

            var root = Path.Combine(ctx.ClonesRoot, $"hc{index}");
            var watch = Stopwatch.StartNew();
            var port = entry?.port > 0 && !IsPortBusy(entry.port) ? entry.port : PickPort(ctx.Config.basePort + index, state);
            try
            {
                var created = PrepareClone(ctx, index, root, port);
                var prepared = watch.Elapsed.TotalSeconds;
                if (File.Exists(HyperTestingPaths.StatusFile(root)))
                    File.Delete(HyperTestingPaths.StatusFile(root));
                var pid = Launch(ctx, root);
                entry ??= new HyperPoolEntry { index = index };
                if (!state.clones.Contains(entry))
                    state.clones.Add(entry);
                entry.port = port;
                entry.pid = pid;
                entry.root = root;
                entry.state = "starting";
                state.Save();
                HyperRamMonitor.EnsureRunning(ctx.Config.ramSampleIntervalSec, ctx.Config.ramReserveMb);

                var current = entry;
                var ready = WaitUntil(root, () => LivePid(current), TimeSpan.FromMinutes(created ? 20 : 8), s => s.pid == LivePid(current) && s.runnerActive && !s.isCompiling && !s.isUpdating, out var status, out var why);
                var livePid = LivePid(entry);
                if (livePid > 0)
                    entry.pid = livePid;
                entry.state = ready ? "ready" : "unhealthy";
                entry.note = ready ? string.Empty : why;
                var fresh = HyperPoolState.Load();
                fresh.clones.RemoveAll(c => c.index == index);
                fresh.clones.Add(entry);
                fresh.Save();

                var memory = livePid > 0 ? HyperSystemMemory.ReadEditor(livePid, HyperProcess.Snapshot()) : default;
                HyperRamMonitor.RecordCloneCommit(memory.TotalCommitMb);
                var summary = $"hc{index}: {(ready ? "READY" : "NOT READY - " + why)} port {port} pid {entry.pid}, prepare {prepared:0}s{(created ? " (new clone, Library copied)" : "")}, total {watch.Elapsed.TotalSeconds:0}s, RAM {memory.Editor.WorkingSetMb} MB, commit {memory.TotalCommitMb} MB, {memory.Workers} import workers";
                HyperJournal.AppendAuto(HyperTestingPaths.CloneIssues, (ready ? "info: " : "PROBLEM: ") + summary);
                return summary;
            }
            catch (Exception ex)
            {
                HyperJournal.AppendAuto(HyperTestingPaths.CloneIssues, $"PROBLEM: hc{index} failed to come up: {ex.Message}");
                return $"STOP hc{index}: {ex.Message}";
            }
        }

        // the editors died of the commit limit, not of missing RAM: gate on commit headroom
        static string? CommitGate(Context ctx)
        {
            var config = ctx.Config;
            var origin = HyperSystemMemory.ReadEditor(HyperProcess.CurrentPid, HyperProcess.Snapshot());
            var budget = CloneBudgetMb(config, HyperRamMonitor.PeakCloneCommitMb(), origin.Editor.CommitMb);
            return RefuseCommit(HyperSystemMemory.Read(), budget, config.ramReserveMb, ctx.MonoHeapMb);
        }

        // a clone is budgeted at the larger of what clones reached before (+20 %, workers included) and what the original editor holds now plus its workers
        internal static long CloneBudgetMb(HyperTestingConfig config, long measuredPeakCommitMb, long originEditorCommitMb) =>
            Math.Max(Math.Max(config.ramPerCloneEstimateMb, originEditorCommitMb) + Math.Max(0, config.cloneImportWorkers) * ImportWorkerCommitMb,
                (long)(measuredPeakCommitMb * 1.2));

        internal static string? RefuseCommit(SystemMemory system, long budgetMb, int reserveMb, long monoHeapMb)
        {
            if (system.CommitKnown)
            {
                return system.CommitChargeMb + budgetMb <= system.CommitLimitMb - reserveMb
                    ? null
                    : $"commit is too tight: {system.CommitChargeMb} MB of {system.CommitLimitMb} MB committed, one more editor needs about {budgetMb} MB and {reserveMb} MB must stay free. {Fixes(monoHeapMb)}";
            }
            if (!system.PhysicalKnown)
                return null;
            return system.AvailableMb - reserveMb < budgetMb
                ? $"only {system.AvailableMb} MB free (commit unreadable); keeping {reserveMb} MB reserve leaves less than the {budgetMb} MB one more editor needs. {Fixes(monoHeapMb)}"
                : null;
        }

        static string Fixes(long monoHeapMb) =>
            "Fix one of: set a larger fixed pagefile (Windows > Advanced system settings > Performance > Virtual memory); close the heavy scene in the original editor; " +
            $"restart the original editor when its Mono heap is above {MonoHeapRestartMb} MB (now {monoHeapMb} MB); stop java.exe/Gradle and other Unity builds.";

        // not refusals: a Gradle daemon or another Unity editor takes commit the gate cannot give back
        static List<string> LoadWarnings(IReadOnlyList<HyperProcess.Entry> snapshot, HyperPoolState state)
        {
            var warnings = new List<string>();
            foreach (var java in snapshot.Where(e => string.Equals(e.Exe, "java.exe", StringComparison.OrdinalIgnoreCase)))
            {
                var commit = HyperProcess.ReadMemory(java.Pid).CommitMb;
                if (commit >= 512)
                    warnings.Add($"WARN java.exe (pid {java.Pid}, Gradle?) holds {commit} MB of commit; stop the Gradle daemon before starting clones.");
            }

            var ours = new HashSet<int> { HyperProcess.CurrentPid };
            foreach (var entry in state.clones)
                ours.Add(LivePid(entry, snapshot));
            foreach (var editor in snapshot.Where(e => HyperProcess.IsEditorExe(e.Exe) && !ours.Contains(e.Pid) && !ours.Contains(e.ParentPid)))
            {
                var commandLine = HyperProcess.CommandLine(editor.Pid);
                if (commandLine == null || HyperProcess.IsImportWorker(commandLine))
                    continue;
                var project = HyperProcess.ProjectPathOf(commandLine);
                var kind = commandLine.IndexOf("-batchmode", StringComparison.OrdinalIgnoreCase) >= 0 ? "a batch-mode Unity (a build?)" : "another Unity editor";
                warnings.Add($"WARN {kind} is running ({(project.Length > 0 ? project : "pid " + editor.Pid)}, {HyperProcess.ReadMemory(editor.Pid).CommitMb} MB commit); it counts against the commit limit.");
            }
            return warnings;
        }

        static bool PrepareClone(Context ctx, int index, string root, int port)
        {
            var isNew = !Directory.Exists(Path.Combine(root, "Library"));
            Directory.CreateDirectory(root);

            Junction(Path.Combine(root, "Assets"), Path.Combine(ctx.OriginRoot, "Assets"));

            var packages = Path.Combine(root, "Packages");
            Directory.CreateDirectory(packages);
            foreach (var dir in Directory.GetDirectories(Path.Combine(ctx.OriginRoot, "Packages")))
                Junction(Path.Combine(packages, Path.GetFileName(dir)), dir);
            SyncPackageFiles(ctx.OriginRoot, root);

            Robocopy(Path.Combine(ctx.OriginRoot, "ProjectSettings"), Path.Combine(root, "ProjectSettings"), Array.Empty<string>(), Array.Empty<string>());
            if (isNew)
                Robocopy(Path.Combine(ctx.OriginRoot, "Library"), Path.Combine(root, "Library"), LibraryExcludedDirs, LibraryExcludedFiles);
            else
                RemoveCloneLibraryExtras(root);

            WriteCloneBridgeConfig(ctx.OriginRoot, root, port);
            DisableCloneSearchIndex(root);

            var marker = new HyperCloneMarker
            {
                index = index,
                port = port,
                originRoot = ctx.OriginRoot,
                originDataPath = ctx.OriginDataPath,
                cloneRoot = root,
                createdUtc = DateTime.UtcNow.ToString("o"),
                packageVersion = ctx.PackageVersion,
                playTargetFrameRate = ctx.Config.playTargetFrameRate,
                playLowestQuality = ctx.Config.playLowestQuality,
                isolatePlayerPrefs = ctx.Config.isolatePlayerPrefs,
                closeSceneViews = ctx.Config.closeSceneViews,
                importWorkers = Math.Max(0, ctx.Config.cloneImportWorkers),
            };
            File.WriteAllText(Path.Combine(root, HyperTestingPaths.MarkerFileName), JsonUtility.ToJson(marker, true));
            Directory.CreateDirectory(Path.Combine(root, "Logs"));
            return isNew;
        }

        // an existing clone still holds what the exclusions keep out of a new one: the search index and the scene the original had open
        static void RemoveCloneLibraryExtras(string root)
        {
            var library = Path.Combine(root, "Library");
            var search = Path.Combine(library, "Search");
            if (Directory.Exists(search) && FindReparsePoint(search) == null)
                Directory.Delete(search, true);
            var scenes = Path.Combine(library, "LastSceneManagerSetup.txt");
            if (File.Exists(scenes))
                File.Delete(scenes);
        }

        // the 500 MB memory-mapped search index is of no use to a clone: no indexing at editor start, and a minimal index if one is built anyway
        static void DisableCloneSearchIndex(string root)
        {
            var userSettings = Path.Combine(root, "UserSettings");
            Directory.CreateDirectory(userSettings);
            var settings = Path.Combine(userSettings, "Search.settings");
            File.WriteAllText(settings, SearchSettingsWithoutStartupIndex(File.Exists(settings) ? File.ReadAllText(settings) : null));
            var index = Path.Combine(userSettings, "Search.index");
            File.WriteAllText(index, SearchIndexWithoutOptions(File.Exists(index) ? File.ReadAllText(index) : null));
        }

        internal static string SearchSettingsWithoutStartupIndex(string? existing)
        {
            const string setting = "indexOnEditorStartup = false";
            if (string.IsNullOrWhiteSpace(existing))
                return setting + "\nversion = 1\n";
            if (Regex.IsMatch(existing, @"^[ \t]*indexOnEditorStartup[ \t]*=", RegexOptions.Multiline))
                return Regex.Replace(existing, @"^([ \t]*)indexOnEditorStartup[ \t]*=[ \t]*\S+", "${1}" + setting, RegexOptions.Multiline);
            return existing.TrimEnd() + "\n" + setting + "\n";
        }

        internal static string SearchIndexWithoutOptions(string? existing)
        {
            if (string.IsNullOrWhiteSpace(existing))
            {
                return "{\n    \"name\": \"Assets\",\n    \"roots\": [\"Assets\"],\n    \"includes\": [],\n    \"excludes\": [\"Assets/Temp/\", \"Assets/External/\"],\n" +
                    "    \"options\": {\n        \"types\": false,\n        \"properties\": false,\n        \"extended\": false,\n        \"dependencies\": false\n    },\n    \"baseScore\": 999\n}\n";
            }
            return Regex.Replace(existing, "\"(types|properties|extended|dependencies)\"(\\s*):(\\s*)true", "\"$1\"$2:$3false");
        }

        static void Junction(string link, string target)
        {
            if (Directory.Exists(link))
            {
                if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) == 0)
                    throw new IOException($"'{link}' exists as a real folder, not a junction. Remove it by hand after checking it holds nothing you need.");
                return;
            }
            var code = Run("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"", out var output);
            if (code != 0 || !Directory.Exists(link))
                throw new IOException($"mklink /J failed for '{link}': {output.Trim()}");
        }

        static bool SyncPackageFiles(string originRoot, string cloneRoot)
        {
            var changed = false;
            var originPackages = Path.Combine(originRoot, "Packages");
            foreach (var file in Directory.GetFiles(originPackages))
            {
                var text = Regex.Replace(File.ReadAllText(file), "\"file:([^\"]+)\"", m =>
                {
                    var value = m.Groups[1].Value;
                    if (Path.IsPathRooted(value))
                        return m.Value;
                    return $"\"file:{Path.GetFullPath(Path.Combine(originPackages, value)).Replace('\\', '/')}\"";
                });
                var destination = Path.Combine(cloneRoot, "Packages", Path.GetFileName(file));
                if (File.Exists(destination) && File.ReadAllText(destination) == text)
                    continue;
                File.WriteAllText(destination, text);
                changed = true;
            }
            return changed;
        }

        static void WriteCloneBridgeConfig(string originRoot, string cloneRoot, int port)
        {
            var source = Path.Combine(originRoot, "UserSettings", "Feeder-MCP-Config.json");
            var userSettings = Path.Combine(cloneRoot, "UserSettings");
            Directory.CreateDirectory(userSettings);
            var json = File.Exists(source) ? File.ReadAllText(source) : "{}";
            json = Regex.Replace(json, "\"host\"\\s*:\\s*\"[^\"]*\"", $"\"host\": \"http://localhost:{port}\"");
            json = Regex.Replace(json, "\"token\"\\s*:\\s*\"[^\"]*\"", $"\"token\": \"{Guid.NewGuid():N}\"");
            if (!json.Contains("\"host\""))
                json = json.TrimEnd().TrimEnd('}') + $",\"host\": \"http://localhost:{port}\"}}";
            File.WriteAllText(Path.Combine(userSettings, "Feeder-MCP-Config.json"), json);
        }

        static int Launch(Context ctx, string root)
        {
            var args = $"-projectPath \"{root}\" -logFile \"{Path.Combine(root, "Logs", "hyper-editor.log")}\" -silent-crashes";
            if (ctx.Config.jobWorkerCount > 0)
                args += $" -job-worker-count {ctx.Config.jobWorkerCount}";
            var info = new ProcessStartInfo(ctx.UnityPath, args) { UseShellExecute = false, WorkingDirectory = root };
            foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("UNITY_MCP_", StringComparison.OrdinalIgnoreCase)).ToList())
                info.Environment.Remove(key);
            var process = Process.Start(info) ?? throw new IOException("Unity.exe did not start");
            if (ctx.Config.belowNormalPriority)
            {
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
                catch (Exception ex) { Debug.LogWarning($"{HyperTestingActivation.LogPrefix} could not lower priority: {ex.Message}"); }
            }
            return process.Id;
        }

        public static async Task<string> RefreshAsync()
        {
            var ctx = await CaptureAsync();
            if (ctx == null)
                return Refusal;
            await _gate.WaitAsync();
            try
            {
                var live = HyperPoolState.Load().clones.Where(c => LivePid(c) > 0).OrderBy(c => c.index).ToList();
                if (live.Count == 0)
                    return "No clone is running. Run action 'up' first.";

                using var compileSlots = new SemaphoreSlim(Math.Max(1, ctx.Config.maxParallelCompiles));
                var results = await Task.WhenAll(live.Select(entry => Task.Run(async () =>
                {
                    await compileSlots.WaitAsync();
                    try { return RefreshOne(ctx, entry); }
                    finally { compileSlots.Release(); }
                })));
                return string.Join("\n", results);
            }
            finally
            {
                _gate.Release();
            }
        }

        static string RefreshOne(Context ctx, HyperPoolEntry entry)
        {
            var watch = Stopwatch.StartNew();
            var settingsCode = Robocopy(Path.Combine(ctx.OriginRoot, "ProjectSettings"), Path.Combine(entry.root, "ProjectSettings"), Array.Empty<string>(), Array.Empty<string>());
            var packagesChanged = SyncPackageFiles(ctx.OriginRoot, entry.root);
            var id = SendCommand(entry.root, "refresh");
            var done = WaitUntil(entry.root, () => LivePid(entry), TimeSpan.FromSeconds(ctx.Config.compileTimeoutSec), s => s.lastCommandId == id, out var status, out var why);
            var notes = new List<string>();
            if ((settingsCode & 1) != 0)
                notes.Add("ProjectSettings re-synced from the original (restart the clone if a changed setting needs an editor restart)");
            if (packagesChanged)
                notes.Add("Packages manifest changed (package resolve ran)");
            var line = done
                ? $"hc{entry.index}: {(status!.compileFailed ? "COMPILE ERRORS" : "compiled clean")} in {watch.Elapsed.TotalSeconds:0}s, RAM {status.workingSetMb} MB, commit {status.commitMb + status.workersCommitMb} MB {string.Join("; ", notes)}"
                : $"hc{entry.index}: refresh NOT confirmed after {watch.Elapsed.TotalSeconds:0}s - {why}";
            if (!done || status!.compileFailed)
                HyperJournal.AppendAuto(HyperTestingPaths.CloneIssues, "PROBLEM: " + line);
            return line;
        }

        public static async Task<string> DownAsync(bool purge)
        {
            var ctx = await CaptureAsync();
            if (ctx == null)
                return Refusal;
            await _gate.WaitAsync();
            try
            {
                var state = HyperPoolState.Load();
                var lines = await Task.Run(() =>
                {
                    var created = ReportCreatedAssets(state, out var confirmed);
                    var stopped = state.clones.OrderBy(c => c.index).Select(entry => StopOne(entry, purge)).ToList();
                    stopped.AddRange(created);
                    stopped.AddRange(ReportOriginChanges(ctx, confirmed));
                    return stopped;
                });
                if (purge)
                    state.clones.Clear();
                else
                    state.clones.ForEach(c => { c.pid = 0; c.state = "stopped"; });
                state.Save();
                HyperTestingWorkspace.WriteAgentFiles(state);
                return string.Join("\n", lines);
            }
            finally
            {
                _gate.Release();
            }
        }

        // what the clones themselves saw being created in the original, read before they quit; a stopped clone left its status behind and is skipped
        static List<string> ReportCreatedAssets(HyperPoolState state, out HashSet<string> confirmed)
        {
            confirmed = new HashSet<string>();
            var lines = new List<string>();
            foreach (var entry in state.clones.OrderBy(c => c.index))
            {
                var status = LivePid(entry) > 0 ? ReadStatus(entry.root) : null;
                if (status == null || status.createdAssetCount == 0)
                    continue;
                confirmed.UnionWith(status.createdAssets);
                var text = $"CRITICAL: hc{entry.index} created {status.createdAssetCount} asset(s) in the ORIGINAL project through the Assets junction: {string.Join("; ", status.createdAssets)}";
                lines.Add(text);
                HyperJournal.AppendAuto(HyperTestingPaths.CloneIssues, text);
            }
            return lines;
        }

        // what git sees in the original that was not there when the clones started and that no clone reported:
        // the orchestrator adds files too, so this is a warning to compare with its own edits, not proof
        static List<string> ReportOriginChanges(Context ctx, HashSet<string> confirmed)
        {
            var result = HyperOriginGuard.Check(ctx.OriginRoot, 0);
            var unreported = UnreportedPaths(result.NewPaths, confirmed);
            if (unreported.Count == 0)
                return result.Note.Length > 0 ? new List<string> { "NOTE: " + result.Note } : new List<string>();
            var text = OriginChangeWarning(unreported);
            HyperJournal.AppendAuto(HyperTestingPaths.CloneIssues, text);
            return new List<string> { text };
        }

        static string OriginChangeWarning(IReadOnlyList<string> unreported) =>
            $"WARNING: {unreported.Count} new untracked path(s) in the ORIGINAL project since up that no clone reported: {string.Join("; ", unreported.Take(20))}. " +
            "Clones are read-only, so unless you or your tools added them, something running in a clone wrote them.";

        internal static List<string> UnreportedPaths(IEnumerable<string> newPaths, ICollection<string> confirmed) =>
            newPaths.Where(path => !IsConfirmed(path, confirmed)).ToList();

        // a path the clone reported, the .meta of one, or the .meta of a folder that holds one
        internal static bool IsConfirmed(string path, ICollection<string> confirmed)
        {
            var unityPath = UnityPath(path);
            if (confirmed.Contains(unityPath))
                return true;
            if (!unityPath.EndsWith(".meta", StringComparison.Ordinal))
                return false;
            var asset = unityPath.Substring(0, unityPath.Length - ".meta".Length);
            return confirmed.Contains(asset) || confirmed.Any(c => c.StartsWith(asset + "/", StringComparison.Ordinal));
        }

        // git paths are relative to the repository, which can sit above the Unity project
        static string UnityPath(string gitPath)
        {
            foreach (var root in new[] { "Assets/", "Packages/" })
            {
                if (gitPath.StartsWith(root, StringComparison.Ordinal))
                    return gitPath;
                var at = gitPath.IndexOf("/" + root, StringComparison.Ordinal);
                if (at >= 0)
                    return gitPath.Substring(at + 1);
            }
            return gitPath;
        }

        static string StopOne(HyperPoolEntry entry, bool purge)
        {
            var result = $"hc{entry.index}: ";
            var pid = LivePid(entry);
            if (pid > 0)
            {
                SendCommand(entry.root, "quit");
                if (!WaitForExit(pid, TimeSpan.FromSeconds(45)))
                {
                    try { using var p = Process.GetProcessById(pid); p.Kill(); }
                    catch (Exception) { }
                    result += "killed after 45 s (did not quit on command)";
                    HyperJournal.AppendAuto(HyperTestingPaths.CloneIssues, $"PROBLEM: hc{entry.index} ignored the quit command and was killed");
                }
                else
                {
                    result += "stopped";
                }
            }
            else
            {
                result += "was not running";
            }

            if (purge && Directory.Exists(entry.root))
                result += "; " + SafeDeleteClone(entry.root);
            return result;
        }

        // junctions are unlinked one by one first: a recursive delete through a junction would wipe the original project
        static string SafeDeleteClone(string root)
        {
            var links = new List<string> { Path.Combine(root, "Assets") };
            var packages = Path.Combine(root, "Packages");
            if (Directory.Exists(packages))
                links.AddRange(Directory.GetDirectories(packages));

            foreach (var link in links.Where(Directory.Exists))
            {
                if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) == 0)
                    continue;
                Run("cmd.exe", $"/c rmdir \"{link}\"", out _);
                if (Directory.Exists(link))
                    return $"purge ABORTED: could not unlink junction '{link}'";
            }

            var leftover = FindReparsePoint(root);
            if (leftover != null)
                return $"purge ABORTED: '{leftover}' is still a link; delete the clone by hand";

            Directory.Delete(root, true);
            return "folder deleted";
        }

        static string? FindReparsePoint(string folder)
        {
            foreach (var dir in Directory.GetDirectories(folder))
            {
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                    return dir;
                var nested = FindReparsePoint(dir);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        public static async Task<string> StatusAsync()
        {
            var ctx = await CaptureAsync();
            if (ctx == null)
                return Refusal;
            // git runs off the calling thread: the window asks from the main thread
            var git = await Task.Run(() => HyperOriginGuard.Check(ctx.OriginRoot, 120));
            return StatusText(ctx, git) + JobText();
        }

        static Task<string>? _job;
        static string _jobName = string.Empty;
        static DateTime _jobStartedUtc;

        static string LastJobFile => Path.Combine(Path.GetDirectoryName(HyperTestingPaths.PoolFile)!, "last-job.txt");

        // long pool work runs in the background so an MCP call never hits a client timeout; poll with status
        public static string StartJob(string name, Func<Task<string>> work)
        {
            if (_job != null && !_job.IsCompleted)
                return $"Job '{_jobName}' is still running ({(DateTime.UtcNow - _jobStartedUtc).TotalSeconds:0}s). Poll action 'status' until it finishes.";
            _jobName = name;
            _jobStartedUtc = DateTime.UtcNow;
            var lastJobFile = LastJobFile;
            _job = Task.Run(async () =>
            {
                string result;
                try { result = await work(); }
                catch (Exception ex) { result = "FAILED: " + ex; }
                Directory.CreateDirectory(Path.GetDirectoryName(lastJobFile)!);
                File.WriteAllText(lastJobFile, $"{name} finished {DateTime.Now:yyyy-MM-dd HH:mm:ss} after {(DateTime.UtcNow - _jobStartedUtc).TotalSeconds:0}s" + Environment.NewLine + result);
                return result;
            });
            return $"Started job '{name}'. It runs in the background (clone creation can take minutes). Poll action 'status' every 30-60 s.";
        }

        static string JobText()
        {
            if (_job != null && !_job.IsCompleted)
                return $"{Environment.NewLine}Job '{_jobName}' RUNNING for {(DateTime.UtcNow - _jobStartedUtc).TotalSeconds:0}s.";
            return File.Exists(LastJobFile) ? Environment.NewLine + "Last job: " + File.ReadAllText(LastJobFile) : string.Empty;
        }

        static string StatusText(Context ctx, HyperOriginGuard.Result git)
        {
            var config = ctx.Config;
            var snapshot = HyperProcess.Snapshot();
            var system = HyperSystemMemory.Read();
            var origin = HyperSystemMemory.ReadEditor(HyperProcess.CurrentPid, snapshot);
            var text = new StringBuilder();
            text.AppendLine($"{HyperTestingActivation.ModeName}: ON (package {ctx.PackageVersion}), clones folder {ctx.ClonesRoot}");
            text.AppendLine($"System: {system.AvailableMb} MB RAM free of {system.TotalMb} MB ({system.LoadPercent}% used), commit {system.CommitChargeMb} MB of {system.CommitLimitMb} MB ({system.CommitFreeMb} MB left), reserve {config.ramReserveMb} MB, GPU {ctx.VramMb} MB");
            text.AppendLine($"Original editor: {origin.Editor.WorkingSetMb} MB RAM, {origin.Editor.CommitMb} MB commit, {origin.Workers} import workers ({origin.WorkersCommitMb} MB commit), Mono heap {ctx.MonoHeapMb} MB{(ctx.OriginPlaying ? ", IN PLAY MODE" : string.Empty)}");
            var state = HyperPoolState.Load();
            if (state.clones.Count == 0)
                text.AppendLine("No clones yet. Action 'up' creates and starts them.");
            var warnings = new List<string>();
            var reported = new HashSet<string>();
            foreach (var entry in state.clones.OrderBy(c => c.index))
            {
                var pid = LivePid(entry, snapshot);
                var alive = pid > 0;
                var status = ReadStatus(entry.root);
                var age = status == null ? -1 : DateTimeOffset.UtcNow.ToUnixTimeSeconds() - status.writtenUnix;
                text.Append($"hc{entry.index}: {(alive ? "running" : "not running")}, port {entry.port}, pid {(alive ? pid : entry.pid)}, mcp http://localhost:{entry.port}/mcp, agent {HyperTestingPaths.AgentPrefix}{entry.index}");
                if (alive)
                {
                    var memory = HyperSystemMemory.ReadEditor(pid, snapshot);
                    text.Append($", RAM {memory.Editor.WorkingSetMb} MB (peak {memory.Editor.PeakWorkingSetMb} MB), commit {memory.Editor.CommitMb} MB + {memory.Workers} import workers {memory.WorkersCommitMb} MB");
                }
                if (status != null && alive)
                {
                    text.Append($", runner {(status.runnerActive ? "active" : "INACTIVE " + status.runnerProblem)}, playing {status.isPlaying}, compiling {status.isCompiling}, compile errors {status.compileFailed}, status age {age}s");
                    if (status.blockedWrites > 0)
                        text.Append($", BLOCKED WRITES {status.blockedWrites}");
                    if (ctx.OriginPlaying && (status.isUpdating || status.isPlaying))
                        warnings.Add($"WARNING: the original editor is in play mode while hc{entry.index} is {(status.isPlaying ? "playing" : "importing")}; on a 4 GB GPU that reset the driver. Stop play mode in the original.");
                    if (status.createdAssetCount > 0)
                    {
                        reported.UnionWith(status.createdAssets);
                        warnings.Add($"CRITICAL: hc{entry.index} created {status.createdAssetCount} asset(s) in the ORIGINAL project through the Assets junction: {string.Join("; ", status.createdAssets)}");
                    }
                }
                if (!string.IsNullOrEmpty(entry.note))
                    text.Append($", note: {entry.note}");
                text.AppendLine();
            }
            var unreported = UnreportedPaths(git.NewPaths, reported);
            if (unreported.Count > 0)
                warnings.Add(OriginChangeWarning(unreported));
            foreach (var warning in warnings)
                text.AppendLine(warning);
            return text.ToString();
        }

        static string SendCommand(string root, string command)
        {
            var id = Guid.NewGuid().ToString("N").Substring(0, 12);
            Directory.CreateDirectory(HyperTestingPaths.ChannelFolder(root));
            WriteAtomic(HyperTestingPaths.CommandFile(root), JsonUtility.ToJson(new HyperCloneCommand { id = id, command = command }));
            return id;
        }

        internal static void WriteAtomic(string path, string text)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path);
        }

        internal static HyperCloneStatus? ReadStatus(string root)
        {
            try
            {
                var path = HyperTestingPaths.StatusFile(root);
                return File.Exists(path) ? JsonUtility.FromJson<HyperCloneStatus>(File.ReadAllText(path)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static bool WaitUntil(string root, Func<int> livePid, TimeSpan timeout, Func<HyperCloneStatus, bool> condition, out HyperCloneStatus? status, out string why)
        {
            var deadline = DateTime.UtcNow + timeout;
            status = null;
            while (DateTime.UtcNow < deadline)
            {
                if (livePid() <= 0)
                {
                    why = "the clone editor process exited (see its Logs/hyper-editor.log)";
                    return false;
                }
                status = ReadStatus(root);
                if (status != null && !status.runnerActive && !string.IsNullOrEmpty(status.runnerProblem))
                {
                    why = "runner refused: " + status.runnerProblem;
                    return false;
                }
                if (status != null && condition(status))
                {
                    why = string.Empty;
                    return true;
                }
                Thread.Sleep(2000);
            }
            why = $"timed out after {timeout.TotalSeconds:0}s (an unfocused editor may be throttled; status age matters)";
            return false;
        }

        static bool WaitForExit(int pid, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (HyperProcess.IsAlive(pid))
            {
                if (DateTime.UtcNow >= deadline)
                    return false;
                Thread.Sleep(500);
            }
            return true;
        }

        // the stored pid is the editor that was launched; a changed pid is found again by the -projectPath of the clone
        internal static int LivePid(HyperPoolEntry entry, IReadOnlyList<HyperProcess.Entry>? snapshot = null)
        {
            if (entry.pid > 0 && !string.IsNullOrEmpty(entry.root) && HyperProcess.IsEditorOf(entry.pid, entry.root))
                return entry.pid;
            return string.IsNullOrEmpty(entry.root) ? 0 : HyperProcess.FindEditor(entry.root, snapshot ?? HyperProcess.Snapshot());
        }

        internal static bool IsCloneProcess(int pid)
        {
            if (!HyperProcess.IsAlive(pid))
                return false;
            var image = HyperProcess.ImagePath(pid);
            return image != null && HyperProcess.IsEditorExe(Path.GetFileName(image));
        }

        static int PickPort(int preferred, HyperPoolState state)
        {
            var taken = new HashSet<int>(state.clones.Select(c => c.port));
            for (var port = preferred; port < preferred + 500; port += 10)
            {
                if (!taken.Contains(port) && !IsPortBusy(port))
                    return port;
            }
            throw new IOException($"no free port near {preferred}");
        }

        static bool IsPortBusy(int port) =>
            IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == port);

        static int Robocopy(string source, string destination, string[] excludedDirs, string[] excludedFiles)
        {
            source = Path.GetFullPath(source);
            destination = Path.GetFullPath(destination);
            var args = new StringBuilder($"\"{source}\" \"{destination}\" /MIR /MT:16 /R:1 /W:1 /NFL /NDL /NP /NJH /NJS");
            if (excludedDirs.Length > 0)
                args.Append(" /XD ").Append(string.Join(" ", excludedDirs.Select(d => $"\"{Path.Combine(source, d)}\"")));
            if (excludedFiles.Length > 0)
                args.Append(" /XF ").Append(string.Join(" ", excludedFiles.Select(f => $"\"{f}\"")));
            var code = Run("robocopy.exe", args.ToString(), out var output);
            if (code >= 8)
                throw new IOException($"robocopy '{source}' -> '{destination}' failed with code {code}: {output.Trim()}");
            return code;
        }

        static int Run(string file, string args, out string output)
        {
            var info = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            output = stdout.Result + stderr.Result;
            return process.ExitCode;
        }

        static Task WriteAgentFilesAsync()
        {
            var state = HyperPoolState.Load();
            return MainThread.Instance.RunAsync(() => HyperTestingWorkspace.WriteAgentFiles(state));
        }
    }
}
