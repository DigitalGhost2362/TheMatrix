#nullable enable
using Feeder.MCP.Editor.API;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class ScreenshotSizeTests
    {
        [TestCase(1920, 1080, 1024, 1024, 576)]
        [TestCase(1080, 1920, 1024, 576, 1024)]
        [TestCase(800, 600, 1024, 800, 600)]
        [TestCase(1920, 1080, 0, 1920, 1080)]
        [TestCase(1920, 1080, 640, 640, 360)]
        public void ApplyMaxResolution_ScalesTheLongestSideDown(int width, int height, int max, int expectedWidth, int expectedHeight)
        {
            Assert.AreEqual((expectedWidth, expectedHeight), Tool_Screenshot.ApplyMaxResolution(width, height, max));
        }

        // Texture2D y = 0 is the bottom row, so the first row of tiles (Front, Right, Back) sits at y = tile height.
        [TestCase(0, 0, 100)]
        [TestCase(1, 100, 100)]
        [TestCase(2, 200, 100)]
        [TestCase(3, 0, 0)]
        [TestCase(4, 100, 0)]
        [TestCase(5, 200, 0)]
        public void SurroundTiles_FillTheGridTopRowFirst(int index, int x, int y)
        {
            Assert.AreEqual((x, y), Tool_Screenshot.QuadrantOffset(index, 100, 3, 2));
        }

        [Test]
        public void CompositeTiles_KeepTheirOldLayout()
        {
            Assert.AreEqual((0, 100), Tool_Screenshot.QuadrantOffset(0, 100, 2, 2));
            Assert.AreEqual((100, 100), Tool_Screenshot.QuadrantOffset(1, 100, 2, 2));
            Assert.AreEqual((0, 0), Tool_Screenshot.QuadrantOffset(2, 100, 2, 2));
            Assert.AreEqual((100, 0), Tool_Screenshot.QuadrantOffset(3, 100, 2, 2));
        }
    }
}
