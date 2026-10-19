#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Feeder.Logging;
using NUnit.Framework;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests.Utils
{
    public sealed class LogStorageTests
    {
        const string FileName = "mx-logs.txt";

        string _directory = null!;
        readonly List<IDisposable> _owned = new List<IDisposable>();

        [SetUp]
        public void CreateDirectory()
        {
            _directory = Path.Combine(Path.GetTempPath(), "MxTest_Logs_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var owned in _owned)
                owned.Dispose();
            _owned.Clear();
            try
            {
                if (Directory.Exists(_directory))
                    Directory.Delete(_directory, true);
            }
            catch (IOException)
            {
            }
        }

        BufferedFileLogStorage Open(int threshold = 100)
        {
            var storage = new BufferedFileLogStorage(NullLogger.Instance, threshold, _directory, FileName);
            _owned.Add(storage);
            return storage;
        }

        static LogEntry Entry(string message, LogType type = LogType.Log, int minutesAgo = 0, string? stackTrace = null)
            => new LogEntry(type, message, DateTime.Now.AddMinutes(-minutesAgo), stackTrace);

        static string[] Messages(IEnumerable<LogEntry> entries) => entries.Select(e => e.Message).ToArray();

        [Test]
        public void Query_IsOldestFirstAndKeepsTheNewest()
        {
            var storage = Open();
            storage.Append(Entry("a"), Entry("b"), Entry("c"));

            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, Messages(storage.Query()));
            CollectionAssert.AreEqual(new[] { "b", "c" }, Messages(storage.Query(maxEntries: 2)));
        }

        [Test]
        public void Query_SpanningFileAndBuffer_StaysChronological()
        {
            var storage = Open(threshold: 2);
            storage.Append(Entry("a"));
            storage.Append(Entry("b"));
            storage.Append(Entry("c"));

            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, Messages(storage.Query()));
            CollectionAssert.AreEqual(new[] { "b", "c" }, Messages(storage.Query(maxEntries: 2)));
        }

        [Test]
        public void Query_FiltersByTypeAndPredicate()
        {
            var storage = Open();
            storage.Append(Entry("plain"), Entry("odd", LogType.Warning), Entry("worse", LogType.Warning), Entry("bad", LogType.Error));

            CollectionAssert.AreEqual(new[] { "odd", "worse" }, Messages(storage.Query(logTypeFilter: LogType.Warning)));
            CollectionAssert.AreEqual(new[] { "worse" }, Messages(storage.Query(filter: e => e.Message.StartsWith("w"))));
        }

        [Test]
        public void Query_TimeWindow_DropsOlderEntries()
        {
            var storage = Open(threshold: 2);
            storage.Append(Entry("ancient", minutesAgo: 60));
            storage.Append(Entry("old", minutesAgo: 10));
            storage.Append(Entry("fresh"));

            CollectionAssert.AreEqual(new[] { "fresh" }, Messages(storage.Query(lastMinutes: 5)));
            CollectionAssert.AreEqual(new[] { "old", "fresh" }, Messages(storage.Query(since: DateTime.Now.AddMinutes(-30))));
        }

        [Test]
        public void FileEntries_KeepMessageTypeAndStackTrace()
        {
            var storage = Open(threshold: 1);
            storage.Append(Entry("two\nlines \"quoted\"", LogType.Exception, stackTrace: "at Foo()"));

            var entry = storage.Query().Single();

            Assert.AreEqual("two\nlines \"quoted\"", entry.Message);
            Assert.AreEqual(LogType.Exception, entry.LogType);
            Assert.AreEqual("at Foo()", entry.StackTrace);
        }

        [Test]
        public void FlushedEntries_SurviveReopening()
        {
            var first = Open();
            first.Append(Entry("kept"));
            first.Flush();
            first.Dispose();

            CollectionAssert.AreEqual(new[] { "kept" }, Messages(Open().Query()));
        }

        [Test]
        public void Dispose_PersistsBufferedEntries()
        {
            var first = Open();
            first.Append(Entry("pending"));
            first.Dispose();

            CollectionAssert.AreEqual(new[] { "pending" }, Messages(Open().Query()));
        }

        [Test]
        public void Clear_RemovesBufferedAndFileEntries()
        {
            var storage = Open(threshold: 2);
            storage.Append(Entry("a"), Entry("b"), Entry("c"));

            storage.Clear();

            Assert.IsEmpty(storage.Query());
            Assert.IsFalse(File.Exists(Path.Combine(_directory, FileName)));
        }

        [Test]
        public void AfterDispose_QueryIsEmpty()
        {
            var storage = Open();
            storage.Append(Entry("a"));
            storage.Dispose();

            Assert.IsEmpty(storage.Query());
        }

        [Test]
        public void ZeroThreshold_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BufferedFileLogStorage(NullLogger.Instance, 0, _directory, FileName));

            // the base constructor has already opened the file; let the finalizer release it before cleanup
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        [Test]
        public void Collector_StoresUnityLogsWithoutRichText()
        {
            var storage = new MemoryLogStorage();
            var marker = "MxTest_" + Guid.NewGuid().ToString("N");
            using (new UnityLogCollector(storage))
            {
                Debug.Log($"<b>{marker}</b> <size=20>big</size>");
            }

            var entry = storage.Entries.Single(e => e.Message.Contains(marker));
            Assert.AreEqual($"{marker} big", entry.Message);
            Assert.AreEqual(LogType.Log, entry.LogType);
            Assert.IsNull(entry.StackTrace);
        }

        [Test]
        public void Collector_StopsListeningOnceDisposed()
        {
            var storage = new MemoryLogStorage();
            var marker = "MxTest_" + Guid.NewGuid().ToString("N");
            new UnityLogCollector(storage).Dispose();

            Debug.Log(marker);

            Assert.IsFalse(storage.Entries.Any(e => e.Message.Contains(marker)));
            Assert.IsTrue(storage.Disposed);
        }

        [Test]
        public void Collector_ClearAndSave_ReachTheStorage()
        {
            var storage = new MemoryLogStorage();
            using var collector = new UnityLogCollector(storage);
            storage.Append(Entry("a"));

            collector.Save();
            collector.Clear();

            Assert.AreEqual(1, storage.Flushes);
            Assert.IsFalse(storage.Entries.Any(e => e.Message == "a"));
        }

        sealed class MemoryLogStorage : ILogStorage
        {
            public readonly List<LogEntry> Entries = new List<LogEntry>();
            public int Flushes;
            public bool Disposed;

            public Task AppendAsync(params LogEntry[] entries)
            {
                Append(entries);
                return Task.CompletedTask;
            }

            public void Append(params LogEntry[] entries)
            {
                lock (Entries)
                    Entries.AddRange(entries);
            }

            public Task FlushAsync()
            {
                Flush();
                return Task.CompletedTask;
            }

            public void Flush() => Flushes++;

            public Task<LogEntry[]> QueryAsync(int maxEntries = 100, LogType? logTypeFilter = null, bool includeStackTrace = false,
                int lastMinutes = 0, DateTime? since = null, Func<LogEntry, bool>? filter = null)
                => Task.FromResult(Query(maxEntries, logTypeFilter, includeStackTrace, lastMinutes, since, filter));

            public LogEntry[] Query(int maxEntries = 100, LogType? logTypeFilter = null, bool includeStackTrace = false,
                int lastMinutes = 0, DateTime? since = null, Func<LogEntry, bool>? filter = null)
            {
                lock (Entries)
                    return Entries.ToArray();
            }

            public void Clear()
            {
                lock (Entries)
                    Entries.Clear();
            }

            public void Dispose() => Disposed = true;
        }
    }
}
