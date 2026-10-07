#nullable enable
using System.Collections.Generic;
using Feeder.Logging;
using AgentConfig = Feeder.ToolHost.AgentConfig;
using TransportMethod = Feeder.ToolHost.Common.Consts.MCP.Server.TransportMethod;

namespace Feeder.MCP.Editor.UI
{
    /// <summary>
    /// DeepSeek AI agent configurator for the Matrix Bridge.
    ///
    /// The shared <see cref="AgentConfig.AiAgentConfiguratorRegistry"/> (compiled into
    /// Feeder.ToolHost.dll) has no runtime registration API, so DeepSeek is defined here, in
    /// the package's Editor assembly, and injected into the agent list right after
    /// Claude Code by <see cref="AiAgentCatalog"/>.
    ///
    /// Configuration surface:
    /// - MCP entry: none. DeepSeek Harness does not read <c>.mcp.json</c> (it takes its MCP
    ///   servers from its own plugin config), and that file's single <c>mcpServers.Feeder-MCP</c>
    ///   entry belongs to Claude Code: sharing it made the two chips switch on and off together.
    /// - Skills: generated into the project's <c>.agents/skills</c> folder — the folder
    ///   DeepSeek Harness (and Codex-class agents) read skills from. Whether DeepSeek is on is
    ///   its own auto-configure flag, never a file on disk.
    /// </summary>
    public sealed class DeepSeekAiAgentConfigurator : AgentConfig.AiAgentConfigurator
    {
        public const string Id = "deepseek";

        public override string AgentName => "DeepSeek";
        public override string AgentId => Id;
        public override string DownloadUrl => "https://www.deepseek.com/";
        public override string SkillsPath => ".agents/skills";
        public override string IconName => ""; // no bundled icon asset — the window hides the icon slot

        public override string TutorialUrl => "https://api-docs.deepseek.com/";
        public override string TutorialLinkLabel => "DeepSeek Docs";
        public override string DownloadLinkLabel => "DeepSeek";

        protected override AgentConfig.AiAgentConfig CreateStdioConfig(
            AgentConfig.AgentConfiguratorSettings settings, ILogger? logger)
            => CreateConfig(settings);

        protected override AgentConfig.AiAgentConfig CreateHttpConfig(
            AgentConfig.AgentConfiguratorSettings settings, ILogger? logger)
            => CreateConfig(settings);

        protected override IReadOnlyList<AgentConfig.ConfigurationSection> BuildSections(
            AgentConfig.AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => System.Array.Empty<AgentConfig.ConfigurationSection>();

        AgentConfig.AiAgentConfig CreateConfig(AgentConfig.AgentConfiguratorSettings settings)
            => new SkillsOnlyAgentConfig(AgentName, ResolveAbsoluteSkillsPath(settings.ProjectRootPath, SkillsPath));

        sealed class SkillsOnlyAgentConfig : AgentConfig.AiAgentConfig
        {
            public SkillsOnlyAgentConfig(string name, string skillsFolder) : base(name, skillsFolder)
            {
            }

            public override string ExpectedFileContent => string.Empty;

            public override bool Configure() => true;

            public override bool Unconfigure() => true;

            public override bool IsDetected() => IsConfigured();

            public override bool IsConfigured() => MatrixPluginEditor.IsAutoConfigureAgent(DeepSeekAiAgentConfigurator.Id);
        }
    }
}
