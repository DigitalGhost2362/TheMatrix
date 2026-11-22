#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Feeder.MCP.Editor.HyperTesting;
using NUnit.Framework;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests
{
    public sealed class HyperTestingTests
    {
        static void RequireWindows() => Assume.That(Application.platform, Is.EqualTo(RuntimePlatform.WindowsEditor));

        static Process StartShortLivedProcess() =>
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 4 127.0.0.1 >nul") { UseShellExecute = false, CreateNoWindow = true })!;

        #region Process facts

        [Test]
        public void ReadMemory_RunningEditor_NeverReadsZero()
        {
            RequireWindows();
            var memory = HyperProcess.ReadMemory(HyperProcess.CurrentPid);
            Assert.Greater(memory.WorkingSetMb, 0, "working set");
            Assert.Greater(memory.CommitMb, 0, "commit");
            Assert.GreaterOrEqual(memory.PeakWorkingSetMb, memory.WorkingSetMb, "peak working set");
        }

        [Test]
        public void ReadEditor_RunningEditor_CountsItsCommit()
        {
            RequireWindows();
            var memory = HyperSystemMemory.ReadEditor(HyperProcess.CurrentPid, HyperProcess.Snapshot());
            Assert.Greater(memory.Editor.WorkingSetMb, 0);
            Assert.GreaterOrEqual(memory.TotalCommitMb, memory.Editor.CommitMb);
        }

        [Test]
        public void IsAlive_ProcessStartedMomentAgo_IsTrueRightAway()
        {
            RequireWindows();
            using var child = StartShortLivedProcess();
            Assert.IsTrue(HyperProcess.IsAlive(child.Id));
            var image = HyperProcess.ImagePath(child.Id);
            Assert.AreEqual("cmd.exe", Path.GetFileName(image ?? string.Empty).ToLowerInvariant());
            child.Kill();
        }

        [Test]
        public void IsAlive_ExitedProcess_IsFalse()
        {
            RequireWindows();
            using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { UseShellExecute = false, CreateNoWindow = true })!;
            child.WaitForExit();
            Assert.IsFalse(HyperProcess.IsAlive(child.Id));
        }

        [TestCase(0)]
        [TestCase(-5)]
        [TestCase(999999)]
        public void IsAlive_NoSuchProcess_IsFalse(int pid)
        {
            RequireWindows();
            Assert.IsFalse(HyperProcess.IsAlive(pid));
        }

        [Test]
        public void ImportWorkers_ProcessWithoutWorkers_IsEmpty()
        {
            RequireWindows();
            using var child = StartShortLivedProcess();
            Assert.IsEmpty(HyperProcess.ImportWorkers(child.Id, HyperProcess.Snapshot()));
            child.Kill();
        }

        [Test]
        public void FindEditor_ThisProject_IsThisProcess()
        {
            RequireWindows();
            var commandLine = HyperProcess.CommandLine(HyperProcess.CurrentPid);
            Assume.That(commandLine, Does.Contain("-project").IgnoreCase);
            Assert.AreEqual(HyperProcess.CurrentPid, HyperProcess.FindEditor(HyperTestingPaths.ProjectRoot, HyperProcess.Snapshot()));
            Assert.IsTrue(HyperProcess.IsEditorOf(HyperProcess.CurrentPid, HyperTestingPaths.ProjectRoot + "/"));
            Assert.IsFalse(HyperProcess.IsEditorOf(HyperProcess.CurrentPid, Path.Combine(HyperTestingPaths.ProjectRoot, "NotThisProject")));
        }

        [Test]
        public void SystemRead_ReportsCommitCharge()
        {
            RequireWindows();
            var system = HyperSystemMemory.Read();
            Assert.IsTrue(system.CommitKnown);
            Assert.Greater(system.CommitChargeMb, 0);
            Assert.Greater(system.CommitLimitMb, system.CommitChargeMb);
            Assert.Greater(system.AvailableMb, 0);
        }

        [TestCase("\"C:\\Program Files\\Unity\\Editor\\Unity.exe\" -projectpath C:\\Work\\game -useHub", "C:\\Work\\game")]
        [TestCase("Unity.exe -projectPath \"C:\\Work\\my game\" -logFile x.log", "C:\\Work\\my game")]
        [TestCase("Unity.exe -batchMode", "")]
        public void ProjectPathOf_ReadsTheArgument(string commandLine, string expected)
        {
            Assert.AreEqual(expected, HyperProcess.ProjectPathOf(commandLine));
        }

        [Test]
        public void IsImportWorker_RecognizesTheWorkerNameArgument()
        {
            const string worker = "\"Unity.exe\" \"-adb2\" \"-batchMode\" \"-name\" \"AssetImportWorker3\" \"-projectPath\" \"C:/Work/game\" \"-name\" \"AssetImport\"";
            Assert.IsTrue(HyperProcess.IsImportWorker(worker));
            Assert.IsFalse(HyperProcess.IsImportWorker("\"Unity.exe\" -projectpath C:\\Work\\game"));
            Assert.IsFalse(HyperProcess.IsImportWorker(null));
        }

        [Test]
        public void NormalizePath_IgnoresCaseSlashesAndTrailingSeparator()
        {
            Assert.AreEqual(HyperProcess.NormalizePath("C:/Work/Game/"), HyperProcess.NormalizePath("c:\\work\\game"));
            Assert.AreEqual(string.Empty, HyperProcess.NormalizePath(" "));
        }

        #endregion

        #region Memory gate

        static HyperTestingConfig Config(int estimateMb = 4096, int workers = 0) => new HyperTestingConfig { ramPerCloneEstimateMb = estimateMb, cloneImportWorkers = workers };

        [Test]
        public void CloneBudget_TakesTheLargestOfEstimatePeakAndOriginCommit()
        {
            Assert.AreEqual(4096, HyperClonePool.CloneBudgetMb(Config(), 0, 0));
            Assert.AreEqual(6000, HyperClonePool.CloneBudgetMb(Config(), 5000, 3000));
            Assert.AreEqual(9200, HyperClonePool.CloneBudgetMb(Config(), 5200, 9200));
        }

        [Test]
        public void CloneBudget_AddsWhatEachImportWorkerHolds()
        {
            Assert.AreEqual(4096 + 2 * 2048, HyperClonePool.CloneBudgetMb(Config(workers: 2), 0, 0));
            Assert.AreEqual(9000 + 2 * 2048, HyperClonePool.CloneBudgetMb(Config(workers: 2), 0, 9000));
            Assert.AreEqual(4096, HyperClonePool.CloneBudgetMb(Config(workers: -3), 0, 0));
        }

        [Test]
        public void CloneBudget_MeasuredPeakAlreadyHoldsItsWorkers()
        {
            Assert.AreEqual(10800, HyperClonePool.CloneBudgetMb(Config(workers: 2), 9000, 0));
        }

        [Test]
        public void RefuseCommit_EnoughHeadroom_Passes()
        {
            var system = new SystemMemory(32000, 8000, 75, 30000, 60000);
            Assert.IsNull(HyperClonePool.RefuseCommit(system, 9000, 4096, 1000));
        }

        [Test]
        public void RefuseCommit_ProjectedCommitPastLimitMinusReserve_RefusesAndNamesTheFixes()
        {
            var system = new SystemMemory(32000, 8000, 75, 48000, 60000);
            var refusal = HyperClonePool.RefuseCommit(system, 9000, 4096, 3000);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("pagefile", refusal);
            StringAssert.Contains("heavy scene", refusal);
            StringAssert.Contains("Mono heap", refusal);
            StringAssert.Contains("3000", refusal);
        }

        [Test]
        public void RefuseCommit_ExactlyAtTheLimit_Passes()
        {
            var system = new SystemMemory(32000, 8000, 75, 46904, 60000);
            Assert.IsNull(HyperClonePool.RefuseCommit(system, 9000, 4096, 0));
            Assert.IsNotNull(HyperClonePool.RefuseCommit(new SystemMemory(32000, 8000, 75, 46905, 60000), 9000, 4096, 0));
        }

        [Test]
        public void RefuseCommit_CommitUnreadable_FallsBackToFreePhysicalMemory()
        {
            var unreadable = new SystemMemory(32000, 12000, 60, -1, -1);
            Assert.IsNull(HyperClonePool.RefuseCommit(unreadable, 4096, 4096, 0));
            Assert.IsNotNull(HyperClonePool.RefuseCommit(new SystemMemory(32000, 8000, 75, -1, -1), 4096, 4096, 0));
            Assert.IsNull(HyperClonePool.RefuseCommit(SystemMemory.Unknown, 4096, 4096, 0));
        }

        [TestCase(4080, 4096, 0, false)]
        [TestCase(4080, 4096, 1, true)]
        [TestCase(4096, 4096, 2, true)]
        [TestCase(8176, 4096, 3, false)]
        [TestCase(4080, 0, 1, false)]
        [TestCase(0, 4096, 1, false)]
        public void VramCapReached_SmallCardCarriesOneCloneAtATime(int vramMb, int limitMb, int liveClones, bool expected)
        {
            var config = new HyperTestingConfig { vramOneCloneMaxMb = limitMb };
            Assert.AreEqual(expected, HyperClonePool.VramCapReached(config, vramMb, liveClones));
        }

        [Test]
        public void ConfigDefaults_AreTheSafeOnes()
        {
            var config = new HyperTestingConfig();
            Assert.AreEqual(0, config.cloneImportWorkers);
            Assert.AreEqual(4096, config.vramOneCloneMaxMb);
            Assert.AreEqual(0, new HyperCloneMarker().importWorkers);
            Assert.AreEqual(0, JsonUtility.FromJson<HyperCloneMarker>("{\"index\":1}").importWorkers);
            Assert.AreEqual(0, JsonUtility.FromJson<HyperTestingConfig>("{\"maxClones\":2}").cloneImportWorkers);
        }

        #endregion

        #region Search index

        [Test]
        public void SearchSettings_NoFile_GetsAMinimalOneWithStartupIndexOff()
        {
            var text = HyperClonePool.SearchSettingsWithoutStartupIndex(null);
            StringAssert.Contains("indexOnEditorStartup = false", text);
            Assert.AreEqual(text, HyperClonePool.SearchSettingsWithoutStartupIndex(text));
        }

        [Test]
        public void SearchSettings_ExistingFile_OnlyTheStartupLineChanges()
        {
            const string existing = "trackSelection = true\nindexOnEditorStartup = true\nversion = 1\n";
            var text = HyperClonePool.SearchSettingsWithoutStartupIndex(existing);
            Assert.AreEqual("trackSelection = true\nindexOnEditorStartup = false\nversion = 1\n", text);
        }

        [Test]
        public void SearchSettings_SettingMissing_IsAppended()
        {
            var text = HyperClonePool.SearchSettingsWithoutStartupIndex("trackSelection = true\nversion = 1\n");
            Assert.AreEqual("trackSelection = true\nversion = 1\nindexOnEditorStartup = false\n", text);
        }

        [Test]
        public void SearchIndex_ExistingOptions_AreAllSwitchedOff()
        {
            const string existing = "{\n \"name\": \"Assets\",\n \"options\": {\n  \"types\": true,\n  \"properties\": true,\n  \"extended\": false,\n  \"dependencies\": true\n },\n \"baseScore\": 999\n}";
            var text = HyperClonePool.SearchIndexWithoutOptions(existing);
            StringAssert.DoesNotContain("true", text);
            StringAssert.Contains("\"name\": \"Assets\"", text);
            StringAssert.Contains("\"baseScore\": 999", text);
        }

        [Test]
        public void SearchIndex_NoFile_GetsTheAssetsIndexWithEveryOptionOff()
        {
            var text = HyperClonePool.SearchIndexWithoutOptions(null);
            StringAssert.Contains("\"roots\": [\"Assets\"]", text);
            StringAssert.DoesNotContain("true", text);
            Assert.AreEqual(4, text.Split(new[] { "false" }, StringSplitOptions.None).Length - 1);
        }

        #endregion

        #region Origin guard

        [Test]
        public void NewUntracked_ListsOnlyUntrackedPathsMissingFromTheBaseline()
        {
            var baseline = new[] { "?? Assets/Old.cs", " M Assets/Changed.cs" };
            var current = new[] { "?? Assets/Old.cs", " M Assets/Changed.cs", " M Assets/AlsoChanged.cs", "?? Assets/Resources/GDKEditionAutoGen/GDKEdition.asset", "?? Assets/Resources/GDKEditionAutoGen.meta" };
            var created = HyperOriginGuard.NewUntracked(baseline, current);
            CollectionAssert.AreEqual(new[] { "Assets/Resources/GDKEditionAutoGen/GDKEdition.asset", "Assets/Resources/GDKEditionAutoGen.meta" }, created);
        }

        [Test]
        public void NewUntracked_NothingNew_IsEmpty()
        {
            var lines = new[] { "?? Assets/Old.cs" };
            Assert.IsEmpty(HyperOriginGuard.NewUntracked(lines, lines));
        }

        [Test]
        public void UnreportedPaths_CreationsAReportedCloneMadeAndTheirMetaFilesAreConfirmed()
        {
            var reported = new[] { "Assets/Resources/GDKEditionAutoGen/GDKEdition.asset" };
            var git = new[]
            {
                "Assets/Resources/GDKEditionAutoGen/GDKEdition.asset",
                "Assets/Resources/GDKEditionAutoGen/GDKEdition.asset.meta",
                "Assets/Resources/GDKEditionAutoGen.meta",
                "Assets/Scripts/AddedByTheOrchestrator.cs",
            };
            CollectionAssert.AreEqual(new[] { "Assets/Scripts/AddedByTheOrchestrator.cs" }, HyperClonePool.UnreportedPaths(git, reported));
        }

        [Test]
        public void UnreportedPaths_RepositoryAboveTheProject_StillMatches()
        {
            var reported = new[] { "Assets/Foo/Bar.asset" };
            var git = new[] { "Game/Assets/Foo/Bar.asset", "Game/Assets/Foo.meta", "Game/Assets/Other.cs" };
            CollectionAssert.AreEqual(new[] { "Game/Assets/Other.cs" }, HyperClonePool.UnreportedPaths(git, reported));
        }

        [Test]
        public void UnreportedPaths_NothingReported_ListsEverything()
        {
            var git = new[] { "Assets/A.cs", "Assets/A.cs.meta" };
            CollectionAssert.AreEqual(git, HyperClonePool.UnreportedPaths(git, new string[0]));
        }

        #endregion

        #region Sampler

        [Test]
        public void CsvHeader_NamesTheCommitAndWorkerColumns()
        {
            var columns = HyperRamMonitor.CsvHeader.Split(',');
            Assert.AreEqual(16, columns.Length);
            foreach (var name in new[] { "commitChargeMb", "commitLimitMb", "commitFreeMb", "originCommitMb", "originWorkers", "clonesCommitMb", "clonesWorkersCommitMb" })
                CollectionAssert.Contains(columns, name);
        }

        #endregion
    }
}
