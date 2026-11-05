#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text;
using Feeder.MCP.Editor.API;
using Feeder.MCP.Editor.Utils;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class ScriptEditTests
    {
        string _path = null!;

        [SetUp]
        public void SetUp() => _path = Path.Combine(Path.GetTempPath(), "MxEdit_" + Guid.NewGuid().ToString("N") + ".cs");

        [TearDown]
        public void TearDown()
        {
            foreach (var p in new[] { _path, _path + ".mxtmp" })
                if (File.Exists(p))
                    File.Delete(p);
        }

        [Test]
        public void Sha256_MatchesAnIndependentHash()
        {
            var bytes = Encoding.UTF8.GetBytes("abc");
            Assert.AreEqual("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", ScriptEdit.Sha256(bytes));
        }

        [Test]
        public void Replace_UniqueMatch_Replaces()
        {
            Assert.IsTrue(ScriptEdit.Replace("int A = 2;", "A = 2", "A = 3", false, out var result, out var error));
            Assert.AreEqual("int A = 3;", result);
            Assert.IsNull(error);
        }

        [Test]
        public void Replace_NoMatch_IsAnchorNotFound()
        {
            Assert.IsFalse(ScriptEdit.Replace("int A = 2;", "B = 2", "x", false, out _, out var error));
            StringAssert.StartsWith("[anchor_not_found]", error);
        }

        [Test]
        public void Replace_TwoMatches_IsAmbiguousUnlessReplaceAll()
        {
            Assert.IsFalse(ScriptEdit.Replace("a a", "a", "b", false, out _, out var error));
            StringAssert.StartsWith("[anchor_ambiguous count=2]", error);

            Assert.IsTrue(ScriptEdit.Replace("a a", "a", "b", true, out var result, out _));
            Assert.AreEqual("b b", result);
        }

        [Test]
        public void Replace_LfAnchorMatchesCrlfFile_AndKeepsCrlf()
        {
            Assert.IsTrue(ScriptEdit.Replace("one\r\ntwo\r\nthree", "one\ntwo", "1\n2", false, out var result, out _));
            Assert.AreEqual("1\r\n2\r\nthree", result);
        }

        [Test]
        public void WriteAtomic_KeepsBom_AndLeavesNoTempFile()
        {
            File.WriteAllBytes(_path, new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a' });
            var bytes = File.ReadAllBytes(_path);
            Assert.IsTrue(ScriptEdit.HasBom(bytes));

            ScriptEdit.WriteAtomic(_path, "bc", ScriptEdit.HasBom(bytes));

            var written = File.ReadAllBytes(_path);
            Assert.IsTrue(ScriptEdit.HasBom(written));
            Assert.AreEqual("bc", ScriptEdit.ReadText(written));
            Assert.IsFalse(File.Exists(_path + ".mxtmp"));
        }

        [Test]
        public void WriteAtomic_NewFileWithoutBom_HasNoBom()
        {
            ScriptEdit.WriteAtomic(_path, "x", false);
            Assert.IsFalse(ScriptEdit.HasBom(File.ReadAllBytes(_path)));
        }

        [Test]
        public void FormatDiagnostic_GivesLineColumnAndCode()
        {
            Assert.AreEqual("3:12 CS0103 The name 'x' does not exist", ScriptEdit.FormatDiagnostic("(3,12): error CS0103: The name 'x' does not exist"));
        }

        [Test]
        public void Tool_NoContentAndNoOldText_IsAnErrorAndLeavesTheFileAlone()
        {
            File.WriteAllText(_path, "class A { }");

            var response = Tool_Script.UpdateOrCreate(_path, oldText: null, newText: "x", requestId: "t-nocontent");

            StringAssert.Contains("Nothing to write", response.GetMessage());
            Assert.AreEqual("class A { }", File.ReadAllText(_path));
        }

        [Test]
        public void Tool_OldTextWithoutContent_NeverEmptiesTheFile()
        {
            File.WriteAllText(_path, "class A { int a = 2; }");

            var response = Tool_Script.UpdateOrCreate(_path, oldText: "a = 9", newText: "a = 3", requestId: "t-anchor");

            StringAssert.Contains("[anchor_not_found]", response.GetMessage());
            Assert.AreEqual("class A { int a = 2; }", File.ReadAllText(_path));
        }

        [Test]
        public void Tool_StaleHash_IsRefusedWithTheCurrentHash()
        {
            File.WriteAllText(_path, "class A { }");
            var current = ScriptEdit.Sha256(File.ReadAllBytes(_path));

            var response = Tool_Script.UpdateOrCreate(_path, "class B { }", "t-stale", expectedSha256: "0000stale");

            StringAssert.Contains($"[stale_file current_sha256={current}]", response.GetMessage());
            Assert.AreEqual("class A { }", File.ReadAllText(_path));
        }

        [Test]
        public void Tool_ContentAndOldText_AreRefused()
        {
            File.WriteAllText(_path, "class A { }");

            var response = Tool_Script.UpdateOrCreate(_path, "class B { }", "t-both", oldText: "A", newText: "C");

            StringAssert.Contains("not both", response.GetMessage());
            Assert.AreEqual("class A { }", File.ReadAllText(_path));
        }

        [Test]
        public void Tool_UnknownValidateLevel_IsRefused()
        {
            var response = Tool_Script.UpdateOrCreate(_path, "class A { }", "t-validate", validate: "deep");

            StringAssert.Contains("validate must be", response.GetMessage());
            Assert.IsFalse(File.Exists(_path));
        }

        [Test]
        public void Read_IncludeSha256_AppendsTheHashOfTheWholeFile()
        {
            File.WriteAllText(_path, "l1\nl2");
            var hash = ScriptEdit.Sha256(File.ReadAllBytes(_path));

            var text = Tool_Script.Read(_path, 1, 1, includeSha256: true);

            Assert.AreEqual($"l1\n[sha256 of the whole file: {hash}]", text);
        }

        [Test]
        public void Validate_Semantic_FindsTypeErrorsThatSyntaxMisses()
        {
            const string code = "public class P { void M() { int x = \"s\"; Missing(); } }";

            var syntax = Tool_Script.Validate(content: code, semantic: false);
            if (syntax.StartsWith("SKIPPED"))
                Assert.Ignore("Compile service is not reachable.");
            StringAssert.StartsWith("OK", syntax);

            var semantic = Tool_Script.Validate(content: code, semantic: true);
            StringAssert.Contains("CS0029", semantic);
            StringAssert.Contains("CS0103", semantic);
            StringAssert.Contains("1:", semantic);
        }

        [Test]
        public void Validate_CleanCode_IsOk()
        {
            var result = Tool_Script.Validate(content: "public class P { public int M() { return 1; } }");
            if (result.StartsWith("SKIPPED"))
                Assert.Ignore("Compile service is not reachable.");
            StringAssert.StartsWith("OK", result);
        }

        [Test]
        public void Validate_NeitherContentNorPath_Throws()
        {
            Assert.Throws<Feeder.ToolHost.ToolCallerException>(() => Tool_Script.Validate());
        }
    }
}
