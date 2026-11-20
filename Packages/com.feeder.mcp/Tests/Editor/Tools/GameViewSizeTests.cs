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
        const int W1 = 1237, H1 = 701, W2 = 703, H2 = 1239, W3 = 1171, H3 = 643;
        const string Fixed = "MxTest Fixed";

        EditorWindow? view;
        int selectedBefore;

        [SetUp]
        public void SetUp()
        {
            var views = UnityEngine.Resources.FindObjectsOfTypeAll(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")!);
            view = views.Length > 0 ? (EditorWindow)views[0] : null;
            if (view == null)
                Assert.Ignore("No Game view is open.");
            if (Entries(Tool_GameView.EntryName(W1, H1)) + Entries(Tool_GameView.EntryName(W2, H2)) + Entries(Fixed) > 0)
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
                if (name != Tool_GameView.EntryName(W1, H1) && name != Tool_GameView.EntryName(W2, H2) && name != Fixed)
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

        string Rendered()
        {
            var size = (UnityEngine.Vector2)view!.GetType().GetProperty("targetRenderSize", Any)!.GetValue(view);
            return $"{size.x:0}x{size.y:0}";
        }

        // a fixed entry other than Matrix: the answer once read a size that lagged behind the view
        [Test]
        public void Answer_NamesTheRenderedSizes_FromAFixedEntryThatIsNotMatrix()
        {
            var (_, group) = Group();
            var editor = typeof(EditorWindow).Assembly;
            var kind = System.Enum.Parse(editor.GetType("UnityEditor.GameViewSizeType")!, "FixedResolution");
            group.GetType().GetMethod("AddCustomSize")!.Invoke(group, new[] { System.Activator.CreateInstance(editor.GetType("UnityEditor.GameViewSize")!, kind, W3, H3, Fixed) });
            var index = (int)group.GetType().GetMethod("GetTotalCount")!.Invoke(group, null) - 1;
            view!.GetType().GetMethod("SizeSelectionCallback", Any)!.Invoke(view, new object?[] { index, null });
            Assert.AreEqual($"{W3}x{H3}", Rendered());

            Assert.IsTrue(Tool_GameView.Apply(W1, H1, out var message), message);
            StringAssert.Contains($"{W3}x{H3} ({Fixed}) -> {W1}x{H1} ({Tool_GameView.EntryName(W1, H1)})", message);
            Assert.AreEqual($"{W1}x{H1}", Rendered());

            Assert.IsTrue(Tool_GameView.Apply(W2, H2, out message), message);
            StringAssert.Contains($"{W1}x{H1} ({Tool_GameView.EntryName(W1, H1)}) -> {W2}x{H2} ({Tool_GameView.EntryName(W2, H2)})", message);
            Assert.AreEqual($"{W2}x{H2}", Rendered());
            Assert.AreEqual(Rendered(), Tool_GameView.Resolution());
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

        // read in the same frame as the size change, before any repaint: an old frame must never come back silently
        [Test]
        public void CaptureRightAfterASizeChange_IsAtTheNewSize_OrSaysItHasNotRedrawn()
        {
            if (Tool_Screenshot.CaptureGameView(0, out _, out _, out var visible) == null)
                Assert.Ignore("The Game view cannot be captured (behind another tab?): " + visible);
            Assert.IsTrue(Tool_GameView.Apply(W2, H2, out var message), message);

            var png = Tool_Screenshot.CaptureGameView(0, out var width, out var height, out var error);

            if (png == null)
                StringAssert.Contains("has not redrawn at the new size", error);
            else
                Assert.AreEqual((W2, H2), (width, height));
        }

        [Test]
        public void SmallMaxResolution_ShrinksTheReturnedImage_NotTheSavedFile()
        {
            if (Tool_Screenshot.CaptureGameView(0, out _, out _, out var visible) == null)
                Assert.Ignore("The Game view cannot be captured (behind another tab?): " + visible);
            Assert.IsTrue(Tool_GameView.Apply(W1, H1, out var message), message);
            const string Folder = "Temp/MxTestShot";
            try
            {
                var response = new Tool_Screenshot().ScreenshotGameView(null, 256, Folder + "/m3.png");

                var png = System.IO.File.ReadAllBytes(Folder + "/m3.png");
                int Big(int at) => (png[at] << 24) | (png[at + 1] << 16) | (png[at + 2] << 8) | png[at + 3];
                Assert.AreEqual((W1, H1), (Big(16), Big(20)), "the saved file keeps the Game view size");
                StringAssert.Contains($"({256}x{UnityEngine.Mathf.RoundToInt(H1 * 256f / W1)})", response.GetMessage());
                StringAssert.Contains($"({W1}x{H1})", response.GetMessage());
            }
            finally
            {
                if (System.IO.Directory.Exists(Folder))
                    System.IO.Directory.Delete(Folder, true);
            }
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
