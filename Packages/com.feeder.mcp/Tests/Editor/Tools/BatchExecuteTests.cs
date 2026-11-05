#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Feeder.Json;
using Feeder.MCP.Editor.API;
using Feeder.ToolHost.Common.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class BatchExecuteTests
    {
        static readonly string[] Known = { "alpha", "beta", "gamma", "disabled-tool" };
        readonly List<string> ran = new List<string>();
        readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in objects.Where(o => o != null))
                UnityEngine.Object.DestroyImmediate(go);
            objects.Clear();
            ran.Clear();
        }

        static string? Resolve(string name)
            => name == "disabled-tool" ? null : Known.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

        static BatchCommand Cmd(string tool, string? argumentsJson = null)
            => new BatchCommand { Tool = tool, Arguments = argumentsJson == null ? (JsonElement?)null : JsonDocument.Parse(argumentsJson).RootElement.Clone() };

        Task<ResponseCallTool> Ok(string name, IReadOnlyDictionary<string, JsonElement>? args, int index)
        {
            ran.Add(name);
            return Task.FromResult(ResponseCallTool.Success("done " + name));
        }

        BatchResult Run(BatchCommand[] commands, bool stopOnError, bool atomic,
            Func<string, IReadOnlyDictionary<string, JsonElement>?, int, Task<ResponseCallTool>>? invoke = null)
            => ToolBatch.RunAsync(commands, stopOnError, atomic, Resolve, invoke ?? Ok,
                () => { Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName(ToolBatch.UndoName); return Undo.GetCurrentGroup(); },
                group => Undo.CollapseUndoOperations(group),
                group => Undo.RevertAllDownToGroup(group)).GetAwaiter().GetResult();

        [Test]
        public void RunsCommandsInOrder_WithExactToolNames()
        {
            var result = Run(new[] { Cmd("ALPHA"), Cmd("beta", "{\"x\":1}") }, true, false);

            CollectionAssert.AreEqual(new[] { "alpha", "beta" }, ran);
            Assert.AreEqual(2, result.Succeeded);
            Assert.AreEqual("done alpha", result.Results[0].Text);
        }

        [Test]
        public void StopOnError_SkipsTheRest()
        {
            var result = Run(new[] { Cmd("alpha"), Cmd("beta"), Cmd("gamma") }, true, false, Fail("beta"));

            Assert.AreEqual(2, result.Executed);
            Assert.AreEqual(1, result.StoppedAt);
            CollectionAssert.AreEqual(new[] { "alpha", "beta" }, ran);
        }

        [Test]
        public void WithoutStopOnError_ContinuesPastFailures()
        {
            var result = Run(new[] { Cmd("alpha"), Cmd("beta"), Cmd("gamma") }, false, false, Fail("beta"));

            Assert.AreEqual(3, result.Executed);
            Assert.AreEqual(1, result.Failed);
            Assert.IsNull(result.StoppedAt);
            Assert.IsFalse(result.Results[1].Ok);
        }

        [Test]
        public void ThrownException_IsReportedAsFailure()
        {
            var result = Run(new[] { Cmd("alpha") }, true, false, (n, a, i) => throw new InvalidOperationException("boom"));

            Assert.AreEqual("boom", result.Results[0].Error);
        }

        [Test]
        public void EmptyOrTooManyCommands_Throw()
        {
            Assert.Throws<ArgumentException>(() => Run(new BatchCommand[0], true, false));
            var many = Enumerable.Range(0, ToolBatch.MaxCommands + 1).Select(_ => Cmd("alpha")).ToArray();
            Assert.Throws<ArgumentException>(() => Run(many, true, false));
            Assert.AreEqual(0, ran.Count);
            Assert.DoesNotThrow(() => Run(many.Take(ToolBatch.MaxCommands).ToArray(), true, false));
        }

        [TestCase("batch-execute")]
        [TestCase("tool-call")]
        [TestCase("editor-health")]
        [TestCase("editor-lock-acquire")]
        [TestCase("Editor-Lock-Release")]
        [TestCase("editor-lock-status")]
        public void ForbiddenTools_AreRejectedBeforeAnythingRuns(string forbidden)
        {
            Assert.Throws<ArgumentException>(() => Run(new[] { Cmd("alpha"), Cmd(forbidden) }, true, false));
            Assert.AreEqual(0, ran.Count);
        }

        [Test]
        public void UnknownOrDisabledTool_IsRejectedBeforeAnythingRuns()
        {
            Assert.Throws<ArgumentException>(() => Run(new[] { Cmd("alpha"), Cmd("disabled-tool") }, true, false));
            Assert.Throws<ArgumentException>(() => Run(new[] { Cmd("alpha"), Cmd("nope") }, true, false));
            Assert.AreEqual(0, ran.Count);
        }

        [Test]
        public void ArgumentsThatAreNotAnObject_AreRejectedBeforeAnythingRuns()
        {
            Assert.Throws<ArgumentException>(() => Run(new[] { Cmd("alpha"), Cmd("beta", "[1]") }, true, false));
            Assert.AreEqual(0, ran.Count);
        }

        [Test]
        public void ProcessingResponse_IsAnErrorAndStopsTheBatch()
        {
            var result = Run(new[] { Cmd("alpha"), Cmd("beta") }, false, false,
                (n, a, i) => Task.FromResult(n == "alpha" ? ResponseCallTool.Processing("compiling") : ResponseCallTool.Success()));

            Assert.IsFalse(result.Results[0].Ok);
            StringAssert.Contains("asynchronously", result.Results[0].Error);
        }

        [Test]
        public void Atomic_FailureRevertsEarlierUndoRecordedChanges()
        {
            var result = Run(new[] { Cmd("alpha"), Cmd("beta") }, false, true, (n, a, i) =>
            {
                if (n == "beta")
                    return Task.FromResult(ResponseCallTool.Error("nope"));
                return Task.FromResult(Create("MatrixBatchAtomicFail"));
            });

            Assert.IsTrue(result.RolledBack);
            Assert.IsTrue(result.Results.All(r => r.RolledBack));
            Assert.IsTrue(objects[0] == null, "the object made by the first command should be reverted");
        }

        [Test]
        public void Atomic_Success_IsOneUndoStep()
        {
            var result = Run(new[] { Cmd("alpha"), Cmd("beta") }, true, true, (n, a, i) => Task.FromResult(Create("MatrixBatchAtomic_" + n)));

            Assert.IsFalse(result.RolledBack);
            Assert.AreEqual(ToolBatch.UndoName, Undo.GetCurrentGroupName());
            Undo.PerformUndo();
            Assert.IsTrue(objects.All(o => o == null), "one undo should remove both objects");
        }

        Func<string, IReadOnlyDictionary<string, JsonElement>?, int, Task<ResponseCallTool>> Fail(string tool)
            => (n, a, i) =>
            {
                ran.Add(n);
                return Task.FromResult(n == tool ? ResponseCallTool.Error("failed " + n) : ResponseCallTool.Success());
            };

        ResponseCallTool Create(string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            objects.Add(go);
            return ResponseCallTool.Success();
        }
    }
}
