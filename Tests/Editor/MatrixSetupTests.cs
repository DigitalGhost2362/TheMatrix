#nullable enable

using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests
{
    public sealed class MatrixSetupTests
    {
        static readonly string[] Files = { ".mcp.json", ".codex/config.toml" };

        [TestCase("")]
        [TestCase("Library/\n")]
        [TestCase("Library/\r\nTemp/")]
        public void Revert_RemovesExactlyWhatSetupAppended(string existing)
        {
            var appended = MatrixSetup.BuildIgnoreAppend(existing, Files, out _);
            Assert.AreEqual(existing, MatrixEditorSettings.RemoveFromText(existing + appended, appended));
        }

        [Test]
        public void Revert_KeepsLinesAddedAfterSetup()
        {
            var appended = MatrixSetup.BuildIgnoreAppend("Library/\n", Files, out _);
            var edited = "Library/\n" + appended + "Build/\n";
            Assert.AreEqual("Library/\nBuild/\n", MatrixEditorSettings.RemoveFromText(edited, appended));
        }

        [Test]
        public void Revert_LeavesTextAloneWhenTheBlockWasEdited()
        {
            var appended = MatrixSetup.BuildIgnoreAppend("Library/\n", Files, out _);
            var edited = "Library/\n" + appended.Replace("/.mcp.json", "/other.json");
            Assert.AreEqual(edited, MatrixEditorSettings.RemoveFromText(edited, appended));
        }
    }
}
