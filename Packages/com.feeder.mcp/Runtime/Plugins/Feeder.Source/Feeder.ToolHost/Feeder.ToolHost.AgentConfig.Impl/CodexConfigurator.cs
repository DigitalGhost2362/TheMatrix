using System;
using System.Collections.Generic;
using System.IO;
using Feeder.ToolHost.Common;
using Feeder.Logging;

namespace Feeder.ToolHost.AgentConfig.Impl
{
public sealed class CodexConfigurator : AiAgentConfigurator
{
	private const string EnvVarNameAuthToken = "FEEDER_MCP_AUTH_TOKEN";

	private const string EnvVarNameBearerToken = "bearer_token_env_var";

	public override string AgentName => "Codex";

	public override string AgentId => "codex";

	public override string DownloadUrl => "https://openai.com/codex/";

	public override string? SkillsPath => ".agents/skills";

	public override string? IconName => "codex-64.png";

	private static string LocalConfigPath(AgentConfiguratorSettings s)
	{
		return Path.Combine(s.ProjectRootPath, ".codex", "config.toml");
	}

	protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		TomlAiAgentConfig tomlAiAgentConfig = new TomlAiAgentConfig(AgentName, LocalConfigPath(settings), "mcp_servers", logger).SetProperty("enabled", true, requiredForConfiguration: true).SetProperty("command", settings.ExecutableFullPath.Replace('\\', '/'), requiredForConfiguration: true, ValueComparisonMode.Path);
		object[] values = new string[4]
		{
			string.Format("{0}={1}", "port", settings.Port),
			string.Format("{0}={1}", "plugin-timeout", settings.TimeoutMs),
			string.Format("{0}={1}", "client-transport", Consts.MCP.Server.TransportMethod.stdio),
			string.Format("{0}={1}", "authorization", settings.AuthOption)
		};
		return tomlAiAgentConfig.SetProperty("args", values, requiredForConfiguration: true).SetProperty("tool_timeout_sec", 300).SetPropertyToRemove("url")
			.SetPropertyToRemove("type")
			.SetPropertyToRemove("startup_timeout_sec");
	}

	protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
	{
		return new TomlAiAgentConfig(AgentName, LocalConfigPath(settings), "mcp_servers", logger).SetProperty("enabled", true, requiredForConfiguration: true).SetProperty("url", settings.Host, requiredForConfiguration: true, ValueComparisonMode.Url).SetProperty("tool_timeout_sec", 300)
			.SetProperty("startup_timeout_sec", 30)
			.SetPropertyToRemove("command")
			.SetPropertyToRemove("args")
			.SetPropertyToRemove("type");
	}

	protected override void ApplyHttpAuthorization(AiAgentConfig config, AgentConfiguratorSettings settings)
	{
		base.ApplyHttpAuthorization(config, settings);
		TomlAiAgentConfig tomlAiAgentConfig = (config as TomlAiAgentConfig) ?? throw new InvalidCastException("Expected TomlAiAgentConfig for Codex HTTP configuration but got " + config.GetType().Name);
		if (settings.IsHttpAuthRequired && !string.IsNullOrEmpty(settings.Token))
		{
			tomlAiAgentConfig.SetProperty("bearer_token_env_var", "FEEDER_MCP_AUTH_TOKEN", requiredForConfiguration: true);
		}
		else
		{
			tomlAiAgentConfig.SetPropertyToRemove("bearer_token_env_var");
		}
	}

	protected override IReadOnlyList<ConfigurationSection> BuildSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		string text = string.Format("codex mcp add {0} \"{1}\" port={2} plugin-timeout={3} client-transport=stdio", "Feeder-MCP", settings.ExecutableFullPath, settings.Port, settings.TimeoutMs);
		string text2 = "codex mcp add Feeder-MCP --url " + settings.Host;
		if (settings.IsHttpAuthRequired)
		{
			text += " --bearer-token-env-var=FEEDER_MCP_AUTH_TOKEN";
			text2 += " --bearer-token-env-var=FEEDER_MCP_AUTH_TOKEN";
		}
		if (transport != Consts.MCP.Server.TransportMethod.stdio)
		{
			List<ConfigurationItem> list = new List<ConfigurationItem>();
			if (settings.IsHttpAuthRequired)
			{
				list.Add(ConfigurationItem.Warning("Authorization is enabled. Set the 'FEEDER_MCP_AUTH_TOKEN' environment variable before starting Codex in terminal."));
				list.Add(settings.IsWindows ? ConfigurationItem.ReadOnlyField("setx FEEDER_MCP_AUTH_TOKEN \"" + settings.Token + "\"") : ConfigurationItem.ReadOnlyField("export FEEDER_MCP_AUTH_TOKEN=\"" + settings.Token + "\""));
			}
			list.Add(ConfigurationItem.Description("1. Open a terminal and run the following command to be in the project folder"));
			list.Add(ConfigurationItem.ReadOnlyField("cd \"" + settings.ProjectRootPath + "\""));
			list.Add(ConfigurationItem.Description("2. Run the following command in the project folder to configure Codex"));
			list.Add(ConfigurationItem.ReadOnlyField(text2));
			list.Add(ConfigurationItem.Description("3. Start Codex"));
			list.Add(ConfigurationItem.ReadOnlyField("codex"));
			return new ConfigurationSection[2]
			{
				new ConfigurationSection("Manual Configuration Steps - Option 1", expandedFirst: true, list),
				new ConfigurationSection("Manual Configuration Steps - Option 2", expandedFirst: false, new ConfigurationItem[3]
				{
					ConfigurationItem.Description("1. Open or create file '.codex/config.toml'"),
					ConfigurationItem.Description("2. Copy and paste the configuration TOML into the file."),
					ConfigurationItem.ReadOnlyField(GetHttpConfig(settings, logger).ExpectedFileContent)
				})
			};
		}
		return new ConfigurationSection[2]
		{
			new ConfigurationSection("Manual Configuration Steps - Option 1", expandedFirst: true, new ConfigurationItem[6]
			{
				ConfigurationItem.Description("1. Open a terminal and run the following command to be in the project folder"),
				ConfigurationItem.ReadOnlyField("cd \"" + settings.ProjectRootPath + "\""),
				ConfigurationItem.Description("2. Run the following command in the project folder to configure Codex"),
				ConfigurationItem.ReadOnlyField(text),
				ConfigurationItem.Description("3. Start Codex"),
				ConfigurationItem.ReadOnlyField("codex")
			}),
			new ConfigurationSection("Manual Configuration Steps - Option 2", expandedFirst: false, new ConfigurationItem[3]
			{
				ConfigurationItem.Description("1. Open or create file '.codex/config.toml'"),
				ConfigurationItem.Description("2. Copy and paste the configuration TOML into the file."),
				ConfigurationItem.ReadOnlyField(GetStdioConfig(settings, logger).ExpectedFileContent)
			})
		};
	}

	protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(AgentConfiguratorSettings settings, Consts.MCP.Server.TransportMethod transport, ILogger? logger)
	{
		return AiAgentConfigurator.TroubleshootingSection("- Ensure Codex CLI is installed and accessible from terminal", "- Restart Codex after configuration changes");
	}
}
}
