using Feeder.ToolHost.Common;

namespace Feeder.ToolHost.AgentConfig
{
public sealed class AgentConfiguratorSettings
{
	public OperatingSystemKind OperatingSystem { get; }

	public string ProjectRootPath { get; }

	public string ExecutableFullPath { get; }

	public int Port { get; }

	public int TimeoutMs { get; }

	public string Host { get; }

	public string? Token { get; }

	public Consts.MCP.Server.AuthOption AuthOption { get; }

	public string ServerExecutableName { get; }

	public string ServerVersion { get; }

	public bool IsHttpAuthRequired => AuthOption == Consts.MCP.Server.AuthOption.required;

	public bool IsStdioAuthRequired => AuthOption == Consts.MCP.Server.AuthOption.required;

	public bool IsWindows => OperatingSystem == OperatingSystemKind.Windows;

	public AgentConfiguratorSettings(OperatingSystemKind operatingSystem, string projectRootPath, string executableFullPath, int port, int timeoutMs, string host, string? token = null, Consts.MCP.Server.AuthOption authOption = Consts.MCP.Server.AuthOption.none, string serverExecutableName = "matrix-server", string serverVersion = "")
	{
		OperatingSystem = operatingSystem;
		ProjectRootPath = projectRootPath;
		ExecutableFullPath = executableFullPath;
		Port = port;
		TimeoutMs = timeoutMs;
		Host = host;
		Token = token;
		AuthOption = authOption;
		ServerExecutableName = serverExecutableName;
		ServerVersion = serverVersion;
	}

	public static AgentConfiguratorSettings CreateForHost(string projectRootPath, string executableFullPath, int port, int timeoutMs, string host, string? token = null, Consts.MCP.Server.AuthOption authOption = Consts.MCP.Server.AuthOption.none, string serverExecutableName = "matrix-server", string serverVersion = "")
	{
		return new AgentConfiguratorSettings(HostOperatingSystem.Detect(), projectRootPath, executableFullPath, port, timeoutMs, host, token, authOption, serverExecutableName, serverVersion);
	}
}
}
