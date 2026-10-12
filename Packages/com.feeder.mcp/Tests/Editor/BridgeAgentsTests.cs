#nullable enable

using System;
using System.IO;
using Feeder.MCP.Editor.UI;
using NUnit.Framework;
using AgentConfig = Feeder.ToolHost.AgentConfig;
using TransportMethod = Feeder.ToolHost.Common.Consts.MCP.Server.TransportMethod;

namespace Feeder.MCP.Editor.Tests
{
    public sealed class BridgeAgentsTests
    {
        const string ClaudeId = "claude-code";

        string _root = string.Empty;
        bool _deepSeekWasOn;
        AgentConfig.AgentConfiguratorSettings _settings = null!;
        AgentConfig.AiAgentConfigurator _claude = null!;
        AgentConfig.AiAgentConfigurator _deepSeek = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "matrix-bridge-agents-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _settings = AgentConfig.AgentConfiguratorSettings.CreateForHost(_root, "C:/matrix/server.exe", 25999, 10000, "http://localhost:25999/mcp");
            _claude = Agent(ClaudeId);
            _deepSeek = Agent(DeepSeekAiAgentConfigurator.Id);
            _deepSeekWasOn = MatrixPluginEditor.IsAutoConfigureAgent(DeepSeekAiAgentConfigurator.Id);
            MatrixPluginEditor.SetAutoConfigureAgent(DeepSeekAiAgentConfigurator.Id, false);
        }

        [TearDown]
        public void TearDown()
        {
            MatrixPluginEditor.SetAutoConfigureAgent(DeepSeekAiAgentConfigurator.Id, _deepSeekWasOn);
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [TestCase(TransportMethod.stdio)]
        [TestCase(TransportMethod.streamableHttp)]
        public void WiringClaude_LeavesDeepSeekOff(TransportMethod transport)
        {
            Config(_claude, transport).Configure();

            Assert.AreEqual(AgentConfig.ConfiguratorStatus.Configured, Status(_claude, transport));
            Assert.AreEqual(AgentConfig.ConfiguratorStatus.NotConfigured, Status(_deepSeek, transport));
        }

        [TestCase(TransportMethod.stdio)]
        [TestCase(TransportMethod.streamableHttp)]
        public void WiringDeepSeek_LeavesClaudeOff_AndWritesNoMcpJson(TransportMethod transport)
        {
            MatrixPluginEditor.SetAutoConfigureAgent(DeepSeekAiAgentConfigurator.Id, true);

            Config(_deepSeek, transport).Configure();

            Assert.AreEqual(AgentConfig.ConfiguratorStatus.Configured, Status(_deepSeek, transport));
            Assert.AreEqual(AgentConfig.ConfiguratorStatus.NotConfigured, Status(_claude, transport));
            Assert.IsFalse(File.Exists(Path.Combine(_root, ".mcp.json")));
        }

        [TestCase(TransportMethod.stdio)]
        [TestCase(TransportMethod.streamableHttp)]
        public void UnwiringDeepSeek_KeepsClaudesEntry(TransportMethod transport)
        {
            Config(_claude, transport).Configure();
            MatrixPluginEditor.SetAutoConfigureAgent(DeepSeekAiAgentConfigurator.Id, true);

            Config(_deepSeek, transport).Unconfigure();
            MatrixPluginEditor.SetAutoConfigureAgent(DeepSeekAiAgentConfigurator.Id, false);

            Assert.AreEqual(AgentConfig.ConfiguratorStatus.Configured, Status(_claude, transport));
            Assert.AreEqual(AgentConfig.ConfiguratorStatus.NotConfigured, Status(_deepSeek, transport));
        }

        [TestCase(TransportMethod.stdio)]
        [TestCase(TransportMethod.streamableHttp)]
        public void UnwiringClaude_KeepsDeepSeekOn(TransportMethod transport)
        {
            Config(_claude, transport).Configure();
            MatrixPluginEditor.SetAutoConfigureAgent(DeepSeekAiAgentConfigurator.Id, true);

            Config(_claude, transport).Unconfigure();

            Assert.AreEqual(AgentConfig.ConfiguratorStatus.NotConfigured, Status(_claude, transport));
            Assert.AreEqual(AgentConfig.ConfiguratorStatus.Configured, Status(_deepSeek, transport));
        }

        // an entry of someone else that shares our command and url must survive wiring and unwiring
        [TestCase(TransportMethod.stdio)]
        [TestCase(TransportMethod.streamableHttp)]
        public void WiringAndUnwiringClaude_KeepsAnotherServerWithTheSameCommandAndUrl(TransportMethod transport)
        {
            var path = Path.Combine(_root, ".mcp.json");
            File.WriteAllText(path, "{ \"mcpServers\": { \"other-tool\": { \"command\": \"C:/matrix/server.exe\", \"url\": \"http://localhost:25999/mcp\" } } }");

            Config(_claude, transport).Configure();
            StringAssert.Contains("other-tool", File.ReadAllText(path));
            Assert.AreEqual(AgentConfig.ConfiguratorStatus.Configured, Status(_claude, transport));

            Config(_claude, transport).Unconfigure();
            StringAssert.Contains("other-tool", File.ReadAllText(path));
        }

        [Test]
        public void WiringAndUnwiringCodex_KeepsTheNextSectionItsCommentAndLineEndings()
        {
            var codex = Agent("codex");
            var path = Path.Combine(_root, ".codex", "config.toml");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            const string other = "\n# another tool\n[mcp_servers.other]\nurl = \"http://localhost:25999/mcp\"\n";
            File.WriteAllText(path, "[mcp_servers.Feeder-MCP]\nurl = \"http://localhost:1/mcp\"\n" + other);

            Config(codex, TransportMethod.streamableHttp).Configure();
            var written = File.ReadAllText(path);
            StringAssert.EndsWith(other, written);
            StringAssert.DoesNotContain("\r", written);
            Assert.AreEqual(AgentConfig.ConfiguratorStatus.Configured, Status(codex, TransportMethod.streamableHttp));

            Config(codex, TransportMethod.streamableHttp).Unconfigure();
            Assert.AreEqual(other, File.ReadAllText(path));
        }

        [Test]
        public void CanRewrite_OnlyPlainJsonOrNoFile()
        {
            var config = Config(_claude, TransportMethod.streamableHttp);
            Assert.IsTrue(BridgeAgents.CanRewrite(config));

            File.WriteAllText(config.ConfigPath, "// shared file\n{ \"mcpServers\": {} }");
            Assert.IsFalse(BridgeAgents.CanRewrite(config));

            File.WriteAllText(config.ConfigPath, "{ \"mcpServers\": {} }");
            Assert.IsTrue(BridgeAgents.CanRewrite(config));
        }

        static AgentConfig.AiAgentConfigurator Agent(string agentId)
        {
            var agent = AiAgentCatalog.GetByAgentId(agentId);
            Assert.IsNotNull(agent, agentId);
            return agent!;
        }

        AgentConfig.AiAgentConfig Config(AgentConfig.AiAgentConfigurator agent, TransportMethod transport)
            => transport == TransportMethod.stdio ? agent.GetStdioConfig(_settings) : agent.GetHttpConfig(_settings);

        AgentConfig.ConfiguratorStatus Status(AgentConfig.AiAgentConfigurator agent, TransportMethod transport)
            => agent.GetStatus(_settings, transport);
    }
}
