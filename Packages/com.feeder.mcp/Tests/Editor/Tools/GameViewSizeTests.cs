#nullable enable

using System.Collections;
using Feeder.MCP.Editor.API;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class GameViewSizeTests
    {
        const string BeforeKey = "Feeder.MCP.GameViewSize.Before";

        // a size a session set and has not restored yet belongs to that session
        static void IgnoreWhenASessionHoldsASize()
        {
            if (SessionState.GetInt(BeforeKey, -1) >= 0)
                Assert.Ignore("A session changed the Game view size and has not restored it.");
        }

        [UnityTest]
        public IEnumerator SetSize_ThenRestore_CapturesAtThatSize_AndPutsBackTheOldSize()
        {
            IgnoreWhenASessionHoldsASize();
            if (Tool_Screenshot.CaptureGameView(0, out _, out _, out var visible) == null)
                Assert.Ignore("The Game view cannot be captured (behind another tab?): " + visible);
            var before = Tool_GameView.Resolution();
            if (!Tool_GameView.Apply(1280, 720, out var message))
                Assert.Ignore(message);
            try
            {
                yield return null;
                yield return null;
                var png = Tool_Screenshot.CaptureGameView(0, out var width, out var height, out var error);
                Assert.IsNotNull(png, error);
                Assert.AreEqual((1280, 720), (width, height));
                Assert.AreEqual("1280x720", Tool_GameView.Resolution());
            }
            finally
            {
                Tool_GameView.Apply(0, 0, out _);
            }
            yield return null;
            Assert.AreEqual(before, Tool_GameView.Resolution());
        }

        [Test]
        public void OneSideZero_IsRefused()
        {
            Assert.IsFalse(Tool_GameView.Apply(1280, 0, out var message));
            StringAssert.Contains("both", message);
        }

        [Test]
        public void Restore_WithNothingSaved_ChangesNothing()
        {
            IgnoreWhenASessionHoldsASize();
            var before = Tool_GameView.Resolution();
            Assert.IsTrue(Tool_GameView.Apply(0, 0, out var message), message);
            Assert.AreEqual(before, Tool_GameView.Resolution());
        }
    }
}
