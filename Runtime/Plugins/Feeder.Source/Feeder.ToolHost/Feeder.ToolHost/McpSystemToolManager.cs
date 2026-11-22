using System;
using System.Collections.Generic;
using System.Linq;
using Feeder.Json;
using Feeder.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Feeder.ToolHost.Common;
using Feeder.ToolHost.Common.Hub.Client;
using Feeder.ToolHost.Common.Model;
using Feeder.Reflect;
using Feeder.Logging;

namespace Feeder.ToolHost
{
public class McpSystemToolManager : ISystemToolManager, IClientSystemToolHub
{
	private readonly ILogger _logger;

	private readonly SystemToolRunnerCollection _tools;

	public int TotalToolsCount => _tools.Count;

	public McpSystemToolManager(ILogger<McpSystemToolManager> logger, SystemToolRunnerCollection tools)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_logger.LogTrace("Ctor");
		_tools = tools ?? throw new ArgumentNullException("tools");
		if (!_logger.IsEnabled(LogLevel.Trace))
		{
			return;
		}
		_logger.LogTrace("Registered system tools [{0}]:", tools.Count);
		foreach (KeyValuePair<string, IRunTool> tool in tools)
		{
			_logger.LogTrace("System tool: {0}", tool.Key);
		}
	}

	public IEnumerable<IRunTool> GetAllTools()
	{
		return _tools.Values.ToList();
	}

	public bool HasTool(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return false;
		}
		return _tools.ContainsKey(name);
	}

	public async Task<ResponseData<ResponseCallTool>> RunSystemTool(RequestCallTool request, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (request == null)
		{
			return ResponseData<ResponseCallTool>.Error(string.Empty, "Request is null.");
		}
		string name = request.Name;
		if (string.IsNullOrWhiteSpace(name))
		{
			return ResponseData<ResponseCallTool>.Error(request.RequestID, "System tool name is empty.");
		}
		if (!_tools.TryGetValue(name, out IRunTool value))
		{
			_logger.LogWarning("System tool '{name}' not found. Available: [{available}]", name, string.Join(", ", _tools.Keys.OrderBy((string k) => k)));
			return ResponseData<ResponseCallTool>.Error(request.RequestID, "System tool '" + name + "' not found.");
		}
		try
		{
			_logger.LogDebug("Executing system tool '{name}'.", name);
			return (await value.Run(request.RequestID, request.Arguments, cancellationToken)).Pack(request.RequestID);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "System tool '{name}' failed.", name);
			return ResponseData<ResponseCallTool>.Error(request.RequestID, "System tool '" + name + "' failed: " + ex.Message);
		}
	}

	public Task<ResponseData<ResponseListTool[]>> RunListSystemTool(RequestListTool request, CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			_logger.LogDebug("Listing system tools.");
			ResponseListTool[] array = _tools.Select<KeyValuePair<string, IRunTool>, ResponseListTool>(delegate(KeyValuePair<string, IRunTool> kvp)
			{
				ResponseListTool responseListTool = new ResponseListTool
				{
					Name = kvp.Value.Name,
					Enabled = kvp.Value.Enabled,
					Title = kvp.Value.Title,
					Description = kvp.Value.Description,
					InputSchema = (kvp.Value.InputSchema.ToJsonElement() ?? Consts.MCP.EmptyInputSchema),
					ReadOnlyHint = kvp.Value.ReadOnlyHint,
					DestructiveHint = kvp.Value.DestructiveHint,
					IdempotentHint = kvp.Value.IdempotentHint,
					OpenWorldHint = kvp.Value.OpenWorldHint
				};
				if (kvp.Value.OutputSchema == null)
				{
					return responseListTool;
				}
				JsonNode outputSchema = kvp.Value.OutputSchema;
				if (outputSchema == null)
				{
					return responseListTool;
				}
				if (outputSchema.GetValueKind() != JsonValueKind.Object)
				{
					return responseListTool;
				}
				if (outputSchema["type"]?.GetValue<string>() != "object")
				{
					return responseListTool;
				}
				responseListTool.OutputSchema = outputSchema.ToJsonElement();
				return responseListTool;
			}).ToArray();
			_logger.LogDebug("{0} System tools listed.", array.Length);
			return array.Log(_logger).Pack(request.RequestID).TaskFromResult();
		}
		catch (Exception ex)
		{
			return ResponseData<ResponseListTool[]>.Error(request.RequestID, $"Failed to list system tools. Exception: {ex}").Log(_logger, "RunListSystemTool", ex).TaskFromResult();
		}
	}
}
}
