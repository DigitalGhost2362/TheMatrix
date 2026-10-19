#nullable enable

using System;
using Feeder.MCP.Runtime.Extensions;
using Feeder.MCP.Runtime.Utils;
using Feeder.Reflect.Utils;
using NUnit.Framework;
using MatrixLogLevel = Feeder.MCP.Runtime.Utils.LogLevel;

namespace Feeder.MCP.Editor.Tests.Utils
{
    public sealed class StringUtilsTests
    {
        [TestCase(0, "")]
        [TestCase(1, "  ")]
        [TestCase(3, "      ")]
        [TestCase(-2, "")]
        public void GetPadding_IsTwoSpacesPerLevel(int depth, string expected)
        {
            Assert.AreEqual(expected, StringUtils.GetPadding(depth));
        }

        [TestCase(null, true)]
        [TestCase("", true)]
        [TestCase("null", true)]
        [TestCase(" ", false)]
        [TestCase("Null", false)]
        [TestCase("value", false)]
        public void IsNullOrEmpty_TreatsTheLiteralNullAsEmpty(string? value, bool expected)
        {
            Assert.AreEqual(expected, StringUtils.IsNullOrEmpty(value));
        }

        [TestCase("   ", true)]
        [TestCase("null", true)]
        [TestCase(" x ", false)]
        public void IsNullOrWhiteSpace_TreatsTheLiteralNullAsBlank(string value, bool expected)
        {
            Assert.AreEqual(expected, StringUtils.IsNullOrWhiteSpace(value));
        }

        [TestCase("/Root/Arm/", "Root/Arm")]
        [TestCase("Root", "Root")]
        [TestCase("///", "")]
        [TestCase("", "")]
        [TestCase(null, null)]
        public void TrimPath_RemovesOuterSlashesOnly(string? path, string? expected)
        {
            Assert.AreEqual(expected, StringUtils.TrimPath(path));
        }

        [Test]
        public void ParseParent_SplitsAtTheLastSlash()
        {
            Assert.IsTrue(StringUtils.Path_ParseParent("/Root/Arm/Hand/", out var parent, out var name));
            Assert.AreEqual("Root/Arm", parent);
            Assert.AreEqual("Hand", name);
        }

        [Test]
        public void ParseParent_SingleSegment_HasNoParent()
        {
            Assert.IsFalse(StringUtils.Path_ParseParent("Hand", out var parent, out var name));
            Assert.IsNull(parent);
            Assert.AreEqual("Hand", name);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("//")]
        public void ParseParent_Empty_ReturnsNothing(string? path)
        {
            Assert.IsFalse(StringUtils.Path_ParseParent(path, out var parent, out var name));
            Assert.IsNull(parent);
            Assert.IsNull(name);
        }

        [TestCase("Assets/Scripts/Player.cs", "Player.cs")]
        [TestCase("Assets/Scripts/", "Scripts")]
        [TestCase("Player.cs", "Player.cs")]
        public void GetLastName_ReturnsTheFinalSegment(string path, string expected)
        {
            Assert.AreEqual(expected, StringUtils.Path_GetLastName(path));
        }

        [TestCase("Assets/Scripts/Player.cs", "Assets/Scripts")]
        [TestCase("Assets/Scripts/", "Assets")]
        public void GetParentFolderPath_DropsTheFinalSegment(string path, string expected)
        {
            Assert.AreEqual(expected, StringUtils.Path_GetParentFolderPath(path));
        }

        [TestCase("Friday", DayOfWeek.Friday)]
        [TestCase("friday", DayOfWeek.Friday)]
        [TestCase("SUNDAY", DayOfWeek.Sunday)]
        public void ConvertParameterStringToEnum_ParsesNamesIgnoringCase(string text, DayOfWeek expected)
        {
            Assert.AreEqual(expected, StringUtils.ConvertParameterStringToEnum(text, typeof(DayOfWeek), "day"));
        }

        [TestCase("Funday")]
        [TestCase("42")]
        public void ConvertParameterStringToEnum_UnknownValue_NamesTheParameter(string text)
        {
            var error = Assert.Throws<ArgumentException>(() => StringUtils.ConvertParameterStringToEnum(text, typeof(DayOfWeek), "day"));

            StringAssert.Contains("'day'", error!.Message);
            StringAssert.Contains("Monday", error.Message);
        }

        [Test]
        public void ConvertParameterStringToEnum_NonString_Throws()
        {
            Assert.Throws<ArgumentException>(() => StringUtils.ConvertParameterStringToEnum(3, typeof(DayOfWeek), "day"));
        }

        [Test]
        public void JoinHelpers_FormatLists()
        {
            var words = new[] { "a", "b", "c" };

            Assert.AreEqual("a, b, c", words.Join());
            Assert.AreEqual("a|b|c", words.Join("|"));
            Assert.AreEqual("'a', 'b', 'c'", words.JoinEnclose());
            Assert.AreEqual("a, c", words.JoinExcept("b"));
        }

        [Test]
        public void JoinEncloseExcept_SkipsAndWraps()
        {
            Assert.AreEqual("\"a\";\"b\"", new[] { "a", "b", "c" }.JoinEncloseExcept("c", ";", "\""));
        }

        [Test]
        public void ExtractProcessId_ReadsTheLockingProcess()
        {
            Assert.IsTrue(ErrorUtils.ExtractProcessId("IOException: The file is locked by: \"Unity Editor (4821)\"", out var pid));
            Assert.AreEqual(4821, pid);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("The file is locked by someone")]
        public void ExtractProcessId_NoMatch_ReturnsMinusOne(string error)
        {
            Assert.IsFalse(ErrorUtils.ExtractProcessId(error, out var pid));
            Assert.AreEqual(-1, pid);
        }

        [TestCase(MatrixLogLevel.Trace, MatrixLogLevel.Debug, true)]
        [TestCase(MatrixLogLevel.Warning, MatrixLogLevel.Error, true)]
        [TestCase(MatrixLogLevel.Warning, MatrixLogLevel.Warning, true)]
        [TestCase(MatrixLogLevel.Error, MatrixLogLevel.Warning, false)]
        [TestCase(MatrixLogLevel.None, MatrixLogLevel.Exception, false)]
        public void LogLevel_IsEnabled_ComparesAgainstTheThreshold(MatrixLogLevel threshold, MatrixLogLevel level, bool expected)
        {
            Assert.AreEqual(expected, threshold.IsEnabled(level));
        }
    }
}
