#nullable enable

using Feeder.MCP.Editor.API;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class PingTests
    {
        [Test]
        public void NoMessage_ReturnsPong()
        {
            Assert.AreEqual("pong", new Tool_Ping().Ping());
        }

        [TestCase("hello")]
        [TestCase("  spaced  ")]
        [TestCase("")]
        [TestCase("{\"json\":true}")]
        public void Message_IsEchoedVerbatim(string message)
        {
            Assert.AreEqual(message, new Tool_Ping().Ping(message));
        }
    }
}
