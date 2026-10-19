#nullable enable

using System;
using System.Linq;
using Feeder.MCP.Editor.API;
using Feeder.ToolHost;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class ConsoleGetLogsTests
    {
        readonly Tool_Console _tool = new Tool_Console();
        string _marker = null!;
        string _since = null!;

        [SetUp]
        public void Prepare()
        {
            Assume.That(MatrixPluginEditor.HasInstance && MatrixPluginEditor.Instance.LogCollector != null, Is.True,
                "Matrix Bridge is not active, so no log collector is running.");
            _marker = "MxTest_" + Guid.NewGuid().ToString("N");
            _since = DateTime.UtcNow.AddSeconds(-2).ToString("o");
        }

        LogEntry[] Mine(int maxEntries = 100, LogType? logTypeFilter = null, LogType[]? logTypes = null,
            string? exclude = null, bool groupDuplicates = false, bool includeStackTrace = false)
            => _tool.GetLogs(maxEntries, logTypeFilter, includeStackTrace, 0, logTypes, _since, _marker, exclude, groupDuplicates);

        [Test]
        public void Include_ReturnsOwnEntriesOldestFirst()
        {
            Debug.Log($"{_marker} one");
            Debug.LogWarning($"{_marker} two");

            var logs = Mine();

            Assert.AreEqual(2, logs.Length);
            Assert.AreEqual($"{_marker} one", logs[0].Message);
            Assert.AreEqual(LogType.Log, logs[0].LogType);
            Assert.AreEqual($"{_marker} two", logs[1].Message);
            Assert.AreEqual(LogType.Warning, logs[1].LogType);
        }

        [Test]
        public void LogTypeFilter_KeepsOnlyThatType()
        {
            Debug.Log($"{_marker} info");
            Debug.LogWarning($"{_marker} careful");

            var logs = Mine(logTypeFilter: LogType.Warning);

            Assert.AreEqual(1, logs.Length);
            Assert.AreEqual($"{_marker} careful", logs[0].Message);
        }

        [Test]
        public void LogTypes_ReplaceTheSingleFilter()
        {
            Debug.Log($"{_marker} info");
            Debug.LogWarning($"{_marker} careful");

            var logs = Mine(logTypeFilter: LogType.Warning, logTypes: new[] { LogType.Log });

            Assert.AreEqual(1, logs.Length);
            Assert.AreEqual(LogType.Log, logs[0].LogType);
        }

        [Test]
        public void ErrorEntries_AreCollectedAndFilterable()
        {
            LogAssert.Expect(LogType.Error, $"{_marker} broke");
            Debug.Log($"{_marker} fine");
            Debug.LogError($"{_marker} broke");

            var logs = Mine(logTypes: new[] { LogType.Error });

            Assert.AreEqual(1, logs.Length);
            Assert.AreEqual($"{_marker} broke", logs[0].Message);
        }

        [Test]
        public void Exclude_DropsMatchingMessages()
        {
            Debug.Log($"{_marker} keep");
            Debug.Log($"{_marker} drop");

            var logs = Mine(exclude: "drop$");

            Assert.AreEqual(1, logs.Length);
            Assert.AreEqual($"{_marker} keep", logs[0].Message);
        }

        [Test]
        public void MaxEntries_KeepsTheNewest()
        {
            Debug.Log($"{_marker} first");
            Debug.Log($"{_marker} second");
            Debug.Log($"{_marker} third");

            var logs = Mine(maxEntries: 2);

            CollectionAssert.AreEqual(new[] { $"{_marker} second", $"{_marker} third" }, logs.Select(l => l.Message).ToArray());
        }

        [Test]
        public void GroupDuplicates_CountsRepeatsAndKeepsOrderOfLastOccurrence()
        {
            Debug.Log($"{_marker} same");
            Debug.Log($"{_marker} other");
            Debug.Log($"{_marker} same");
            Debug.Log($"{_marker} same");

            var logs = Mine(groupDuplicates: true);

            Assert.AreEqual(2, logs.Length);
            Assert.AreEqual($"{_marker} other", logs[0].Message);
            Assert.AreEqual(1, logs[0].Count);
            Assert.AreEqual($"{_marker} same", logs[1].Message);
            Assert.AreEqual(3, logs[1].Count);
        }

        [Test]
        public void WithoutGrouping_CountIsUnset()
        {
            Debug.Log($"{_marker} same");
            Debug.Log($"{_marker} same");

            var logs = Mine();

            Assert.AreEqual(2, logs.Length);
            Assert.IsTrue(logs.All(l => l.Count == null));
        }

        [Test]
        public void RichTextTags_AreStripped()
        {
            Debug.Log($"<b>{_marker}</b> <color=#ff0000>alarm</color> <i>now</i>");

            Assert.AreEqual($"{_marker} alarm now", Mine().Single().Message);
        }

        [Test]
        public void StackTraces_AreLeftOutUnlessAsked()
        {
            Debug.LogWarning($"{_marker} careful");

            Assert.IsNull(Mine().Single().StackTrace);
        }

        [Test]
        public void SinceUtc_InTheFuture_ReturnsNothing()
        {
            Debug.Log($"{_marker} old news");

            var future = DateTime.UtcNow.AddMinutes(5).ToString("o");

            Assert.IsEmpty(_tool.GetLogs(include: _marker, sinceUtc: future));
        }

        [Test]
        public void SinceUtc_NotATime_IsACallerError()
        {
            Assert.Throws<ToolCallerException>(() => _tool.GetLogs(sinceUtc: "yesterday-ish"));
        }

        [Test]
        public void Include_InvalidRegex_IsACallerError()
        {
            Assert.Throws<ToolCallerException>(() => _tool.GetLogs(include: "(unclosed"));
        }

        [Test]
        public void MaxEntries_BelowOne_Throws()
        {
            Assert.Throws<ArgumentException>(() => _tool.GetLogs(maxEntries: 0));
        }
    }
}
