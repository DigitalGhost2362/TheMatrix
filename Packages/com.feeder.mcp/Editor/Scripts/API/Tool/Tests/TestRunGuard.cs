#nullable enable
using System;
using UnityEditor;

namespace Feeder.MCP.Editor.API.TestRunner
{
    public static class TestRunGuard
    {
        public const int ExecutingRetryMs = 10000;
        public const int StartingRetryMs = 5000;
        public static readonly TimeSpan ExecutingTimeout = TimeSpan.FromMinutes(60);
        public static readonly TimeSpan DeferredTimeout = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan StartingTimeout = TimeSpan.FromMinutes(2);

        const string StartedKey = "Feeder_MCP_TestRunner_StartedUtcTicks";
        const string ExecutingKey = "Feeder_MCP_TestRunner_Executing";

        public enum SlotState { Free, Busy, Stale }

        public static bool Executing
        {
            get => SessionState.GetBool(ExecutingKey, false);
            set => SessionState.SetBool(ExecutingKey, value);
        }

        public static void Begin(string requestId)
        {
            TestResultCollector.TestCallRequestID = requestId;
            SessionState.SetString(StartedKey, DateTime.UtcNow.Ticks.ToString());
            Executing = false;
        }

        public static void Touch()
            => SessionState.SetString(StartedKey, DateTime.UtcNow.Ticks.ToString());

        public static void Clear()
        {
            TestResultCollector.TestCallRequestID = string.Empty;
            SessionState.EraseString(StartedKey);
            Executing = false;
        }

        public static SlotState Evaluate(string requestId, long startedTicks, long nowTicks, bool deferred, bool executing, bool editorBusy)
        {
            if (string.IsNullOrEmpty(requestId))
                return SlotState.Free;
            if (startedTicks <= 0)
                return SlotState.Stale;

            var age = TimeSpan.FromTicks(Math.Max(0, nowTicks - startedTicks));
            var timeout = executing ? ExecutingTimeout : deferred ? DeferredTimeout : StartingTimeout;
            if (!executing && !deferred && editorBusy)
                return SlotState.Busy;
            return age > timeout ? SlotState.Stale : SlotState.Busy;
        }

        // lets a test borrow the slot without wiping a live run it is executing inside of
        public readonly struct Snapshot
        {
            internal readonly string RequestId;
            internal readonly string Started;
            internal readonly bool Executing;

            internal Snapshot(string requestId, string started, bool executing)
            {
                RequestId = requestId;
                Started = started;
                Executing = executing;
            }
        }

        public static Snapshot Capture()
            => new(TestResultCollector.TestCallRequestID, SessionState.GetString(StartedKey, string.Empty), Executing);

        public static void Restore(Snapshot snapshot)
        {
            TestResultCollector.TestCallRequestID = snapshot.RequestId;
            if (string.IsNullOrEmpty(snapshot.Started))
                SessionState.EraseString(StartedKey);
            else
                SessionState.SetString(StartedKey, snapshot.Started);
            Executing = snapshot.Executing;
        }

        public static int RetryAfterMs(bool executing) => executing ? ExecutingRetryMs : StartingRetryMs;

        public static string FormatRefusal(string requestId, TimeSpan age, bool deferred, bool executing)
        {
            var phase = executing ? "running" : deferred ? "waiting for a script reload" : "starting";
            return $"[tests_running retry_after_ms={RetryAfterMs(executing)}] A tests-run is already in progress " +
                $"(request {requestId}, started {(int)age.TotalSeconds} s ago, {phase}). " +
                "Only one run can use the Unity Test Runner at a time; wait for it to finish, then call tests-run again.";
        }

        // returns the refusal text when a live run owns the slot; clears a stale slot
        public static string? CheckAndReclaim(bool deferred)
        {
            var requestId = TestResultCollector.TestCallRequestID;
            long.TryParse(SessionState.GetString(StartedKey, string.Empty), out var started);
            var executing = Executing;
            var now = DateTime.UtcNow.Ticks;
            var state = Evaluate(requestId, started, now, deferred, executing, EditorApplication.isCompiling || EditorApplication.isUpdating);
            if (state == SlotState.Busy)
                return FormatRefusal(requestId, TimeSpan.FromTicks(Math.Max(0, now - started)), deferred, executing);
            if (state == SlotState.Stale)
                Clear();
            return null;
        }
    }
}
