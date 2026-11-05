#nullable enable

using System;
using Feeder.MCP.Editor.API;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class ScriptExecuteCacheTests
    {
        // a unique comment gives every test its own compile-cache entry, so statics never leak between tests
        static string Counter() => "public class Script { static int c; public static int Main() { return ++c; } } // " + Guid.NewGuid().ToString("N");

        static string? Run(string code, bool fresh = false, int replayId = 0)
        {
            try
            {
                return Tool_Script.Execute(code, freshAssembly: fresh, replayId: replayId)?.valueJsonElement?.ToString();
            }
            catch (Exception e) when (e.Message.Contains("Compile service failed"))
            {
                Assert.Ignore("Compile service is not reachable: " + e.Message);
                return null;
            }
        }

        [Test]
        public void SameCode_ReusesTheLoadedAssembly_SoStaticsPersist()
        {
            var code = Counter();
            Assert.AreEqual("1", Run(code));
            Assert.AreEqual("2", Run(code));
        }

        [Test]
        public void FreshAssembly_StartsWithEmptyStatics()
        {
            var code = Counter();
            Assert.AreEqual("1", Run(code, fresh: true));
            Assert.AreEqual("1", Run(code, fresh: true));
        }

        [Test]
        public void History_RecordsRuns_AndReplayRepeatsTheRun()
        {
            var code = Counter();
            Run(code);

            var newest = ScriptRunHistory.Recent(1)[0];
            Assert.AreEqual(code, newest.code);
            Assert.AreEqual("2", Run("", replayId: newest.id));
        }

        [Test]
        public void Replay_OfAReplay_DoesNotAddAnEntry()
        {
            var code = Counter();
            Run(code);
            var before = ScriptRunHistory.Recent(1)[0].id;

            Run("", replayId: before);

            Assert.AreEqual(before, ScriptRunHistory.Recent(1)[0].id);
        }

        [Test]
        public void History_KeepsOnlyTheNewestTwentyRuns_WithRisingIds()
        {
            for (var i = 0; i < 25; i++)
                ScriptRunHistory.Add("// run " + i, "Script", "Main", false, string.Empty);

            var all = ScriptRunHistory.Recent(100);
            Assert.AreEqual(20, all.Count);
            Assert.Greater(all[0].id, all[19].id);
            Assert.AreEqual("// run 24", all[0].code);
        }

        [Test]
        public void Replay_UnknownId_IsAnError()
        {
            var ex = Assert.Throws<Feeder.ToolHost.ToolCallerException>(() => Tool_Script.Execute("", replayId: int.MaxValue));
            StringAssert.Contains("replay_not_found", ex!.Message);
        }

        [Test]
        public void ExecuteHistory_ListsTheLatestRun()
        {
            var id = ScriptRunHistory.Add("// listed " + Guid.NewGuid().ToString("N"), "Script", "Main", false, string.Empty);

            StringAssert.Contains($"#{id} ", Tool_Script.ExecuteHistory(1));
        }
    }
}
