#nullable enable

using System;
using System.Collections;
using System.Threading.Tasks;
using Feeder.MCP.Editor.API;
using Feeder.ToolHost;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class WaitForTests
    {
        internal static bool Ready;

        static IEnumerator Await(Task<WaitForResult> task)
        {
            var until = EditorApplication.timeSinceStartup + 60;
            while (!task.IsCompleted && EditorApplication.timeSinceStartup < until)
                yield return null;
            Assert.IsTrue(task.IsCompleted, "wait-for did not return");
        }

        [UnityTest]
        public IEnumerator Log_WrittenAfterOneSecond_IsMet()
        {
            var marker = "MxWaitFor_" + Guid.NewGuid().ToString("N");
            var task = new Tool_Wait().WaitFor("log", pattern: marker, timeoutSec: 10);
            var logAt = EditorApplication.timeSinceStartup + 1;
            while (EditorApplication.timeSinceStartup < logAt)
                yield return null;
            Debug.Log(marker);
            yield return Await(task);
            var result = task.Result;
            Assert.IsTrue(result.Met, result.Note);
            Assert.That(result.WaitedMs, Is.InRange(1000, 2500));
            Assert.AreEqual(1, result.Matches.Length);
            StringAssert.Contains(marker, result.Matches[0].Message);
        }

        [UnityTest]
        public IEnumerator Log_NeverWritten_ReturnsNotMetAfterTheTimeout()
        {
            var task = new Tool_Wait().WaitFor("log", pattern: "MxWaitFor_never_" + Guid.NewGuid().ToString("N"), timeoutSec: 2);
            yield return Await(task);
            var result = task.Result;
            Assert.IsFalse(result.Met);
            Assert.That(result.WaitedMs, Is.InRange(2000, 3500));
            Assert.That(long.Parse(result.NextCursor), Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator Member_ReadsTheValue()
        {
            Ready = true;
            var task = new Tool_Wait().WaitFor("member", member: typeof(WaitForTests).FullName + ".Ready", value: "true", timeoutSec: 5);
            yield return Await(task);
            Assert.IsTrue(task.Result.Met, task.Result.Note);
        }

        [Test]
        public void MissingArguments_AreCallerErrors()
        {
            Assert.ThrowsAsync<ToolCallerException>(() => new Tool_Wait().WaitFor("log"));
            Assert.ThrowsAsync<ToolCallerException>(() => new Tool_Wait().WaitFor("member", member: "A.B"));
            Assert.ThrowsAsync<ToolCallerException>(() => new Tool_Wait().WaitFor("sometime", pattern: "x"));
        }
    }
}
