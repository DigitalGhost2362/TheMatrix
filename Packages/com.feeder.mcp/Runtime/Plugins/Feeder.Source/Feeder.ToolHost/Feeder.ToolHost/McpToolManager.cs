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
using Feeder.Rx;

namespace Feeder.ToolHost
{
public class McpToolManager : IToolManager, IClientToolHub, IDisposable
{
	protected readonly ILogger _logger;

	protected readonly Reflector _reflector;

	protected readonly CompositeDisposable _disposables;

	private long toolCallsCount;

	private readonly ToolRunnerCollection _tools;

	private readonly Subject<Unit> _onToolsUpdated;

	private readonly Subject<ToolCallActivity> _onToolCall;

	public Reflector Reflector => _reflector;

	public Observable<Unit> OnToolsUpdated => (Observable<Unit>)(object)_onToolsUpdated;

	public Observable<ToolCallActivity> OnToolCall => _onToolCall;

	public ulong ToolCallsCount => (ulong)Interlocked.Read(ref toolCallsCount);

	public int EnabledToolsCount => _tools.Count<KeyValuePair<string, IRunTool>>((KeyValuePair<string, IRunTool> kvp) => kvp.Value.Enabled);

	public int TotalToolsCount => _tools.Count;

	public int EnabledToolsTokenCount => _tools.Where<KeyValuePair<string, IRunTool>>((KeyValuePair<string, IRunTool> kvp) => kvp.Value.Enabled).Sum((KeyValuePair<string, IRunTool> kvp) => kvp.Value.TokenCount);

	public IEnumerable<IRunTool> GetAllTools()
	{
		return _tools.Values.ToList();
	}

	public McpToolManager(ILogger<McpToolManager> logger, Reflector reflector, ToolRunnerCollection tools)
	{
		_disposables = new CompositeDisposable();
		_onToolsUpdated = new Subject<Unit>();
		_onToolCall = new Subject<ToolCallActivity>();
		_logger = logger ?? throw new ArgumentNullException("logger");
		_logger.LogTrace("Ctor");
		_reflector = reflector ?? throw new ArgumentNullException("reflector");
		_tools = tools ?? throw new ArgumentNullException("tools");
		if (!_logger.IsEnabled(LogLevel.Trace))
		{
			return;
		}
		_logger.LogTrace("Registered tools [{0}]:", tools.Count);
		foreach (KeyValuePair<string, IRunTool> tool in tools)
		{
			_logger.LogTrace("Tool: {0}", tool.Key);
		}
	}

	public bool HasTool(string name)
	{
		return _tools.ContainsKey(name);
	}

	public bool AddTool(string name, IRunTool runner)
	{
		if (HasTool(name))
		{
			_logger.LogWarning("Tool with Name '{0}' already exists. Skipping addition.", name);
			return false;
		}
		_tools[name] = runner;
		_onToolsUpdated.OnNext(Unit.Default);
		return true;
	}

	public bool RemoveTool(string name)
	{
		if (!HasTool(name))
		{
			_logger.LogWarning("Tool with Name '{0}' not found. Cannot remove.", name);
			return false;
		}
		bool num = _tools.Remove(name);
		if (num)
		{
			_onToolsUpdated.OnNext(Unit.Default);
		}
		return num;
	}

	public bool IsToolEnabled(string name)
	{
		if (!_tools.TryGetValue(name, out IRunTool value))
		{
			_logger.LogWarning("Tool with Name '{0}' not found.", name);
			return false;
		}
		return value.Enabled;
	}

	public bool SetToolEnabled(string name, bool enabled)
	{
		if (!_tools.TryGetValue(name, out IRunTool value))
		{
			_logger.LogWarning("Tool with Name '{0}' not found.", name);
			return false;
		}
		value.Enabled = enabled;
		_onToolsUpdated.OnNext(Unit.Default);
		return true;
	}

	public Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool data)
	{
		return RunCallTool(data, default(CancellationToken));
	}

	public async Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool data, CancellationToken cancellationToken = default(CancellationToken))
	{
		Interlocked.Increment(ref toolCallsCount);
		if (data == null)
		{
			return ResponseData<ResponseCallTool>.Error("00000000-0000-0000-0000-000000000000", "Tool data is null.").Log(_logger);
		}
		if (string.IsNullOrEmpty(data.Name))
		{
			return ResponseData<ResponseCallTool>.Error(data.RequestID, "Tool.Name is null.").Log(_logger);
		}
		if (!_tools.TryGetValue(data.Name, out IRunTool value))
		{
			return ResponseData<ResponseCallTool>.Error(data.RequestID, "Tool with Name '" + data.Name + "' not found.").Log(_logger);
		}
		try
		{
			if (_logger.IsEnabled(LogLevel.Information))
			{
				string message = ((data.Arguments == null) ? ("Run tool '" + data.Name + "' with no parameters.") : string.Format("Run tool '{0}' with parameters[{1}]:\n{2}\n", data.Name, data.Arguments.Count, string.Join(",\n", data.Arguments)));
				_logger.LogInformation(message);
			}
			ResponseCallTool responseCallTool = await RunTool(value, data.RequestID, data.Arguments, cancellationToken);
			if (responseCallTool == null)
			{
				return ResponseData<ResponseCallTool>.Error(data.RequestID, "Tool '" + data.Name + "' returned null result.").Log(_logger);
			}
			responseCallTool.Log(_logger);
			return responseCallTool.Pack(data.RequestID);
		}
		catch (Exception ex)
		{
			return ResponseData<ResponseCallTool>.Error(data.RequestID, $"Failed to run tool '{data.Name}'. Exception: {ex}").Log(_logger, "RunCallTool[" + data.Name + "]", ex);
		}
	}

	public async Task<ResponseCallTool> RunTool(IRunTool tool, string requestId, IReadOnlyDictionary<string, JsonElement>? namedParameters, CancellationToken cancellationToken = default(CancellationToken))
	{
		_onToolCall.OnNext(new ToolCallActivity(tool.Name, tool.ReadOnlyHint, tool.DestructiveHint, finished: false));
		try
		{
			return await tool.Run(requestId, namedParameters, cancellationToken);
		}
		finally
		{
			_onToolCall.OnNext(new ToolCallActivity(tool.Name, tool.ReadOnlyHint, tool.DestructiveHint, finished: true));
		}
	}

	public Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool data)
	{
		return RunListTool(data, default(CancellationToken));
	}

	public Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool data, CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			_logger.LogDebug("Listing tools.");
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
			_logger.LogDebug("{0} Tools listed.", array.Length);
			return array.Log(_logger).Pack(data.RequestID).TaskFromResult();
		}
		catch (Exception ex)
		{
			return ResponseData<ResponseListTool[]>.Error(data.RequestID, $"Failed to list tools. Exception: {ex}").Log(_logger, "RunListTool", ex).TaskFromResult();
		}
	}

	public void Dispose()
	{
		_disposables.Dispose();
		_onToolCall.Dispose();
		_tools.Clear();
	}
}
}
