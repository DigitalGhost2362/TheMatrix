using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Feeder.McpPlugin.Common.Utils;

namespace Feeder.McpPlugin
{
public class ConnectionConfig
{
	public virtual string Host { get; set; } = "http://localhost:8080";

	public virtual int TimeoutMs { get; set; } = 60000;

	public virtual bool KeepConnected { get; set; } = true;

	public virtual int MaxConsecutiveConnectionFailures { get; set; }

	public virtual int ConnectTimeoutSeconds { get; set; }

	public virtual string? Token { get; set; }

	public virtual bool GenerateSkillFiles { get; set; } = true;

	public virtual string SkillsPath { get; set; } = "SKILLS";

	[JsonIgnore]
	public string? ProjectRootPath { get; set; }

	public static ConnectionConfig Default => new ConnectionConfig
	{
		Host = "http://localhost:8080",
		TimeoutMs = 60000
	};

	public static string GetSkillsFolderFromArgsOrEnv(string[]? args = null)
	{
		if (args == null)
		{
			args = Environment.GetCommandLineArgs();
		}
		string environmentVariable = Environment.GetEnvironmentVariable("MCP_SKILLS_FOLDER");
		if (ArgsUtils.ParseLineArguments(args).TryGetValue("mcp-skills-folder".TrimStart('-'), out string value))
		{
			return value;
		}
		return environmentVariable ?? "SKILLS";
	}

	public static ConnectionConfig BuildFromArgsOrEnv(string[]? args = null)
	{
		if (args == null)
		{
			args = Environment.GetCommandLineArgs();
		}
		ConnectionConfig connectionConfig = new ConnectionConfig();
		connectionConfig.ParseEnvironmentVariables();
		connectionConfig.ParseCommandLineArguments(args);
		return connectionConfig;
	}

	public static string GetEndpointFromArgsOrEnv(string[]? args = null)
	{
		if (args == null)
		{
			args = Environment.GetCommandLineArgs();
		}
		string environmentVariable = Environment.GetEnvironmentVariable("MCP_SERVER_ENDPOINT");
		if (ArgsUtils.ParseLineArguments(args).TryGetValue("mcp-server-endpoint".TrimStart('-'), out string value))
		{
			return value;
		}
		return environmentVariable ?? "http://localhost:8080";
	}

	public static string? GetTokenFromArgsOrEnv(string[]? args = null)
	{
		if (args == null)
		{
			args = Environment.GetCommandLineArgs();
		}
		string environmentVariable = Environment.GetEnvironmentVariable("MCP_PLUGIN_TOKEN");
		if (ArgsUtils.ParseLineArguments(args).TryGetValue("mcp-plugin-token".TrimStart('-'), out string value))
		{
			return value;
		}
		return environmentVariable;
	}

	public static int GetTimeoutFromArgsOrEnv(string[]? args = null)
	{
		if (args == null)
		{
			args = Environment.GetCommandLineArgs();
		}
		string environmentVariable = Environment.GetEnvironmentVariable("MCP_SERVER_TIMEOUT");
		if (ArgsUtils.ParseLineArguments(args).TryGetValue("mcp-server-timeout".TrimStart('-'), out string value) && int.TryParse(value, out var result))
		{
			return result;
		}
		if (environmentVariable != null && int.TryParse(environmentVariable, out var result2))
		{
			return result2;
		}
		return 10000;
	}

	private void ParseEnvironmentVariables()
	{
		string environmentVariable = Environment.GetEnvironmentVariable("MCP_SERVER_ENDPOINT");
		if (environmentVariable != null)
		{
			Host = environmentVariable;
		}
		string environmentVariable2 = Environment.GetEnvironmentVariable("MCP_SERVER_TIMEOUT");
		if (environmentVariable2 != null && int.TryParse(environmentVariable2, out var result))
		{
			TimeoutMs = result;
		}
		string environmentVariable3 = Environment.GetEnvironmentVariable("MCP_PLUGIN_TOKEN");
		if (environmentVariable3 != null)
		{
			Token = environmentVariable3;
		}
		string environmentVariable4 = Environment.GetEnvironmentVariable("MCP_SKILLS_FOLDER");
		if (environmentVariable4 != null)
		{
			SkillsPath = environmentVariable4;
		}
	}

	private void ParseCommandLineArguments(string[] args)
	{
		Dictionary<string, string> dictionary = ArgsUtils.ParseLineArguments(args);
		string valueOrDefault = dictionary.GetValueOrDefault("mcp-server-endpoint".TrimStart('-'));
		if (valueOrDefault != null)
		{
			Host = valueOrDefault;
		}
		string valueOrDefault2 = dictionary.GetValueOrDefault("mcp-server-timeout".TrimStart('-'));
		if (valueOrDefault2 != null && int.TryParse(valueOrDefault2, out var result))
		{
			TimeoutMs = result;
		}
		string valueOrDefault3 = dictionary.GetValueOrDefault("mcp-plugin-token".TrimStart('-'));
		if (valueOrDefault3 != null)
		{
			Token = valueOrDefault3;
		}
		string valueOrDefault4 = dictionary.GetValueOrDefault("mcp-skills-folder".TrimStart('-'));
		if (valueOrDefault4 != null)
		{
			SkillsPath = valueOrDefault4;
		}
	}

	public override string ToString()
	{
		return $"Endpoint: {Host}, Timeout: {TimeoutMs}ms";
	}
}
}
