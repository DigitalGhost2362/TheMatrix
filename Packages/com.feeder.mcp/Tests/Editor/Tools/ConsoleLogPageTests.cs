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
        public void GetLogsPage_CursorReturnsOnlyNewerLogs()
        {
            Assume.That(MatrixPluginEditor.HasInstance && MatrixPluginEditor.Instance.LogCollector != null, "Matrix Bridge is not active.");
            var tool = new Tool_Console();
            var marker = Guid.NewGuid().ToString("N");

            Debug.Log($"page-before {marker}");
            var first = tool.GetLogsPage(maxEntries: 5, include: marker);
            Assert.AreEqual(1, first.Entries.Length);

            Debug.Log($"page-after {marker}");
            var next = tool.GetLogsPage(maxEntries: 5, afterCursor: first.NextCursor, include: marker);

            Assert.AreEqual(1, next.Entries.Length);
            StringAssert.Contains("page-after", next.Entries[0].Message);
            Assert.Greater(long.Parse(next.NextCursor), long.Parse(first.NextCursor));
        }
    }
}
