using System;
using System.Collections.Generic;
using Feeder.Json.Serialization;
using Feeder.ToolHost.Common.Utils;

namespace Feeder.ToolHost
{
public class ConnectionConfig
{
	public virtual string Host { get; set; } = "http://localhost:20000";

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
		Host = "http://localhost:20000",
		TimeoutMs = 60000
	};

	public override string ToString()
	{
		return $"Endpoint: {Host}, Timeout: {TimeoutMs}ms";
	}
}
}
