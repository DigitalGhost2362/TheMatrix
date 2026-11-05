#nullable enable

using System;
using Feeder.MCP.Editor.API;
using Feeder.ToolHost.Common.Model;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class MenuExecuteTests
    {
        [Test]
        public void MissingPath_ReturnsAnError()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Tool_Menu.Run("Matrix/Does/Not/Exist"));
            StringAssert.Contains("Matrix/Does/Not/Exist", ex!.Message);
        }

        [Test]
        public void MissingPath_ToolAnswersWithAnErrorInsteadOfThrowing()
        {
            var response = new Tool_Menu().Execute("Matrix/Does/Not/Exist");
            Assert.AreEqual(ResponseStatus.Error, response.Status);
        }

        [Test]
        public void List_FindsAKnownMenu()
        {
            string text;
            try
            {
                text = Tool_Menu.Run(null, filter: "Window/General");
            }
            catch (NotSupportedException)
            {
                Assert.Ignore("This Unity version does not expose the menu table.");
                return;
            }

            StringAssert.Contains("Window/General", text);
        }

        [Test]
        public void Filter_IsCaseInsensitiveAndDeduplicated()
        {
            var result = Tool_Menu.Filter(new[] { "Window/General/Console", "Window/General/Console", "Assets/Refresh", "" }, "general");

            CollectionAssert.AreEqual(new[] { "Window/General/Console" }, result);
        }

        [Test]
        public void ExistingItem_Executes()
        {
            try
            {
                StringAssert.Contains("Executed", Tool_Menu.Run("Window/General/Console"));
            }
            catch (InvalidOperationException ex)
            {
                Assert.Ignore("Console menu item is not available here: " + ex.Message);
            }
        }
    }
}
