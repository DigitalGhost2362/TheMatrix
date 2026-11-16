#nullable enable

using ComponentDescription = System.ComponentModel.DescriptionAttribute;
using System.Linq;
using Feeder.ToolHost;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class MxProjectToolTests
    {
        sealed class Sample
        {
            [ComponentDescription("Described.")]
            public static void Good([ComponentDescription("x")] int x, [RequestID] string? requestId = null) { }

            public static void NoMethodDescription([ComponentDescription("x")] int x) { }

            [ComponentDescription("Described.")]
            public static void BareParameter(int count) { }
        }

        static System.Reflection.MethodInfo M(string name) => typeof(Sample).GetMethod(name)!;

        [Test]
        public void MissingDescription_NamesTheMethodOrTheParameter()
        {
            Assert.IsNull(MxProjectTool.MissingDescription(M(nameof(Sample.Good))));
            StringAssert.Contains("method", MxProjectTool.MissingDescription(M(nameof(Sample.NoMethodDescription))));
            StringAssert.Contains("'count'", MxProjectTool.MissingDescription(M(nameof(Sample.BareParameter))));
        }

        [Test]
        public void IsProjectTool_ByTitleOrByAssemblyOutsideThePackage()
        {
            Assert.IsTrue(MxProjectTool.IsProjectTool(M(nameof(Sample.Good)), "Project / Inventory"));
            Assert.IsFalse(MxProjectTool.IsProjectTool(M(nameof(Sample.Good)), "Scene / Open"));
            Assert.IsTrue(MxProjectTool.IsProjectTool(typeof(UnityEngine.Debug).GetMethod("Log", new[] { typeof(object) })!, "Scene / Open"));
        }

        [Test]
        public void EveryPackageTool_HasItsDescriptions()
        {
            var tools = MatrixPluginEditor.Instance.Tools;
            Assume.That(tools, Is.Not.Null, "the Matrix plugin is not built");
            var missing = tools!.GetAllTools().OfType<RunTool>()
                .Select(tool => MxProjectTool.MissingDescription(tool.Method))
                .Where(text => text != null)
                .ToList();
            CollectionAssert.IsEmpty(missing);
        }
    }
}
