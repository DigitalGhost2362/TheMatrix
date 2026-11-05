#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.API.TestRunner;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;

namespace Feeder.MCP.Editor.API
{
    public static partial class Tool_Tests
    {
        public const string TestsResultToolId = "tests-result";
        public const string TestsListToolId = "tests-list";
        public const string TestsClearToolId = "tests-clear";

        [AiTool
        (
            TestsResultToolId,
            Title = "Tests / Result",
            ReadOnlyHint = true,
            IdempotentHint = true,
            Enabled = true
        )]
        [AiSkillDescription("Status and result of the last 'tests-run' (job id, state, summary, failures). " +
            "Poll it after 'tests-run' with async=true, or fetch a result the client timed out on. " +
            "The record is kept in Library and survives domain reloads.")]
        [Description("Status and result of the last 'tests-run'. State is queued, deferred (waiting for a script reload), running, finished, failed or cleared. " +
            "While the run is not finished the answer carries retry_after_ms. The record survives domain reloads.")]
        public static string Result
        (
            [Description("Job id returned by 'tests-run' (optional; the last run is returned when omitted).")]
            string? jobId = null,
            [Description("Include passing tests in the list (default: false).")]
            bool includePassingTests = false
        )
        {
            return MainThread.Instance.Run(() =>
                FormatResult(TestRunStore.Read(), jobId, includePassingTests, !string.IsNullOrEmpty(TestResultCollector.TestCallRequestID)));
        }

        [AiTool
        (
            TestsListToolId,
            Title = "Tests / List",
            ReadOnlyHint = true,
            IdempotentHint = true,
            Enabled = false
        )]
        [AiSkillDescription("List the Unity tests of a mode, filtered by assembly, namespace, class or name text, with a per-assembly count.")]
        [Description("List the Unity tests of a mode, filtered by assembly, namespace, class or name text. Returns the total, a per-assembly count and the test names (up to limit).")]
        public static async Task<string> List
        (
            [Description("'" + nameof(TestMode.EditMode) + "' or '" + nameof(TestMode.PlayMode) + "'. Default: '" + nameof(TestMode.EditMode) + "'")]
            TestMode testMode = TestMode.EditMode,
            [Description("Test assembly name (optional).")]
            string? testAssembly = null,
            [Description("Test namespace (optional).")]
            string? testNamespace = null,
            [Description("Test class name (optional).")]
            string? testClass = null,
            [Description("Keep only tests whose full name contains this text, case-insensitive (optional).")]
            string? nameContains = null,
            [Description("Maximum number of test names returned (default: 200).")]
            int limit = 200
        )
        {
            return await MainThread.Instance.RunAsync(async () =>
            {
                var filter = new TestFilterParameters(testAssembly, testNamespace, testClass);
                var tests = new List<(string Assembly, string Name)>();
                var tcs = new TaskCompletionSource<bool>();
                TestRunnerApi.RetrieveTestList(testMode, root =>
                {
                    try
                    {
                        if (root != null)
                        {
                            if (filter.HasAnyFilter)
                                CountFilteredTests(root, filter, tests);
                            else
                                CollectAll(root, tests);
                        }
                        tcs.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                });
                if (await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5))) != tcs.Task)
                    return "[timeout] The test list did not arrive within 5 s; retry.";
                await tcs.Task;
                return FormatList(testMode.ToString(), tests, nameContains, limit);
            }).Unwrap();
        }

        [AiTool
        (
            TestsClearToolId,
            Title = "Tests / Clear",
            DestructiveHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Free a stuck tests-run slot (a run that never reported back) so 'tests-run' can start again.")]
        [Description("Free a stuck 'tests-run' slot: drops the deferred run and the in-progress marker so a new run can start. " +
            "It does not stop a run that is really executing inside the Unity Test Runner.")]
        public static string Clear()
        {
            return MainThread.Instance.Run(() =>
            {
                var record = TestRunStore.Read();
                var wasExecuting = TestRunGuard.Executing;
                var request = TestResultCollector.TestCallRequestID;
                var hadPending = HasPendingTestRun();
                ClearPendingTestRun();
                EditorApplication.update -= ResumePendingTestRunOnce;
                TestRunGuard.Clear();
                if (record != null && !record.IsTerminal)
                {
                    record.state = TestRunStore.Cleared;
                    record.finishedTicks = DateTime.UtcNow.Ticks;
                    TestRunStore.Write(record);
                }
                return $"cleared: pending_run={hadPending} executing={wasExecuting} request={(string.IsNullOrEmpty(request) ? "(none)" : request)}";
            });
        }

        static void CollectAll(ITestAdaptor test, List<(string Assembly, string Name)> sink)
        {
            if (!test.IsSuite)
                sink.Add((AssemblyOf(test), test.FullName));
            if (test.HasChildren)
                foreach (var child in test.Children)
                    CollectAll(child, sink);
        }

        internal static string FormatList(string mode, List<(string Assembly, string Name)> tests, string? nameContains, int limit)
        {
            if (!string.IsNullOrEmpty(nameContains))
                tests = tests.Where(t => t.Name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            limit = Math.Max(1, limit);
            var sb = new StringBuilder();
            sb.AppendLine($"mode={mode} total={tests.Count} shown={Math.Min(limit, tests.Count)}");
            foreach (var g in tests.GroupBy(t => t.Assembly).OrderBy(g => g.Key))
                sb.AppendLine($"assembly {(g.Key.Length == 0 ? "(unknown)" : g.Key)}: {g.Count()}");
            foreach (var t in tests.Take(limit))
                sb.AppendLine(t.Name);
            return sb.ToString().TrimEnd();
        }

        // slotHeld: the run still owns the Test Runner slot; a running record without it means the run was lost
        internal static string FormatResult(TestJobRecord? record, string? jobId, bool includePassingTests, bool slotHeld)
        {
            if (record == null)
                return "[no_run] No test run has been recorded yet.";
            if (!string.IsNullOrEmpty(jobId) && jobId != record.jobId)
                return $"[unknown_job] No record for job '{jobId}'. The last run is '{record.jobId}'.";

            var elapsed = TimeSpan.FromTicks(Math.Max(0, (record.finishedTicks > 0 ? record.finishedTicks : DateTime.UtcNow.Ticks) - record.startedTicks));
            var sb = new StringBuilder();
            sb.Append($"job_id={record.jobId} state={record.state} mode={record.testMode} elapsed_s={(int)elapsed.TotalSeconds} async={record.isAsync}");
            if (!record.IsTerminal)
            {
                if (slotHeld)
                    sb.Append($" retry_after_ms={TestRunGuard.RetryAfterMs(record.state == TestRunStore.Running)}");
                else
                    sb.Append(" note=\"no run owns the slot any more; call tests-clear or start a new tests-run\"");
                return sb.ToString();
            }

            if (record.state != TestRunStore.Finished)
            {
                if (!string.IsNullOrEmpty(record.error))
                    sb.Append("\nerror: ").Append(record.error);
                return sb.ToString();
            }

            sb.Append($"\nsummary: status={record.status} total={record.total} passed={record.passed} failed={record.failed} skipped={record.skipped} duration_s={record.durationSec:0.###}");
            foreach (var row in record.rows)
            {
                if (!includePassingTests && row.status == nameof(TestResultStatus.Passed))
                    continue;
                sb.Append($"\n- [{row.status}] {row.name} ({row.durationMs:0} ms)");
                if (!string.IsNullOrEmpty(row.message))
                    sb.Append("\n    ").Append(row.message.Replace("\n", "\n    "));
                if (!string.IsNullOrEmpty(row.stackTrace))
                    sb.Append("\n    ").Append(row.stackTrace.Replace("\n", "\n    "));
            }
            return sb.ToString();
        }
    }
}
