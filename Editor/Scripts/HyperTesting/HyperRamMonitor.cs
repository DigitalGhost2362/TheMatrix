#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Feeder.MCP.Editor.HyperTesting
{
    // samples system and per-clone RAM into Journal/ram-samples.csv while any clone runs
    [InitializeOnLoad]
    public static class HyperRamMonitor
    {
        internal const string CsvHeader =
            "time,totalMb,availableMb,loadPercent,commitChargeMb,commitLimitMb,commitFreeMb,originWsMb,originCommitMb,originWorkers,originWorkersCommitMb," +
            "clones,clonesWsMb,clonesCommitMb,clonesWorkersCommitMb,perClone(ws/commit/workers/workersCommit)";

        static readonly object _fileLock = new object();
        static Timer? _timer;
        static int _reserveMb;
        static DateTime _lastAlertUtc = DateTime.MinValue;

        static string PeakFile => Path.Combine(Path.GetDirectoryName(HyperTestingPaths.PoolFile)!, "peak-clone-commit-mb.txt");

        static HyperRamMonitor()
        {
            // import workers load every editor assembly; the sampler belongs to the editor alone
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            if (!HyperTestingActivation.IsEnabled || File.Exists(Path.Combine(HyperTestingPaths.ProjectRoot, HyperTestingPaths.MarkerFileName)))
                return;
            if (HyperPoolState.Load().clones.Any(c => HyperClonePool.IsCloneProcess(c.pid)))
            {
                var config = HyperTestingConfig.Load();
                EnsureRunning(config.ramSampleIntervalSec, config.ramReserveMb);
            }
        }

        public static void EnsureRunning(int intervalSec, int reserveMb)
        {
            _reserveMb = reserveMb;
            if (_timer != null)
                return;
            var period = TimeSpan.FromSeconds(Math.Max(5, intervalSec));
            _timer = new Timer(_ => SampleSafe(), null, TimeSpan.Zero, period);
        }

        static void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        static void SampleSafe()
        {
            try
            {
                Sample();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{HyperTestingActivation.LogPrefix} RAM sample failed: {ex.Message}");
            }
        }

        // highest commit one clone (editor plus workers) reached on this machine, kept across sessions to size the next clone
        internal static long PeakCloneCommitMb()
        {
            try
            {
                lock (_fileLock)
                    return File.Exists(PeakFile) && long.TryParse(File.ReadAllText(PeakFile).Trim(), out var peak) ? peak : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        internal static void RecordCloneCommit(long commitMb)
        {
            try
            {
                lock (_fileLock)
                {
                    if (commitMb <= PeakCloneCommitMb())
                        return;
                    Directory.CreateDirectory(Path.GetDirectoryName(PeakFile)!);
                    File.WriteAllText(PeakFile, commitMb.ToString());
                }
            }
            catch (Exception)
            {
            }
        }

        public static string Sample()
        {
            var snapshot = HyperProcess.Snapshot();
            var live = HyperPoolState.Load().clones.OrderBy(c => c.index)
                .Select(c => (c.index, pid: HyperClonePool.LivePid(c, snapshot)))
                .Where(c => c.pid > 0)
                .Select(c => (c.index, memory: HyperSystemMemory.ReadEditor(c.pid, snapshot)))
                .ToList();
            if (live.Count == 0)
            {
                Stop();
                return "no clone running";
            }

            var system = HyperSystemMemory.Read();
            var origin = HyperSystemMemory.ReadEditor(HyperProcess.CurrentPid, snapshot);
            var perClone = string.Join(";", live.Select(c => $"hc{c.index}={c.memory.Editor.WorkingSetMb}/{c.memory.Editor.CommitMb}/{c.memory.Workers}/{c.memory.WorkersCommitMb}"));
            var clonesCommit = live.Sum(c => c.memory.TotalCommitMb);
            foreach (var clone in live)
                RecordCloneCommit(clone.memory.TotalCommitMb);

            var line = string.Join(",", new object[]
            {
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), system.TotalMb, system.AvailableMb, system.LoadPercent,
                system.CommitChargeMb, system.CommitLimitMb, system.CommitFreeMb,
                origin.Editor.WorkingSetMb, origin.Editor.CommitMb, origin.Workers, origin.WorkersCommitMb,
                live.Count, live.Sum(c => c.memory.Editor.WorkingSetMb), live.Sum(c => c.memory.Editor.CommitMb), live.Sum(c => c.memory.WorkersCommitMb),
                perClone,
            });
            AppendCsv(line);

            var alerts = new List<string>();
            if (system.PhysicalKnown && system.AvailableMb < _reserveMb)
                alerts.Add($"LOW RAM: {system.AvailableMb} MB free (reserve {_reserveMb} MB)");
            if (system.CommitKnown && system.CommitFreeMb < _reserveMb)
                alerts.Add($"LOW COMMIT: {system.CommitFreeMb} MB of commit left (limit {system.CommitLimitMb} MB, charge {system.CommitChargeMb} MB, reserve {_reserveMb} MB)");
            if (alerts.Count > 0 && DateTime.UtcNow - _lastAlertUtc > TimeSpan.FromMinutes(5))
            {
                _lastAlertUtc = DateTime.UtcNow;
                var alert = $"{string.Join("; ", alerts)}, {live.Count} clones using {clonesCommit} MB commit incl. workers [{perClone}], original {origin.TotalCommitMb} MB commit. Stop a clone before starting anything heavy.";
                Debug.LogWarning($"{HyperTestingActivation.LogPrefix} {alert}");
                HyperJournal.AppendAuto(HyperTestingPaths.RamNotes, alert);
            }
            return line;
        }

        // a changed column layout moves the old file aside instead of mixing rows
        static void AppendCsv(string line)
        {
            lock (_fileLock)
            {
                Directory.CreateDirectory(HyperTestingPaths.Journal);
                var path = HyperTestingPaths.RamCsv;
                if (File.Exists(path) && FirstLine(path) != CsvHeader)
                    File.Move(path, Path.Combine(HyperTestingPaths.Journal, $"ram-samples-{DateTime.Now:yyyyMMdd-HHmmss}.old.csv"));
                if (!File.Exists(path))
                    File.WriteAllText(path, CsvHeader + "\n");
                File.AppendAllText(path, line + "\n");
            }
        }

        static string FirstLine(string path)
        {
            using var reader = new StreamReader(path);
            return reader.ReadLine() ?? string.Empty;
        }
    }
}
