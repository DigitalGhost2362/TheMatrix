#nullable enable

using Feeder.MCP.Editor.Matrix;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests
{
    public sealed class HeartbeatPhaseTests
    {
        [TestCase(false, false, 0)]
        [TestCase(true, false, FmpPhases.RunningTests)]
        [TestCase(false, true, FmpPhases.PlaymodeTransition)]
        [TestCase(true, true, FmpPhases.RunningTests | FmpPhases.PlaymodeTransition)]
        public void ComputePhase_SetsOneFlagPerActivity(bool tests, bool transition, int expected)
        {
            Assert.AreEqual(expected, FeederMatrixAdapter.ComputePhase(tests, transition));
        }

        [TestCase(true, false, true)]
        [TestCase(false, false, false)]
        [TestCase(true, true, false)]
        [TestCase(false, true, false)]
        public void StopAgentPlay_OnlyStopsAnAgentsPlayOutsideATestRun(bool agentPlay, bool tests, bool expected)
        {
            Assert.AreEqual(expected, FeederMatrixAdapter.ShouldStopAgentPlay(agentPlay, tests));
        }

        [Test]
        public void Heartbeat_SendsAgentPlay()
        {
            var json = FmpJson.Arguments(new HeartbeatMessage { UnityInstanceId = "u", AgentPlay = true });
            StringAssert.Contains("\"agentPlay\":true", json);
        }
    }
}
