#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests
{
    public sealed class ServerOwnershipTests
    {
        static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ProjA", "Library", "feeder-matrix"));
        static readonly string OtherRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ProjB", "Library", "feeder-matrix"));
        // older packages and other tools install into this folder
        static readonly string SharedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ProjA", "Library", "mcp-server"));

        static string Exe(string root) => Path.Combine(root, "win-x64", "matrix-server.exe");

        string _project = string.Empty;
        Func<string, string> _exeIdentity = ServerOwnership.ExeIdentity;

        [SetUp]
        public void SetUp()
        {
            _project = Path.Combine(Path.GetTempPath(), "matrix-ownership-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_project);
            // the test executables are text files: their content is their identity
            _exeIdentity = ServerOwnership.ExeIdentity;
            ServerOwnership.ExeIdentity = File.ReadAllText;
        }

        [TearDown]
        public void TearDown()
        {
            ServerOwnership.ExeIdentity = _exeIdentity;
            if (Directory.Exists(_project))
                Directory.Delete(_project, true);
        }

        [Test]
        public void Classify_ExeUnderOwnRoot_IsOurs()
        {
            Assert.AreEqual(ServerOwner.Ours, ServerOwnership.Classify(Exe(Root), Root));
        }

        [Test]
        public void Classify_ExeUnderOtherProject_IsOtherProject()
        {
            Assert.AreEqual(ServerOwner.OtherProject, ServerOwnership.Classify(Exe(OtherRoot), Root));
        }

        [Test]
        public void Classify_UnrelatedExe_IsForeign()
        {
            Assert.AreEqual(ServerOwner.Foreign, ServerOwnership.Classify(@"C:\node.exe", Root));
        }

        [Test]
        public void Classify_ExeUnderSharedOldFolder_IsForeignEvenWithOurExeName()
        {
            Assert.AreEqual(ServerOwner.Foreign, ServerOwnership.Classify(Exe(SharedRoot), Root));
            Assert.AreEqual(ServerOwner.Foreign, ServerOwnership.Classify(Path.Combine(SharedRoot, "win-x64", "other-server.exe"), Root));
        }

        [Test]
        public void Classify_UnreadablePath_IsUnknown()
        {
            Assert.AreEqual(ServerOwner.Unknown, ServerOwnership.Classify(null, Root));
            Assert.AreEqual(ServerOwner.Unknown, ServerOwnership.Classify(string.Empty, Root));
        }

        [Test]
        public void Classify_StagingSibling_IsNotOurs()
        {
            var staging = Root + "-staging-abc";
            Assert.AreEqual(ServerOwner.Foreign, ServerOwnership.Classify(Exe(staging), Root));
        }

        [Test]
        public void Classify_RootWithTrailingSeparator_StillMatches()
        {
            Assert.AreEqual(ServerOwner.Ours, ServerOwnership.Classify(Exe(Root), Root + Path.DirectorySeparatorChar));
        }

        [Test]
        public void ClassifyProcess_CurrentEditorProcess_IsNeverOurs()
        {
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            Assert.AreNotEqual(ServerOwner.Ours, ServerOwnership.ClassifyProcess(self, Root));
        }

        [Test]
        public void ClassifyProcess_DecidedByPathNotByName()
        {
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            var exe = ServerOwnership.ReadExePath(self);
            Assume.That(exe, Is.Not.Null, "the editor's own executable path is unreadable here");
            Assert.AreEqual(ServerOwner.Ours, ServerOwnership.ClassifyProcess(self, Path.GetDirectoryName(exe!)!));
        }

        [Test]
        public void ClassifyProcess_MissingPid_IsUnknown()
        {
            Assert.AreEqual(ServerOwner.Unknown, ServerOwnership.ClassifyProcess(-5, Root));
        }

        [Test]
        public void KillProcessesRunning_FileNothingRuns_KillsNothing()
        {
            var exe = Path.Combine(_project, "matrix-test-" + Guid.NewGuid().ToString("N") + ".exe");
            CollectionAssert.IsEmpty(ServerOwnership.KillProcessesRunning(exe));
        }

        [Test]
        public void KillProcessesRunning_SameNameInOtherFolder_StopsOnlyTheExactFile()
        {
            var ping = Path.Combine(Environment.SystemDirectory, "PING.EXE");
            Assume.That(File.Exists(ping), "PING.EXE is not available here");
            var running = Path.Combine(Directory.CreateDirectory(Path.Combine(_project, "a")).FullName, "same-name.exe");
            var other = Path.Combine(Directory.CreateDirectory(Path.Combine(_project, "b")).FullName, "same-name.exe");
            File.Copy(ping, running);
            File.Copy(ping, other);

            using var process = Process.Start(new ProcessStartInfo(running, "-t 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;
            try
            {
                CollectionAssert.IsEmpty(ServerOwnership.KillProcessesRunning(other));
                Assert.IsFalse(process.HasExited, "a process running another file of the same name must survive");

                CollectionAssert.AreEqual(new[] { process.Id }, ServerOwnership.KillProcessesRunning(running));
                Assert.IsTrue(process.WaitForExit(5000));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
        }

        [Test]
        public void DescribeOwner_ReturnsProjectFolderName()
        {
            Assert.AreEqual("ProjB", ServerOwnership.DescribeOwner(Exe(OtherRoot)));
            Assert.AreEqual("another process", ServerOwnership.DescribeOwner(null));
        }

        static readonly DateTime Start = new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);

        static ServerOwnerRecord Record() => new ServerOwnerRecord { pid = 42, startTimeUtcTicks = Start.Ticks, port = 27212, exePath = "x" };

        [Test]
        public void CanAdopt_AllConditionsMatch_True()
        {
            Assert.IsTrue(ServerOwnership.CanAdopt(Record(), Start, ServerOwner.Ours, 27212));
        }

        [Test]
        public void CanAdopt_ReusedPidWithDifferentStartTime_False()
        {
            Assert.IsFalse(ServerOwnership.CanAdopt(Record(), Start.AddSeconds(1), ServerOwner.Ours, 27212));
            Assert.IsFalse(ServerOwnership.CanAdopt(Record(), null, ServerOwner.Ours, 27212));
        }

        [Test]
        public void CanAdopt_DifferentPort_False()
        {
            Assert.IsFalse(ServerOwnership.CanAdopt(Record(), Start, ServerOwner.Ours, 27213));
        }

        [TestCase(ServerOwner.OtherProject)]
        [TestCase(ServerOwner.Foreign)]
        [TestCase(ServerOwner.Unknown)]
        public void CanAdopt_NotOurs_False(ServerOwner owner)
        {
            Assert.IsFalse(ServerOwnership.CanAdopt(Record(), Start, owner, 27212));
        }

        [Test]
        public void CanAdopt_ServerInSharedOldFolder_False()
        {
            Assert.IsFalse(ServerOwnership.CanAdopt(Record(), Start, ServerOwnership.Classify(Exe(SharedRoot), Root), 27212));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("{not json")]
        [TestCase("{\"pid\":0}")]
        public void ParseRecord_BrokenOrEmpty_ReturnsNull(string? json)
        {
            Assert.IsNull(ServerOwnership.ParseRecord(json));
        }

        [Test]
        public void ParseRecord_RoundTrips()
        {
            var parsed = ServerOwnership.ParseRecord(UnityEngine.JsonUtility.ToJson(Record()));
            Assert.IsNotNull(parsed);
            Assert.AreEqual(42, parsed!.pid);
            Assert.AreEqual(Start.Ticks, parsed.startTimeUtcTicks);
            Assert.AreEqual(27212, parsed.port);
        }

        [Test]
        public void OwnerFile_LivesInOwnTempFolder()
        {
            StringAssert.EndsWith(Path.Combine("Temp", "feeder-matrix", "server-owner.json"), ServerOwnership.OwnerFilePath);
        }

        // "other-*.exe" carries a foreign identity, every other executable carries ours
        string InstallFolder(params string[] files)
        {
            var folder = Path.Combine(_project, "Library", ServerOwnership.LegacyFolderName, "win-x64");
            Directory.CreateDirectory(folder);
            foreach (var file in files)
            {
                var content = !file.EndsWith(".exe", StringComparison.Ordinal) ? "content of " + file
                    : file.StartsWith("other-", StringComparison.Ordinal) ? "other-server.dll"
                    : ServerOwnership.LegacyExeIdentity;
                File.WriteAllText(Path.Combine(folder, file), content);
            }
            return folder;
        }

        static Dictionary<string, string> Snapshot(string folder) => Directory
            .GetFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(file => file, File.ReadAllText);

        [Test]
        public void RemoveLegacyInstall_OurOldInstall_RemovesItsFilesAndFolder()
        {
            var folder = InstallFolder("legacy-server.exe", ServerOwnership.LegacyMarkerFileName, "web.config", "version");

            var actions = ServerOwnership.RemoveLegacyInstall(folder);

            Assert.IsFalse(Directory.Exists(folder));
            Assert.IsTrue(Directory.Exists(Path.GetDirectoryName(folder)), "the shared parent folder is never removed");
            Assert.AreEqual(5, actions.Count, string.Join("\n", actions));
        }

        [Test]
        public void RemoveLegacyInstall_OurFilesNextToAnUnknownFile_LeavesTheWholeFolder()
        {
            var folder = InstallFolder("legacy-server.exe", ServerOwnership.LegacyMarkerFileName, "web.config", "version", "notes.txt");
            var before = Snapshot(folder);

            var actions = ServerOwnership.RemoveLegacyInstall(folder);

            CollectionAssert.IsEmpty(actions);
            CollectionAssert.AreEquivalent(before, Snapshot(folder));
        }

        [Test]
        public void RemoveLegacyInstall_OurMarkerWithAnotherServersExe_LeavesTheWholeFolder()
        {
            var folder = InstallFolder("other-server.exe", ServerOwnership.LegacyMarkerFileName, "web.config", "version");
            var before = Snapshot(folder);

            var actions = ServerOwnership.RemoveLegacyInstall(folder);

            CollectionAssert.IsEmpty(actions);
            CollectionAssert.AreEquivalent(before, Snapshot(folder));
        }

        [Test]
        public void RemoveLegacyInstall_OurFilesNextToASubFolder_LeavesTheWholeFolder()
        {
            var folder = InstallFolder("legacy-server.exe", ServerOwnership.LegacyMarkerFileName, "web.config", "version");
            Directory.CreateDirectory(Path.Combine(folder, "logs"));
            File.WriteAllText(Path.Combine(folder, "logs", "server.log"), "log");
            var before = Snapshot(folder);

            var actions = ServerOwnership.RemoveLegacyInstall(folder);

            CollectionAssert.IsEmpty(actions);
            CollectionAssert.AreEquivalent(before, Snapshot(folder));
        }

        [Test]
        public void RemoveLegacyInstall_ForeignInstall_LeftByteForByte()
        {
            var folder = InstallFolder("other-server.exe", "other-server.staticwebassets.endpoints.json", "server.json", "version", "web.config");
            Directory.CreateDirectory(Path.Combine(folder, "logs"));
            File.WriteAllText(Path.Combine(folder, "logs", "server.log"), "log");
            var before = Snapshot(folder);

            var actions = ServerOwnership.RemoveLegacyInstall(folder);

            CollectionAssert.IsEmpty(actions);
            CollectionAssert.AreEquivalent(before, Snapshot(folder));
        }

        [Test]
        public void RemoveLegacyInstall_FolderWithBothMarkers_LeftAlone()
        {
            var folder = InstallFolder("other-server.exe", ServerOwnership.LegacyMarkerFileName, "other-server.staticwebassets.endpoints.json", "version", "web.config");
            var before = Snapshot(folder);

            var actions = ServerOwnership.RemoveLegacyInstall(folder);

            CollectionAssert.IsEmpty(actions);
            CollectionAssert.AreEquivalent(before, Snapshot(folder));
        }

        [Test]
        public void RemoveLegacyInstall_NoFolder_NoOp()
        {
            var folder = Path.Combine(_project, "Library", ServerOwnership.LegacyFolderName, "win-x64");

            CollectionAssert.IsEmpty(ServerOwnership.RemoveLegacyInstall(folder));
            Assert.IsFalse(Directory.Exists(Path.Combine(_project, "Library")));
        }

        [Test]
        public void PickFreePort_SkipsBusyAndReserved()
        {
            var busy = new HashSet<int> { 27213 };
            var reserved = new HashSet<int> { 27214 };
            Assert.AreEqual(27215, ServerOwnership.PickFreePort(27212, busy, reserved));
        }

        [Test]
        public void PickFreePort_WrapsAroundRange()
        {
            Assert.AreEqual(20000, ServerOwnership.PickFreePort(29999, new HashSet<int>(), new HashSet<int>()));
        }

        [Test]
        public void PickFreePort_CurrentOutsideRange_StartsAtMin()
        {
            Assert.AreEqual(20000, ServerOwnership.PickFreePort(8080, new HashSet<int>(), new HashSet<int>()));
        }

        [Test]
        public void PickFreePort_NothingFree_ReturnsMinusOne()
        {
            var busy = new HashSet<int> { 20000, 20001, 20002 };
            Assert.AreEqual(-1, ServerOwnership.PickFreePort(20001, busy, new HashSet<int>(), 20000, 20002));
        }

        static readonly DateTime Now = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void RestartPolicy_BacksOffThenGivesUp()
        {
            var policy = new ServerRestartPolicy();
            var shortLife = TimeSpan.FromSeconds(3);
            var expected = new[] { 1, 2, 5, 10, 30 };
            for (var i = 0; i < expected.Length; i++)
                Assert.AreEqual(TimeSpan.FromSeconds(expected[i]), policy.NextDelay(Now.AddSeconds(i), shortLife));
            Assert.IsNull(policy.NextDelay(Now.AddSeconds(10), shortLife));
        }

        [Test]
        public void RestartPolicy_OldRestartsFallOutOfWindow()
        {
            var policy = new ServerRestartPolicy();
            for (var i = 0; i < ServerRestartPolicy.MaxAttempts; i++)
                policy.NextDelay(Now, TimeSpan.FromSeconds(1));
            Assert.AreEqual(TimeSpan.FromSeconds(1), policy.NextDelay(Now.AddMinutes(6), TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void RestartPolicy_ResetsAfterHealthyLifetime()
        {
            var policy = new ServerRestartPolicy();
            for (var i = 0; i < ServerRestartPolicy.MaxAttempts; i++)
                policy.NextDelay(Now, TimeSpan.FromSeconds(1));
            Assert.AreEqual(TimeSpan.FromSeconds(1), policy.NextDelay(Now.AddSeconds(30), TimeSpan.FromMinutes(3)));
        }

        [Test]
        public void RestartPolicy_ExplicitReset_StartsOver()
        {
            var policy = new ServerRestartPolicy();
            policy.NextDelay(Now, null);
            policy.NextDelay(Now, null);
            policy.Reset();
            Assert.AreEqual(0, policy.Attempts);
            Assert.AreEqual(TimeSpan.FromSeconds(1), policy.NextDelay(Now, null));
        }
    }
}
