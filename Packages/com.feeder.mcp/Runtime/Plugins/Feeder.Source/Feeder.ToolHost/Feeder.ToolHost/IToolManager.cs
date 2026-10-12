using System;
using System.Collections.Generic;
using Feeder.Json;
using System.Threading;
using System.Threading.Tasks;
using Feeder.ToolHost.Common.Model;
using Feeder.ToolHost.Common.Hub.Client;
using Feeder.Rx;

namespace Feeder.ToolHost
{
public interface IToolManager : IClientToolHub, IDisposable
{
	Observable<Unit> OnToolsUpdated { get; }

	Observable<ToolCallActivity> OnToolCall { get; }

	int EnabledToolsCount { get; }

	int TotalToolsCount { get; }

	ulong ToolCallsCount => 0uL;

	int EnabledToolsTokenCount { get; }

	IEnumerable<IRunTool> GetAllTools();

	bool HasTool(string name);

	bool AddTool(string name, IRunTool runner);

	bool RemoveTool(string name);

	bool IsToolEnabled(string name);

	bool SetToolEnabled(string name, bool enabled);

	Task<ResponseCallTool> RunTool(IRunTool tool, string requestId, IReadOnlyDictionary<string, JsonElement>? namedParameters, CancellationToken cancellationToken = default(CancellationToken));
}
}
