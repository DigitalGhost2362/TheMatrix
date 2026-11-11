#nullable enable

using Feeder.Json.Nodes;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests
{
    public sealed class MatrixAgentHookTests
    {
        const string Url = "http://127.0.0.1:25213/hooks/claude";
        const string Entry = "Feeder-MCP";

        static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

        [Test]
        public void Install_AddsOneGroupPerEvent()
        {
            var hooks = Parse(MatrixAgentHook.Apply(null, Url, Entry, null))["hooks"]!.AsObject();

            foreach (var name in new[] { "PreToolUse", "PostToolUse", "PostToolUseFailure", "SubagentStart", "SubagentStop" })
            {
                var groups = hooks[name]!.AsArray();
                Assert.AreEqual(1, groups.Count, name);
                var hook = groups[0]!["hooks"]![0]!;
                Assert.AreEqual("http", hook["type"]!.GetValue<string>(), name);
                Assert.AreEqual(Url, hook["url"]!.GetValue<string>(), name);
            }
            Assert.AreEqual("mcp__Feeder-MCP__.*|Agent|Task", hooks["PreToolUse"]![0]!["matcher"]!.GetValue<string>());
            Assert.IsNull(hooks["SubagentStop"]![0]!["matcher"]);
        }

        [Test]
        public void Install_Twice_FollowsThePortAndStaysOneGroup()
        {
            var first = MatrixAgentHook.Apply(null, "http://127.0.0.1:20270/hooks/claude", Entry, null);
            var hooks = Parse(MatrixAgentHook.Apply(first, Url, Entry, null))["hooks"]!.AsObject();

            Assert.AreEqual(1, hooks["PreToolUse"]!.AsArray().Count);
            Assert.AreEqual(Url, hooks["PreToolUse"]![0]!["hooks"]![0]!["url"]!.GetValue<string>());
        }

        [Test]
        public void Install_KeepsWhatTheFileAlreadyHolds()
        {
            const string existing = @"{""permissions"":{""allow"":[""mcp__Feeder-MCP__ping""]},""hooks"":{""PreToolUse"":[{""matcher"":""Bash"",""hooks"":[{""type"":""command"",""command"":""echo hi""}]}]}}";

            var root = Parse(MatrixAgentHook.Apply(existing, Url, Entry, null));

            Assert.AreEqual("mcp__Feeder-MCP__ping", root["permissions"]!["allow"]![0]!.GetValue<string>());
            var groups = root["hooks"]!["PreToolUse"]!.AsArray();
            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual("Bash", groups[0]!["matcher"]!.GetValue<string>());
        }

        [Test]
        public void Remove_TakesOutOnlyTheMatrixHook()
        {
            const string existing = @"{""permissions"":{""allow"":[]},""hooks"":{""PreToolUse"":[{""matcher"":""Bash"",""hooks"":[{""type"":""command"",""command"":""echo hi""}]}]}}";
            var installed = MatrixAgentHook.Apply(existing, Url, Entry, null);

            var root = Parse(MatrixAgentHook.Apply(installed, null, Entry, null));

            var hooks = root["hooks"]!.AsObject();
            Assert.AreEqual(1, hooks.Count);
            Assert.AreEqual("Bash", hooks["PreToolUse"]![0]!["matcher"]!.GetValue<string>());
            Assert.IsNotNull(root["permissions"]);
        }

        [Test]
        public void Remove_LeavesNoEmptyHooksObject()
        {
            var installed = MatrixAgentHook.Apply(@"{""permissions"":{}}", Url, Entry, null);

            var root = Parse(MatrixAgentHook.Apply(installed, null, Entry, null));

            Assert.IsNull(root["hooks"]);
            Assert.IsNotNull(root["permissions"]);
        }

        [Test]
        public void Token_GoesIntoTheAuthorizationHeader()
        {
            var hooks = Parse(MatrixAgentHook.Apply(null, Url, Entry, "secret"))["hooks"]!;

            Assert.AreEqual("Bearer secret", hooks["SubagentStop"]![0]!["hooks"]![0]!["headers"]!["Authorization"]!.GetValue<string>());
            Assert.IsNull(Parse(MatrixAgentHook.Apply(null, Url, Entry, null))["hooks"]!["SubagentStop"]![0]!["hooks"]![0]!["headers"]);
        }
    }
}
