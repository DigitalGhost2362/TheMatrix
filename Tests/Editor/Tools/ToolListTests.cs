#nullable enable

using System;
using System.Linq;
using Feeder.MCP.Editor.API;
using Feeder.ToolHost;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class ToolListTests
    {
        IToolManager _tools = null!;
        readonly Tool_Tool _tool = new Tool_Tool();

        [SetUp]
        public void RequireToolHost()
        {
            Assume.That(MatrixPluginEditor.Instance.Tools, Is.Not.Null, "Matrix Bridge is not active, so no tool manager is built.");
            _tools = MatrixPluginEditor.Instance.Tools!;
        }

        [Test]
        public void NoFilter_ListsEveryRegisteredTool()
        {
            var listed = _tool.List().Select(t => t.Name).ToArray();

            CollectionAssert.AreEquivalent(_tools.GetAllTools().Select(t => t.Name).ToArray(), listed);
            CollectionAssert.Contains(listed, Tool_Tool.ToolListId);
            CollectionAssert.Contains(listed, Tool_Tool.ToolSetEnabledStateId);
        }

        [Test]
        public void EnabledFlag_MirrorsTheToolManager()
        {
            foreach (var info in _tool.List())
                Assert.AreEqual(_tools.IsToolEnabled(info.Name), info.Enabled, info.Name);
        }

        [Test]
        public void Defaults_LeaveOutDescriptionsAndInputs()
        {
            foreach (var info in _tool.List())
            {
                Assert.IsNull(info.Description, info.Name);
                Assert.IsNull(info.Inputs, info.Name);
            }
        }

        [Test]
        public void IncludeDescription_FillsTheToolsDescription()
        {
            var info = _tool.List("^unity-tool-list$", includeDescription: true).Single();
            var expected = _tools.GetAllTools().Single(t => t.Name == Tool_Tool.ToolListId).Description;

            Assert.IsFalse(string.IsNullOrEmpty(info.Description));
            Assert.AreEqual(expected, info.Description);
        }

        [Test]
        public void IncludeInputs_ListsArgumentNamesWithoutDescriptions()
        {
            var info = _tool.List("^unity-tool-list$", includeInputs: Tool_Tool.InputRequest.Inputs).Single();
            var names = info.Inputs!.Select(i => i.Name).ToArray();

            CollectionAssert.IsSupersetOf(names, new[] { "regexSearch", "includeDescription", "includeInputs" });
            Assert.IsTrue(info.Inputs!.All(i => i.Description == null));
        }

        [Test]
        public void IncludeInputsWithDescription_FillsArgumentDescriptions()
        {
            var info = _tool.List("^unity-tool-list$", includeInputs: Tool_Tool.InputRequest.InputsWithDescription).Single();
            var regex = info.Inputs!.Single(i => i.Name == "regexSearch");

            StringAssert.Contains("Regex", regex.Description);
        }

        [Test]
        public void Regex_ExactName_ReturnsOnlyThatTool()
        {
            var result = _tool.List("^unity-tool-list$");

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(Tool_Tool.ToolListId, result[0].Name);
        }

        [Test]
        public void Regex_IsCaseInsensitive()
        {
            Assert.AreEqual(Tool_Tool.ToolListId, _tool.List("^UNITY-TOOL-LIST$").Single().Name);
        }

        [Test]
        public void Regex_MatchesArgumentNames()
        {
            CollectionAssert.Contains(_tool.List("^includeInputs$").Select(t => t.Name).ToArray(), Tool_Tool.ToolListId);
        }

        [Test]
        public void Regex_WithoutMatch_IsEmpty()
        {
            Assert.IsEmpty(_tool.List("^mx-no-such-tool-[0-9]{12}$"));
        }

        [Test]
        public void Regex_Blank_MeansNoFilter()
        {
            Assert.AreEqual(_tools.GetAllTools().Count(), _tool.List("   ").Length);
        }

        [Test]
        public void Regex_Invalid_ThrowsArgumentException()
        {
            var error = Assert.Throws<ArgumentException>(() => _tool.List("(["));

            StringAssert.Contains("([", error!.Message);
        }
    }
}
