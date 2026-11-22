using System;
using Feeder.ToolHost.Common;
using Feeder.Logging;

namespace Feeder.ToolHost
{
public interface IToolHost : IDisposable
{
	ILogger Logger { get; }

	IMcpManager McpManager { get; }

	Feeder.ToolHost.Common.Version Version { get; }

	ulong ToolCallsCount => 0uL;

	bool GenerateSkillFiles(string? path = null);

	bool GenerateSkillFilesIfNeeded(string? path = null);

	bool DeleteSkillFiles(string? path = null);
}
}
