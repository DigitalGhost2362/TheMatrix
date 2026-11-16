#nullable enable
using System;
using System.Linq;
using Feeder.MCP.Editor.API;
using Feeder.ToolHost;
using NUnit.Framework;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class ConsoleLogPageTests
    {
        static LogEntry[] Logs(int count, long firstSeq = 100) => Enumerable.Range(0, count)
            .Select(i => new LogEntry(LogType.Log, $"m{i}") { Seq = firstSeq + i })
            .ToArray();

        [Test]
        public void Page_WithoutCursorKeepsTheNewestAndSaysItCut()
        {
            var page = Tool_Console.Page(Logs(5), 3, 0, false);

            CollectionAssert.AreEqual(new[] { "m2", "m3", "m4" }, page.Entries.Select(e => e.Message).ToArray());
            Assert.IsTrue(page.Truncated);
            Assert.AreEqual("104", page.NextCursor);
            Assert.That(page.Hint, Does.Contain("maxEntries"));
        }

        [Test]
        public void Page_WithCursorKeepsTheOldestSoNothingIsSkipped()
        {
            var page = Tool_Console.Page(Logs(5), 3, 99, false);

            CollectionAssert.AreEqual(new[] { "m0", "m1", "m2" }, page.Entries.Select(e => e.Message).ToArray());
            Assert.IsTrue(page.Truncated);
            Assert.AreEqual("102", page.NextCursor);
            Assert.That(page.Hint, Does.Contain("afterCursor=102"));
        }

        [Test]
        public void Page_NothingCutHasNoHint()
        {
            var page = Tool_Console.Page(Logs(2), 3, 0, false);

            Assert.IsFalse(page.Truncated);
            Assert.IsNull(page.Hint);
            Assert.AreEqual(2, page.Entries.Length);
        }

        [Test]
        public void Page_EmptyKeepsTheCursor()
        {
            var page = Tool_Console.Page(Array.Empty<LogEntry>(), 3, 777, false);

            Assert.AreEqual("777", page.NextCursor);
            Assert.IsEmpty(page.Entries);
        }

        [Test]
        public void Page_EntriesCarryTheirSeq()
        {
            var page = Tool_Console.Page(Logs(2), 5, 0, false);

            CollectionAssert.AreEqual(new[] { "100", "101" }, page.Entries.Select(e => e.Seq).ToArray());
        }

        [Test]
        public void Cursor_SurvivesTheStringRoundTripExactly()
        {
            const long cursor = 639294581550938155;
            var page = Tool_Console.Page(new[] { new LogEntry(LogType.Log, "x") { Seq = cursor } }, 5, 0, false);

            Assert.AreEqual("639294581550938155", page.NextCursor);
            Assert.AreEqual(cursor, Tool_Console.ParseCursor(page.NextCursor));
            Assert.AreEqual(0, Tool_Console.ParseCursor(null));
            Assert.Throws<ToolCallerException>(() => Tool_Console.ParseCursor("12x"));
        }

        [Test]
        public void AfterACursor_EveryNewerLogIsRead_SoTheOldestAreNotDropped()
        {
            Assert.AreEqual(6, Tool_Console.ScanLimit(0, false, 5));
            Assert.AreEqual(int.MaxValue, Tool_Console.ScanLimit(1, false, 5));
            Assert.AreEqual(int.MaxValue, Tool_Console.ScanLimit(1, true, 5));

            // more newer logs than the old 10000 cap: the first page still starts right after the cursor
            const int count = 10050;
            const long cursor = 1000;
            var folder = "Temp/_MxTestLogPage";
            if (System.IO.Directory.Exists(folder))
                System.IO.Directory.Delete(folder, true);
            var storage = new FileLogStorage(null, folder, "page.log");
            try
            {
                storage.Append(Logs(count, cursor + 1));
                storage.Flush();
                var logs = storage.Query(Tool_Console.ScanLimit(cursor, false, 50), filter: entry => (entry.Seq ?? 0) > cursor);
                var page = Tool_Console.Page(logs, 50, cursor, false);

                Assert.AreEqual("m0", page.Entries[0].Message);
                Assert.AreEqual("m49", page.Entries[49].Message);
                Assert.IsTrue(page.Truncated);
            }
            finally
            {
                storage.Dispose();
                System.IO.Directory.Delete(folder, true);
            }
        }

        [Test]
        public void GetLogs_CursorReturnsOnlyNewerLogs()
        {
            Assume.That(MatrixPluginEditor.HasInstance && MatrixPluginEditor.Instance.LogCollector != null, "Matrix Bridge is not active.");
            var tool = new Tool_Console();
            var marker = Guid.NewGuid().ToString("N");

            Debug.Log($"page-before {marker}");
            var first = tool.GetLogs(maxEntries: 5, include: marker);
            Assert.AreEqual(1, first.Entries.Length);

            Debug.Log($"page-after {marker}");
            var next = tool.GetLogs(maxEntries: 5, afterCursor: first.NextCursor, include: marker);

            Assert.AreEqual(1, next.Entries.Length);
            StringAssert.Contains("page-after", next.Entries[0].Message);
            Assert.Greater(long.Parse(next.NextCursor), long.Parse(first.NextCursor));
        }
    }
}
