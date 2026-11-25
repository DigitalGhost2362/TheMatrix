#nullable enable

using System;
using System.IO;
using Feeder.MCP.Editor.Utils;
using Feeder.ToolHost.Skills;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Utils
{
    public sealed class RetiredSkillTests
    {
        string _root = string.Empty;

        [SetUp]
        public void SetUp() => _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "MxRetiredSkills_" + Guid.NewGuid().ToString("N"))).FullName;

        [TearDown]
        public void TearDown() => Directory.Delete(_root, true);

        string Write(string folder, string text)
        {
            var dir = Directory.CreateDirectory(Path.Combine(_root, folder)).FullName;
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), text);
            return dir;
        }

        [Test]
        public void OldPlaytestGuide_IsRemoved()
        {
            var dir = Write("matrix-playtest", "---\r\nname: matrix-playtest\r\ndescription: \"Play-test the game with the 'playtest' tool, start to finish\"\r\n---\r\n# Playtest\r\n");
            UnitySkillFileGenerator.RemoveRetiredGuides(_root);
            Assert.IsFalse(Directory.Exists(dir));
        }

        [Test]
        public void ToolSkillWithTheOwnMark_IsKept()
        {
            var dir = Write("matrix-playtest", "---\nname: matrix-playtest\ndescription: \"Play-test the game with the 'playtest' tool\"\n---\nRead the " + SkillFileGenerator.OwnSkillMark + ".\n");
            UnitySkillFileGenerator.RemoveRetiredGuides(_root);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "SKILL.md")));
        }

        [Test]
        public void AFileSomeoneElseWrote_IsKept()
        {
            var dir = Write("matrix-playtest", "---\nname: matrix-playtest\ndescription: \"Our own notes\"\n---\n");
            UnitySkillFileGenerator.RemoveRetiredGuides(_root);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "SKILL.md")));
        }
    }
}
