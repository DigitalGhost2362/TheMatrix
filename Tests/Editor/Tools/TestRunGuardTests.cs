#nullable enable

using System;
using Feeder.MCP.Editor.API.TestRunner;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class TestRunGuardTests
    {
        static readonly long Now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc).Ticks;

        static long Ago(TimeSpan span) => Now - span.Ticks;

        [Test]
        public void EmptySlot_IsFree()
            => Assert.AreEqual(TestRunGuard.SlotState.Free, TestRunGuard.Evaluate("", 0, Now, false, false, false));

        [Test]
        public void ExecutingRun_IsBusy()
            => Assert.AreEqual(TestRunGuard.SlotState.Busy,
                TestRunGuard.Evaluate("r1", Ago(TimeSpan.FromMinutes(20)), Now, false, true, false));

        [Test]
        public void ExecutingRun_PastTimeout_IsStale()
            => Assert.AreEqual(TestRunGuard.SlotState.Stale,
                TestRunGuard.Evaluate("r1", Ago(TimeSpan.FromMinutes(61)), Now, false, true, false));

        [Test]
        public void DeferredRun_IsBusyThenStale()
        {
            Assert.AreEqual(TestRunGuard.SlotState.Busy,
                TestRunGuard.Evaluate("r1", Ago(TimeSpan.FromMinutes(5)), Now, true, false, true));
            Assert.AreEqual(TestRunGuard.SlotState.Stale,
                TestRunGuard.Evaluate("r1", Ago(TimeSpan.FromMinutes(11)), Now, true, false, false));
        }

        [Test]
        public void StartingRun_NotExecuting_GoesStaleWhenEditorIdle()
        {
            Assert.AreEqual(TestRunGuard.SlotState.Busy,
                TestRunGuard.Evaluate("r1", Ago(TimeSpan.FromSeconds(30)), Now, false, false, false));
            Assert.AreEqual(TestRunGuard.SlotState.Stale,
                TestRunGuard.Evaluate("r1", Ago(TimeSpan.FromMinutes(3)), Now, false, false, false));
            Assert.AreEqual(TestRunGuard.SlotState.Busy,
                TestRunGuard.Evaluate("r1", Ago(TimeSpan.FromMinutes(3)), Now, false, false, true));
        }

        [Test]
        public void SlotWithoutStartTime_IsStale()
            => Assert.AreEqual(TestRunGuard.SlotState.Stale, TestRunGuard.Evaluate("r1", 0, Now, false, true, false));

        [Test]
        public void Refusal_StartsWithStructuredPrefix()
        {
            var running = TestRunGuard.FormatRefusal("r1", TimeSpan.FromSeconds(42), false, true);
            StringAssert.StartsWith($"[tests_running retry_after_ms={TestRunGuard.ExecutingRetryMs}] ", running);
            StringAssert.Contains("r1", running);
            StringAssert.Contains("42 s ago", running);

            var starting = TestRunGuard.FormatRefusal("r2", TimeSpan.Zero, false, false);
            StringAssert.StartsWith($"[tests_running retry_after_ms={TestRunGuard.StartingRetryMs}] ", starting);
        }
    }
}
