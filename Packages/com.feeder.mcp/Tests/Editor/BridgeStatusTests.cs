#nullable enable

using System;
using Feeder.MCP.Editor.UI;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests
{
    public sealed class BridgeStatusTests
    {
        [TestCase(false, false, false, McpServerStatus.Stopped, null, BridgePhase.Offline)]
        [TestCase(false, false, false, McpServerStatus.Running, null, BridgePhase.Offline)]
        [TestCase(true, false, false, McpServerStatus.Stopped, null, BridgePhase.Linking)]
        [TestCase(false, false, true, McpServerStatus.Stopped, null, BridgePhase.Linking)]
        [TestCase(false, false, false, McpServerStatus.Installing, null, BridgePhase.Linking)]
        [TestCase(false, false, false, McpServerStatus.Starting, null, BridgePhase.Linking)]
        [TestCase(true, true, false, McpServerStatus.Running, null, BridgePhase.Online)]
        [TestCase(false, true, false, McpServerStatus.External, null, BridgePhase.Online)]
        [TestCase(true, true, true, McpServerStatus.Running, "install failed", BridgePhase.Fault)]
        [TestCase(false, false, false, McpServerStatus.Stopped, "install failed", BridgePhase.Fault)]
        public void Resolve_ReturnsExpectedPhase(bool reconnectPending, bool connected, bool connecting, McpServerStatus server, string? error, BridgePhase expected)
        {
            Assert.AreEqual(expected, BridgeStatus.Resolve(reconnectPending, connected, connecting, server, error));
        }

        [Test]
        public void Resolve_NothingInFlight_IsOfflineEvenWhenServerIsRunning()
        {
            Assert.AreEqual(BridgePhase.Offline, BridgeStatus.Resolve(false, false, false, McpServerStatus.Running, null));
        }

        [Test]
        public void Resolve_CrashLoopFault_WinsOverPendingRestart()
        {
            Assert.AreEqual(BridgePhase.Fault, BridgeStatus.Resolve(true, false, false, McpServerStatus.Stopped, "MCP server keeps crashing"));
        }

        [TestCase(true, true, false, null, false, true, BridgePhase.Working)]
        [TestCase(true, false, false, null, false, true, BridgePhase.Linking)]
        [TestCase(true, true, false, null, true, true, BridgePhase.PlayTesting)]
        [TestCase(true, false, true, null, true, false, BridgePhase.PlayTesting)]
        [TestCase(false, false, false, null, true, false, BridgePhase.Offline)]
        [TestCase(true, true, false, "install failed", true, true, BridgePhase.Fault)]
        public void Resolve_AgentActivity_ReturnsExpectedPhase(bool keep, bool connected, bool connecting, string? error, bool agentPlay, bool working, BridgePhase expected)
        {
            Assert.AreEqual(expected, BridgeStatus.Resolve(keep, connected, connecting, McpServerStatus.Running, error, agentPlay, working));
        }

        [Test]
        public void Resolve_EmptyInstallError_IsNotAFault()
        {
            Assert.AreEqual(BridgePhase.Offline, BridgeStatus.Resolve(false, false, false, McpServerStatus.Stopped, string.Empty));
        }

        [Test]
        public void EveryPhase_HasAWordAndAnEnergyInRange()
        {
            foreach (BridgePhase phase in Enum.GetValues(typeof(BridgePhase)))
            {
                var word = BridgeStatus.Word(phase);
                Assert.IsNotEmpty(word);
                Assert.AreEqual(word.ToUpperInvariant(), word);
                Assert.That(BridgeStatus.RainEnergy(phase), Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void RainEnergy_RisesFromOfflineToOnline()
        {
            Assert.Less(BridgeStatus.RainEnergy(BridgePhase.Offline), BridgeStatus.RainEnergy(BridgePhase.Linking));
            Assert.Less(BridgeStatus.RainEnergy(BridgePhase.Linking), BridgeStatus.RainEnergy(BridgePhase.Online));
            Assert.Less(BridgeStatus.RainEnergy(BridgePhase.Online), BridgeStatus.RainEnergy(BridgePhase.Working));
            Assert.Less(BridgeStatus.RainEnergy(BridgePhase.Fault), BridgeStatus.RainEnergy(BridgePhase.Online));
        }

        [Test]
        public void Word_UnknownPhase_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BridgeStatus.Word((BridgePhase)99));
        }
    }
}
