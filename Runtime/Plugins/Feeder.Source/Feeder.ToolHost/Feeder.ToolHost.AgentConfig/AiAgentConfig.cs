using System;
using System.Collections.Generic;
using System.IO;
using Feeder.Logging;

namespace Feeder.ToolHost.AgentConfig
{
public abstract class AiAgentConfig
{
	public static readonly string[] DeprecatedMcpServerNames = new string[0];

	public const string DefaultMcpServerName = "Feeder-MCP";

	// empty on purpose: an entry under another name that shares our command or url belongs to someone else
	// (another tool, the user) and must never be removed; only the Feeder-MCP entry is ours
	public static readonly string[] DefaultIdentityKeys = new string[0];

	protected readonly List<string> _identityKeys = new List<string>(DefaultIdentityKeys);

	protected readonly ILogger? _logger;

	public string Name { get; set; }

	public string ConfigPath { get; set; }

	public string BodyPath { get; set; }

	public abstract string ExpectedFileContent { get; }

	public IReadOnlyList<string> IdentityKeys => _identityKeys;

	protected AiAgentConfig(string name, string configPath, string bodyPath = "mcpServers", ILogger? logger = null)
	{
		Name = name;
		ConfigPath = configPath;
		BodyPath = bodyPath;
		_logger = logger;
	}

	public AiAgentConfig AddIdentityKey(string key)
	{
		if (!_identityKeys.Contains(key))
		{
			_identityKeys.Add(key);
		}
		return this;
	}

	public abstract bool Configure();

	public abstract bool Unconfigure();

	public abstract bool IsDetected();

	public abstract bool IsConfigured();

	public virtual void ApplyHttpAuthorization(bool isRequired, string? token)
	{
	}

	public virtual void ApplyStdioAuthorization(bool isRequired, string? token)
	{
	}

	// the config files are shared with other tools: write a sibling temp file, then swap it in, so no reader
	// ever sees a half-written file
	protected static void WriteAllTextAtomic(string path, string contents)
	{
		string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
		try
		{
			File.WriteAllText(temp, contents);
			if (File.Exists(path))
			{
				File.Replace(temp, path, null);
			}
			else
			{
				File.Move(temp, path);
			}
		}
		finally
		{
			// a failed swap can leave the temp as the only copy: keep it then
			if (File.Exists(temp) && File.Exists(path))
			{
				File.Delete(temp);
			}
		}
	}
}
}
