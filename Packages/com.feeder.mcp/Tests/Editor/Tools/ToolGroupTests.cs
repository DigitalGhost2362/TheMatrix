#nullable enable
using System.Linq;
using System.Threading;
using Feeder.MCP.Data;
using Feeder.MCP.Editor.API;
using Feeder.MCP.Editor.UI;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class ToolGroupTests
    {
        const int ThrottleWindowMs = 150;

        [TestCase("Screenshot / Game View", "Vision")]
        [TestCase("Console / Get Logs", "Editor")]
        [TestCase("Script / Read", "Code & Tests")]
        [TestCase(null, "Other")]
        public void GroupName_FollowsTheSkillsWindow(string? title, string expected)
        {
            Assert.AreEqual(expected, SkillCatalog.GroupName(title, "some-tool"));
        }

        [Test]
        public void List_ShowsEachToolsGroup()
        {
            Assume.That(MatrixPluginEditor.Instance.Tools, Is.Not.Null, "Matrix Bridge is not active.");

            var shot = new Tool_Tool().List($"^{Tool_Screenshot.ScreenshotIsolatedToolId}$").Single();

            Assert.AreEqual("Vision", shot.Group);
        }

        [Test]
        public void SetEnabledState_UnknownGroupFails()
        {
            Assume.That(MatrixPluginEditor.Instance.Tools, Is.Not.Null, "Matrix Bridge is not active.");

            var result = new Tool_Tool().SetEnabledState(new[] { new ToolToggleInput { Name = "group:NoSuchGroup", Enabled = false } });

            Assert.IsFalse(result.Success["group:NoSuchGroup"]);
        }

        [TestCase("group:Vision")]
        [TestCase("vision")]
        public void SetEnabledState_SwitchesAWholeGroupAndBack(string name)
        {
            var manager = MatrixPluginEditor.Instance.Tools;
            Assume.That(manager, Is.Not.Null, "Matrix Bridge is not active.");
            var members = manager!.GetAllTools()!.Where(t => SkillCatalog.GroupName(t.Title, t.Name) == "Vision").Select(t => t.Name).ToList();
            Assume.That(members, Is.Not.Empty);
            var before = members.ToDictionary(n => n, n => manager.IsToolEnabled(n));
            var tool = new Tool_Tool();
            try
            {
                var off = tool.SetEnabledState(new[] { new ToolToggleInput { Name = name, Enabled = false } });
                Assert.IsTrue(off.Success[name]);
                Assert.IsTrue(members.All(n => !manager.IsToolEnabled(n)));
            }
            finally
            {
                Thread.Sleep(ThrottleWindowMs);
                foreach (var pair in before)
                    manager.SetToolEnabled(pair.Key, pair.Value);
            }
        }
    }
}
