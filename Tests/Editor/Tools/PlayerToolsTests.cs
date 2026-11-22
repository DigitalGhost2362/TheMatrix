#nullable enable
using System.Linq;
using System.Text;
using Feeder.Json;
using Feeder.MCP.Player;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class PlayerToolsTests
    {
        static string Text(byte[] callToolResult)
        {
            using (var document = JsonDocument.Parse(callToolResult))
                return document.RootElement.GetProperty("content").EnumerateArray().First().GetProperty("text").GetString()!;
        }

        [Test]
        public void Scan_FindsTheSampleToolsAsRuntimeTools()
        {
            var snapshot = MatrixPlayerTools.Scan().Snapshot("p");

            var names = snapshot.Tools.Select(t => t.Name).ToArray();
            CollectionAssert.IsSubsetOf(new[] { "runtime-ping", "runtime-scene-info" }, names);
            Assert.That(snapshot.Tools.All(t => t.Target == "Runtime"));
            Assert.That(snapshot.Tools.First(t => t.Name == "runtime-ping").ReadOnly);
            Assert.IsNotEmpty(snapshot.RegistryHash);
        }

        [Test]
        public void Schema_ListsParametersWithTheirDescriptions()
        {
            var ping = MatrixPlayerTools.Scan().Snapshot("p").Tools.First(t => t.Name == "runtime-ping");

            using (var schema = JsonDocument.Parse(ping.InputSchemaJsonUtf8))
            {
                Assert.AreEqual("object", schema.RootElement.GetProperty("type").GetString());
                var message = schema.RootElement.GetProperty("properties").GetProperty("message");
                Assert.AreEqual("string", message.GetProperty("type").GetString());
                Assert.AreEqual("Text to echo back.", message.GetProperty("description").GetString());
            }
        }

        [Test]
        public void Run_BindsArgumentsAndFallsBackToDefaults()
        {
            var tools = MatrixPlayerTools.Scan();

            Assert.AreEqual("hello", Text(tools.Run("runtime-ping", Encoding.UTF8.GetBytes("{\"message\":\"hello\"}"))));
            Assert.AreEqual("pong", Text(tools.Run("runtime-ping", Encoding.UTF8.GetBytes("{}"))));
        }

        [Test]
        public void Run_ReturnsAnObjectAsStructuredContent()
        {
            var result = MatrixPlayerTools.Scan().Run("runtime-scene-info", Encoding.UTF8.GetBytes("{}"));

            using (var document = JsonDocument.Parse(result))
                Assert.That(document.RootElement.GetProperty("structuredContent").TryGetProperty("scene", out _));
        }

        [Test]
        public void Run_UnknownToolFailsWithToolNotFound()
        {
            var error = Assert.Throws<ToolException>(() => MatrixPlayerTools.Scan().Run("no-such-tool", new byte[0]))!;

            Assert.AreEqual("FMP_TOOL_NOT_FOUND", error.Code);
        }

        [Test]
        public void ProjectIdFor_ReadsPathsTheWayTheEditorLinkDoes()
        {
            var id = MatrixPlayer.ProjectIdFor(@"C:\Work\MyGame\");

            StringAssert.StartsWith("MyGame-", id);
            Assert.AreEqual("MyGame-".Length + 12, id.Length);
            Assert.AreEqual(id, MatrixPlayer.ProjectIdFor("C:/Work/MyGame"));
            Assert.AreEqual(id, MatrixPlayer.ProjectIdFor("c:/work/MyGame"));
        }
    }
}
