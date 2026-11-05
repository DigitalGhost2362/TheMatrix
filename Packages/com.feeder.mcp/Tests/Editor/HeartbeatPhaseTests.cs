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
    }
}
