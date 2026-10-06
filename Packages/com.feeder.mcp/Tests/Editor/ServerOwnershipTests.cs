#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests
{
    public sealed class ServerOwnershipTests
    {
        static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ProjA", "Library", "mcp-server"));
        static readonly string OtherRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ProjB", "Library", "mcp-server"));

        static string Exe(string root) => Path.Combine(root, "win-x64", "matrix-server.exe");

        [Test]
        public void Classify_ExeUnderOwnRoot_IsOurs()
        {
            Assert.AreEqual(ServerOwner.Ours, ServerOwnership.Classify("matrix-server", Exe(Root), Root));
        }

        [Test]
        public void Classify_ExeUnderOtherProject_IsOtherProject()
        {
            Assert.AreEqual(ServerOwner.OtherProject, ServerOwnership.Classify("matrix-server", Exe(OtherRoot), Root));
        }

        [Test]
        public void Classify_DifferentProcessName_IsNotMcp()
        {
            Assert.AreEqual(ServerOwner.NotMcp, ServerOwnership.Classify("node", @"C:\node.exe", Root));
        }

        [Test]
        public void Classify_LegacyExeNameUnderOwnRoot_IsOurs()
        {
            var legacy = Path.Combine(Root, "win-x64", "gamedev-mcp-server.exe");
            Assert.AreEqual(ServerOwner.Ours, ServerOwnership.Classify("gamedev-mcp-server", legacy, Root));
        }

        [Test]
        public void Classify_LegacyExeNameUnderOtherProject_IsOtherProject()
        {
            var legacy = Path.Combine(OtherRoot, "win-x64", "gamedev-mcp-server.exe");
            Assert.AreEqual(ServerOwner.OtherProject, ServerOwnership.Classify("gamedev-mcp-server", legacy, Root));
        }

        [Test]
        public void Classify_UnreadablePath_IsUnknown()
        {
            Assert.AreEqual(ServerOwner.Unknown, ServerOwnership.Classify("matrix-server", null, Root));
            Assert.AreEqual(ServerOwner.Unknown, ServerOwnership.Classify(null, Exe(Root), Root));
        }

        [Test]
        public void Classify_StagingSibling_IsNotOurs()
        {
            var staging = Root + "-staging-abc";
            Assert.AreEqual(ServerOwner.OtherProject, ServerOwnership.Classify("matrix-server", Exe(staging), Root));
        }

        [Test]
        public void Classify_RootWithTrailingSeparator_StillMatches()
        {
            Assert.AreEqual(ServerOwner.Ours, ServerOwnership.Classify("matrix-server", Exe(Root), Root + Path.DirectorySeparatorChar));
        }

        [Test]
        public void ClassifyProcess_CurrentEditorProcess_IsNotMcp()
        {
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            Assert.AreEqual(ServerOwner.NotMcp, ServerOwnership.ClassifyProcess(self, Root));
        }

        [Test]
        public void ClassifyProcess_MissingPid_IsUnknown()
        {
            Assert.AreEqual(ServerOwner.Unknown, ServerOwnership.ClassifyProcess(-5, Root));
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
        [TestCase(ServerOwner.NotMcp)]
        [TestCase(ServerOwner.Unknown)]
        public void CanAdopt_NotOurs_False(ServerOwner owner)
        {
            Assert.IsFalse(ServerOwnership.CanAdopt(Record(), Start, owner, 27212));
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
