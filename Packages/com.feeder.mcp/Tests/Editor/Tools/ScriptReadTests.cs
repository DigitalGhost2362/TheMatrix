#nullable enable

using System;
using System.IO;
using Feeder.MCP.Editor.API;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class ScriptReadTests
    {
        string _path = null!;

        [SetUp]
        public void WriteScript()
        {
            _path = Path.Combine(Path.GetTempPath(), "MxTest_" + Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(_path, "line1\nline2\nline3\nline4\nline5");
        }

        [TearDown]
        public void DeleteScript()
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }

        [Test]
        public void Defaults_ReturnTheWholeFile()
        {
            Assert.AreEqual("line1\nline2\nline3\nline4\nline5", Tool_Script.Read(_path));
        }

        [Test]
        public void Range_IsInclusiveAndOneBased()
        {
            Assert.AreEqual("line2\nline3", Tool_Script.Read(_path, 2, 3));
        }

        [Test]
        public void WindowsLineEndings_AreNormalized()
        {
            File.WriteAllText(_path, "a\r\nb\r\nc");

            Assert.AreEqual("a\nb\nc", Tool_Script.Read(_path));
        }

        [TestCase(0, 2, "line1\nline2")]
        [TestCase(9, 2, "line1\nline2")]
        [TestCase(4, 99, "line4\nline5")]
        [TestCase(4, 2, "line2")]
        [TestCase(5, 0, "line5")]
        public void OutOfRangeBounds_AreClamped(int from, int to, string expected)
        {
            Assert.AreEqual(expected, Tool_Script.Read(_path, from, to));
        }

        [Test]
        public void EmptyPath_Throws()
        {
            Assert.Throws<ArgumentException>(() => Tool_Script.Read(string.Empty));
        }

        [Test]
        public void NonCsPath_Throws()
        {
            Assert.Throws<ArgumentException>(() => Tool_Script.Read(Path.ChangeExtension(_path, ".txt")));
        }

        [Test]
        public void MissingFile_Throws()
        {
            Assert.Throws<FileNotFoundException>(() => Tool_Script.Read(_path.Replace("MxTest_", "MxTest_Gone_")));
        }
    }
}
