#nullable enable

using System.Collections;
using System.Reflection;
using Feeder.MCP.Editor.API;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class GameViewSizeTests
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

        // sizes no user list is likely to hold, so cleanup removes only what the test added
        const int W1 = 1237, H1 = 701, W2 = 703, H2 = 1239;

        EditorWindow? view;
        int selectedBefore;

        [SetUp]
        public void SetUp()
        {
            var views = UnityEngine.Resources.FindObjectsOfTypeAll(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")!);
            view = views.Length > 0 ? (EditorWindow)views[0] : null;
            if (view == null)
                Assert.Ignore("No Game view is open.");
            if (Entries(Tool_GameView.EntryName(W1, H1)) + Entries(Tool_GameView.EntryName(W2, H2)) > 0)
                Assert.Ignore("The size list already holds a test entry.");
            selectedBefore = (int)view.GetType().GetProperty("selectedSizeIndex", Any)!.GetValue(view);
        }

        [TearDown]
        public void TearDown()
        {
            if (view == null)
                return;
            var (sizes, group) = Group();
            var groupType = group.GetType();
            for (var i = (int)groupType.GetMethod("GetTotalCount")!.Invoke(group, null) - 1; i >= 0; i--)
            {
                var name = BaseText(group, i);
                if (name != Tool_GameView.EntryName(W1, H1) && name != Tool_GameView.EntryName(W2, H2))
                    continue;
                groupType.GetMethod("RemoveCustomSize")!.Invoke(group, new object[] { i });
                if (selectedBefore > i)
                    selectedBefore--;
            }
            sizes.GetType().GetMethod("SaveToHDD")!.Invoke(sizes, null);
            view.GetType().GetMethod("SizeSelectionCallback", Any)!.Invoke(view, new object?[] { selectedBefore, null });
            view.Repaint();
        }

        static (object sizes, object group) Group()
        {
            var sizesType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizes")!;
            var sizes = sizesType.GetProperty("instance", Any)!.GetValue(null)!;
            return (sizes, sizesType.GetProperty("currentGroup", Any)!.GetValue(sizes)!);
        }

        static string BaseText(object group, int index)
        {
            var size = group.GetType().GetMethod("GetGameViewSize")!.Invoke(group, new object[] { index });
            return (string)size.GetType().GetProperty("baseText")!.GetValue(size);
        }

        static int Entries(string name)
        {
            var (_, group) = Group();
            var found = 0;
            for (var i = (int)group.GetType().GetMethod("GetTotalCount")!.Invoke(group, null) - 1; i >= 0; i--)
                if (BaseText(group, i) == name)
                    found++;
            return found;
        }

        [Test]
        public void EachSize_KeepsItsOwnEntry_AndSettingItAgainAddsNoDuplicate()
        {
            Assert.IsTrue(Tool_GameView.Apply(W1, H1, out var message), message);
            Assert.AreEqual($"{W1}x{H1}", Tool_GameView.Resolution());
            Assert.IsTrue(Tool_GameView.Apply(W2, H2, out message), message);
            Assert.AreEqual($"{W2}x{H2}", Tool_GameView.Resolution());
            Assert.IsTrue(Tool_GameView.Apply(W1, H1, out message), message);
            Assert.AreEqual($"{W1}x{H1}", Tool_GameView.Resolution());

            Assert.AreEqual(1, Entries(Tool_GameView.EntryName(W1, H1)));
            Assert.AreEqual(1, Entries(Tool_GameView.EntryName(W2, H2)));
        }

        [UnityTest]
        public IEnumerator SetSize_CapturesAtThatSize()
        {
            if (Tool_Screenshot.CaptureGameView(0, out _, out _, out var visible) == null)
                Assert.Ignore("The Game view cannot be captured (behind another tab?): " + visible);
            Assert.IsTrue(Tool_GameView.Apply(W1, H1, out var message), message);
            yield return null;
            yield return null;
            var png = Tool_Screenshot.CaptureGameView(0, out var width, out var height, out var error);
            Assert.IsNotNull(png, error);
            Assert.AreEqual((W1, H1), (width, height));
        }

        [Test]
        public void ZeroOrNegative_IsRefused()
        {
            Assert.IsFalse(Tool_GameView.Apply(1280, 0, out var message));
            StringAssert.Contains("above 0", message);
            Assert.IsFalse(Tool_GameView.Apply(0, 0, out _));
        }
    }
}
