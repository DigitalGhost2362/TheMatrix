#nullable enable

using System;
using System.Collections;
using System.Text.RegularExpressions;
using Feeder.MCP.Editor.Activity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Feeder.MCP.Editor.Tests
{
    public sealed class PlaySessionGuardTests
    {
        const string AgentPlayKey = "Feeder_Matrix_AgentPlay";

        int _runs;

        void Count() => _runs++;

        [SetUp]
        public void SetUp() => _runs = 0;

        [TearDown]
        public void TearDown() => PlaySessionGuard.Flush();

        [Test]
        public void Arm_TheSameCleanupTwice_RunsItOnce()
        {
            PlaySessionGuard.Arm(Count);
            PlaySessionGuard.Arm(Count);

            PlaySessionGuard.Flush();

            Assert.AreEqual(1, _runs);
        }

        [Test]
        public void Flush_LeavesNothingOnTheUpdateLoop()
        {
            PlaySessionGuard.Arm(Count);
            Assert.IsTrue(PlaySessionGuard.IsArmed);

            PlaySessionGuard.Flush();
            PlaySessionGuard.Flush();

            Assert.IsFalse(PlaySessionGuard.IsArmed);
            Assert.AreEqual(1, _runs);
        }

        [Test]
        public void Flush_ACleanupThatThrows_StillRunsTheNext()
        {
            LogAssert.Expect(LogType.Exception, new Regex("guard test"));
            PlaySessionGuard.Arm(() => throw new InvalidOperationException("guard test"));
            PlaySessionGuard.Arm(Count);

            PlaySessionGuard.Flush();

            Assert.AreEqual(1, _runs);
        }

        [UnityTest]
        public IEnumerator Arm_OutsidePlayMode_CleansUpWithinAFewTicks()
        {
            PlaySessionGuard.Arm(Count);

            for (var i = 0; i < 10 && PlaySessionGuard.IsArmed; i++)
                yield return null;

            Assert.IsFalse(PlaySessionGuard.IsArmed);
            Assert.AreEqual(1, _runs);
        }

        // Unity refusing to enter play mode sends no EnteredEditMode, so only the guard can clear the mark
        [UnityTest]
        public IEnumerator MarkAgentPlay_WithoutPlayMode_IsClearedWithinAFewTicks()
        {
            Assume.That(SessionState.GetBool(AgentPlayKey, false), Is.False);

            AgentActivity.MarkAgentPlay();
            Assert.IsTrue(SessionState.GetBool(AgentPlayKey, false));

            for (var i = 0; i < 10 && SessionState.GetBool(AgentPlayKey, false); i++)
                yield return null;

            Assert.IsFalse(SessionState.GetBool(AgentPlayKey, false));
            Assert.IsFalse(AgentActivity.IsAgentPlay);
        }
    }
}
