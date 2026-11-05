#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Feeder.MCP.Editor.API;
using Feeder.MCP.Editor.API.TestRunner;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class TestRunStoreTests
    {
        string _path = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(), "feeder_store_" + Guid.NewGuid().ToString("N"), "run.json");
            TestRunStore.PathOverride = _path;
        }

        [TearDown]
        public void TearDown()
        {
            TestRunStore.PathOverride = null;
            var dir = Path.GetDirectoryName(_path);
            if (dir != null && Directory.Exists(dir))
                Directory.Delete(dir, true);
        }

        static TestSummaryData Summary(int passed, int failed) => new()
        {
            Status = failed > 0 ? TestRunStatus.Failed : TestRunStatus.Passed,
            TotalTests = passed + failed,
            PassedTests = passed,
            FailedTests = failed,
            Duration = TimeSpan.FromSeconds(2)
        };

        [Test]
        public void Read_WithoutFile_IsNull() => Assert.IsNull(TestRunStore.Read());

        [Test]
        public void Read_CorruptFile_IsNull()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, "{ not json");
            Assert.IsNull(TestRunStore.Read());
        }

        [Test]
        public void Begin_PersistsFilterAndAsync()
        {
            TestRunStore.Begin("job1", "PlayMode", new TestFilterParameters("Asm", null, "Cls", null), true);
            var record = TestRunStore.Read()!;
            Assert.AreEqual("job1", record.jobId);
            Assert.AreEqual(TestRunStore.Queued, record.state);
            Assert.IsTrue(record.isAsync);
            Assert.AreEqual("Asm", record.Filter.TestAssembly);
            Assert.AreEqual("Cls", record.Filter.TestClass);
            Assert.IsNull(record.Filter.TestNamespace);
        }

        [Test]
        public void Finish_StoresSummaryAndRows()
        {
            TestRunStore.Begin("job1", "EditMode", default, false);
            TestRunStore.SetState(TestRunStore.Running);
            TestRunStore.Finish(Summary(1, 1), new[]
            {
                new TestResultData { Name = "A.Ok", Status = TestResultStatus.Passed },
                new TestResultData { Name = "A.Bad", Status = TestResultStatus.Failed, Message = "boom" }
            });
            var record = TestRunStore.Read()!;
            Assert.AreEqual(TestRunStore.Finished, record.state);
            Assert.AreEqual(2, record.total);
            Assert.AreEqual(2, record.rows.Count);
            Assert.AreEqual("boom", record.rows[1].message);
        }

        [Test]
        public void SetState_DoesNotReopenTerminalRecord()
        {
            TestRunStore.Begin("job1", "EditMode", default, false);
            TestRunStore.Fail("x");
            TestRunStore.SetState(TestRunStore.Running);
            Assert.AreEqual(TestRunStore.Failed, TestRunStore.Read()!.state);
        }

        [Test]
        public void FormatResult_NoRecord_And_UnknownJob()
        {
            StringAssert.StartsWith("[no_run]", Tool_Tests.FormatResult(null, null, false, false));
            TestRunStore.Begin("job1", "EditMode", default, false);
            StringAssert.StartsWith("[unknown_job]", Tool_Tests.FormatResult(TestRunStore.Read(), "other", false, true));
        }

        [Test]
        public void FormatResult_Running_CarriesRetryOrLostNote()
        {
            TestRunStore.Begin("job1", "EditMode", default, true);
            TestRunStore.SetState(TestRunStore.Running);
            StringAssert.Contains($"retry_after_ms={TestRunGuard.ExecutingRetryMs}", Tool_Tests.FormatResult(TestRunStore.Read(), "job1", false, true));
            StringAssert.Contains("tests-clear", Tool_Tests.FormatResult(TestRunStore.Read(), null, false, false));
        }

        [Test]
        public void FormatResult_Finished_HidesPassingUnlessAsked()
        {
            TestRunStore.Begin("job1", "EditMode", default, false);
            TestRunStore.Finish(Summary(1, 1), new[]
            {
                new TestResultData { Name = "A.Ok", Status = TestResultStatus.Passed },
                new TestResultData { Name = "A.Bad", Status = TestResultStatus.Failed, Message = "boom" }
            });
            var text = Tool_Tests.FormatResult(TestRunStore.Read(), "job1", false, false);
            StringAssert.Contains("passed=1 failed=1", text);
            StringAssert.Contains("A.Bad", text);
            StringAssert.DoesNotContain("A.Ok", text);
            StringAssert.Contains("A.Ok", Tool_Tests.FormatResult(TestRunStore.Read(), null, true, false));
        }

        [Test]
        public void FormatList_FiltersAndGroupsByAssembly()
        {
            var tests = new List<(string Assembly, string Name)> { ("A", "N.X.One"), ("A", "N.X.Two"), ("B", "N.Y.One") };
            var text = Tool_Tests.FormatList("EditMode", tests, "one", 1);
            StringAssert.Contains("total=2 shown=1", text);
            StringAssert.Contains("assembly A: 1", text);
            StringAssert.Contains("assembly B: 1", text);
        }

        [Test]
        public void Clear_FreesStuckSlot_AndMarksRecord()
        {
            // this test may execute inside a live tests-run: its slot must be handed back untouched
            var live = TestRunGuard.Capture();
            try
            {
                TestRunGuard.Begin("stuck");
                TestRunGuard.Executing = true;
                TestRunStore.Begin("stuck", "EditMode", default, false);
                Assert.IsNotNull(TestRunGuard.CheckAndReclaim(false));

                StringAssert.StartsWith("cleared:", Tool_Tests.Clear());

                Assert.IsNull(TestRunGuard.CheckAndReclaim(false));
                Assert.AreEqual(TestRunStore.Cleared, TestRunStore.Read()!.state);
            }
            finally
            {
                TestRunGuard.Restore(live);
            }
        }

        [Test]
        public void CaptureRestore_KeepsTheLiveRunRequestId()
        {
            var live = TestRunGuard.Capture();
            var before = TestResultCollector.TestCallRequestID;
            var executing = TestRunGuard.Executing;
            try
            {
                TestRunGuard.Begin("other");
                TestRunGuard.Clear();
                Assert.AreEqual(string.Empty, TestResultCollector.TestCallRequestID);
            }
            finally
            {
                TestRunGuard.Restore(live);
            }
            Assert.AreEqual(before, TestResultCollector.TestCallRequestID);
            Assert.AreEqual(executing, TestRunGuard.Executing);
        }
    }
}
