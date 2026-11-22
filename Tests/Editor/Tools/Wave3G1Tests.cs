#nullable enable
using System.IO;
using System.Linq;
using Feeder.MCP.Editor.API;
using Feeder.MCP.Editor.Query;
using Feeder.ToolHost.Common.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class Wave3G1Tests
    {
        GameObject? _fx;
        int _undoGroup;

        [SetUp]
        public void SetUp()
        {
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
            _fx = new GameObject("G1T_Fx", typeof(ParticleSystem));
            Undo.RegisterCreatedObjectUndo(_fx, "G1T fixture");
        }

        [TearDown]
        public void TearDown()
        {
            Undo.RevertAllDownToGroup(_undoGroup);
            foreach (var go in Object.FindObjectsOfType<GameObject>(true).Where(g => g.name.StartsWith("G1T_")))
                Object.DestroyImmediate(go);
            foreach (var name in new[] { "g1t_a", "g1t_b" })
            {
                var file = Path.Combine(Tool_Profiler.SnapshotFolder, name + ".json");
                if (File.Exists(file)) File.Delete(file);
            }
        }

        static string Text(ResponseCallTool r) => string.Join("\n", r.Content.Select(c => c.Text));

        [Test]
        public void Query_WritesParticleModuleProperty()
        {
            MatrixQuery.Mutate("find(\"G1T_Fx\") | set ParticleSystem.main.duration = 3");
            Assert.AreEqual(3f, _fx!.GetComponent<ParticleSystem>().main.duration);
        }

        [Test]
        public void Query_StillWritesThroughParticleCurves()
        {
            MatrixQuery.Mutate("find(\"G1T_Fx\") | set ParticleSystem.main.startLifetime.constant = 4");
            Assert.AreEqual(4f, _fx!.GetComponent<ParticleSystem>().main.startLifetime.constant);
        }

        [Test]
        public void SceneApply_PatchesLiveObjectAndUndoRemovesNewEmitter()
        {
            var tool = new Tool_Vfx();
            var apply = tool.Apply("{\"emitters\":[{\"name\":\"G1T_Core\",\"main\":{\"duration\":2}}]}", target: "G1T_Fx");
            Assert.AreEqual(ResponseStatus.Success, apply.Status, Text(apply));
            Assert.IsNotNull(_fx!.transform.Find("G1T_Core"));
            StringAssert.Contains("G1T_Core", Text(tool.Read(target: "G1T_Fx")));
            Undo.PerformUndo();
            Assert.IsNull(_fx.transform.Find("G1T_Core"));
        }

        [Test]
        public void SceneApply_UnknownTargetIsAnError()
        {
            Assert.AreEqual(ResponseStatus.Error, new Tool_Vfx().Apply("{}", target: "G1T_Nowhere").Status);
        }

        [Test]
        public void MemorySnapshot_TakeCompareAndDelete()
        {
            var tool = new Tool_Profiler();
            Assert.AreEqual(ResponseStatus.Success, tool.Memory("take", "g1t_a").Status);
            var extra = new Texture2D(256, 256);
            try
            {
                Assert.AreEqual(ResponseStatus.Success, tool.Memory("take", "g1t_b").Status);
                var diff = tool.Memory("compare", "g1t_a", against: "g1t_b");
                Assert.AreEqual(ResponseStatus.Success, diff.Status, Text(diff));
                StringAssert.Contains("Texture2D", Text(diff));
                StringAssert.Contains("g1t_a", Text(tool.Memory("list")));
            }
            finally
            {
                Object.DestroyImmediate(extra);
            }
            Assert.AreEqual(ResponseStatus.Success, tool.Memory("compare", "g1t_a").Status);
            Assert.AreEqual(ResponseStatus.Success, tool.Memory("delete", "g1t_a").Status);
            Assert.AreEqual(ResponseStatus.Error, tool.Memory("compare", "g1t_a", against: "g1t_b").Status);
        }

        [Test]
        public void MemorySnapshot_RejectsPathLikeNames()
        {
            Assert.AreEqual(ResponseStatus.Error, new Tool_Profiler().Memory("take", "../evil").Status);
        }

        [Test]
        public void ProfilerStats_ReportsAllSections()
        {
            var text = Text(new Tool_Profiler().Stats());
            StringAssert.Contains("profiler enabled=", text);
            StringAssert.Contains("memoryMB", text);
            StringAssert.Contains("drawCalls=", text);
        }

        [Test]
        public void FrameDebugger_StatusAndEventsWhenOff()
        {
            var tool = new Tool_Profiler();
            var status = tool.FrameDebugger("status");
            Assert.AreEqual(ResponseStatus.Success, status.Status, Text(status));
            var events = tool.FrameDebugger("events");
            Assert.AreEqual(ResponseStatus.Error, events.Status);
            StringAssert.Contains("not_enabled", Text(events));
        }

        [Test]
        public void FrameDebugger_DetailNeverReachesGetFrameEventData()
        {
            CollectionAssert.DoesNotContain(Tool_Profiler.ReflectedFrameDebuggerMethods, "GetFrameEventData");
            var events = new Tool_Profiler().FrameDebugger("events", 0, 10, true);
            Assert.AreEqual(ResponseStatus.Error, events.Status);
            StringAssert.Contains("[unsupported]", Text(events));
        }
    }
}
