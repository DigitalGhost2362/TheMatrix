#nullable enable
using Feeder.MCP.Editor.API;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Query
{
    public class ScriptExecuteBodyTests
    {
        [TestCase("return go.name;", true)]
        [TestCase("var n = 3;\nreturn n * 2;", true)]
        [TestCase("return(1);", true)]
        [TestCase("return\n  new[] { 1, 2 };", true)]
        [TestCase("go.SetActive(false);", false)]
        [TestCase("if (go == null) return;\ngo.SetActive(false);", false)]
        [TestCase("// return 5;\nDebug.Log(1);", false)]
        [TestCase("/* return 5; */ Debug.Log(1);", false)]
        [TestCase("Debug.Log(\"return 5\");", false)]
        [TestCase("Debug.Log(@\"say \"\"return 5\"\" now\");", false)]
        [TestCase("Debug.Log($\"{x} return 5\");", false)]
        [TestCase("var c = '\\'';\nDebug.Log(c);", false)]
        [TestCase("var returned = 4; Debug.Log(returned);", false)]
        public void BodyReturnsValue_FindsValueReturnsOutsideCommentsAndStrings(string body, bool expected)
        {
            Assert.AreEqual(expected, Tool_Script.BodyReturnsValue(body));
        }

        [Test]
        public void GenerateFullCode_UsesObjectReturnOnlyWhenTheBodyReturnsAValue()
        {
            var withValue = Tool_Script.GenerateFullCode("return 1;", "Script", "Main", null, returnsValue: true);
            StringAssert.Contains("public static object Main()", withValue);
            var withoutValue = Tool_Script.GenerateFullCode("Debug.Log(1);", "Script", "Main", null);
            StringAssert.Contains("public static void Main()", withoutValue);
        }

        [Test]
        public void StripLiterals_KeepsCodeAndBlanksLiterals()
        {
            var stripped = Tool_Script.StripLiterals("var s = \"return 1\"; // return 2\nreturn s;");
            StringAssert.DoesNotContain("return 1", stripped);
            StringAssert.DoesNotContain("return 2", stripped);
            StringAssert.Contains("return s;", stripped);
        }
    }
}
