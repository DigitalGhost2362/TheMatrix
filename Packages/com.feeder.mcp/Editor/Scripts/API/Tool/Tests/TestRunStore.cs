#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Feeder.MCP.Editor.API.TestRunner
{
    [Serializable]
    public sealed class TestJobRow
    {
        public string name = string.Empty;
        public string status = string.Empty;
        public double durationMs;
        public string message = string.Empty;
        public string stackTrace = string.Empty;
    }

    [Serializable]
    public sealed class TestJobRecord
    {
        public string jobId = string.Empty;
        public string state = TestRunStore.Queued;
        public string testMode = string.Empty;
        public string assembly = string.Empty;
        public string ns = string.Empty;
        public string cls = string.Empty;
        public string method = string.Empty;
        public bool isAsync;
        public long startedTicks;
        public long finishedTicks;
        public string error = string.Empty;
        public string status = string.Empty;
        public int total;
        public int passed;
        public int failed;
        public int skipped;
        public double durationSec;
        public List<TestJobRow> rows = new();

        public TestFilterParameters Filter => new(
            string.IsNullOrEmpty(assembly) ? null : assembly,
            string.IsNullOrEmpty(ns) ? null : ns,
            string.IsNullOrEmpty(cls) ? null : cls,
            string.IsNullOrEmpty(method) ? null : method);

        public bool IsTerminal => state == TestRunStore.Finished || state == TestRunStore.Failed || state == TestRunStore.Cleared;
    }

    // last test run, kept in Library so a reload, a timed out client or an editor restart cannot lose it
    public static class TestRunStore
    {
        public const string Queued = "queued";
        public const string Deferred = "deferred";
        public const string Running = "running";
        public const string Finished = "finished";
        public const string Failed = "failed";
        public const string Cleared = "cleared";

        public static string? PathOverride;

        static string FilePath => PathOverride
            ?? Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "Library", "Feeder_MCP", "last-test-run.json");

        public static TestJobRecord? Read()
        {
            try
            {
                var path = FilePath;
                if (!File.Exists(path))
                    return null;
                var record = JsonUtility.FromJson<TestJobRecord>(File.ReadAllText(path));
                return string.IsNullOrEmpty(record?.jobId) ? null : record;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Write(TestJobRecord record)
        {
            try
            {
                var path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonUtility.ToJson(record));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestRunner] Could not save the test run record: {ex.Message}");
            }
        }

        public static void Begin(string jobId, string testMode, TestFilterParameters filter, bool isAsync)
            => Write(new TestJobRecord
            {
                jobId = jobId,
                testMode = testMode,
                assembly = filter.TestAssembly ?? string.Empty,
                ns = filter.TestNamespace ?? string.Empty,
                cls = filter.TestClass ?? string.Empty,
                method = filter.TestMethod ?? string.Empty,
                isAsync = isAsync,
                startedTicks = DateTime.UtcNow.Ticks,
            });

        public static void SetState(string state)
        {
            var record = Read();
            if (record == null || record.IsTerminal)
                return;
            record.state = state;
            Write(record);
        }

        public static void Fail(string error)
        {
            var record = Read();
            if (record == null)
                return;
            record.state = Failed;
            record.error = error;
            record.finishedTicks = DateTime.UtcNow.Ticks;
            Write(record);
        }

        public static void Finish(TestSummaryData summary, IEnumerable<TestResultData> results)
        {
            var record = Read();
            if (record == null)
                return;
            record.state = Finished;
            record.finishedTicks = DateTime.UtcNow.Ticks;
            record.status = summary.Status.ToString();
            record.total = summary.TotalTests;
            record.passed = summary.PassedTests;
            record.failed = summary.FailedTests;
            record.skipped = summary.SkippedTests;
            record.durationSec = summary.Duration.TotalSeconds;
            record.rows = new List<TestJobRow>();
            foreach (var r in results)
                record.rows.Add(new TestJobRow
                {
                    name = r.Name,
                    status = r.Status.ToString(),
                    durationMs = r.Duration.TotalMilliseconds,
                    message = r.Message ?? string.Empty,
                    stackTrace = r.StackTrace ?? string.Empty,
                });
            Write(record);
        }
    }
}
