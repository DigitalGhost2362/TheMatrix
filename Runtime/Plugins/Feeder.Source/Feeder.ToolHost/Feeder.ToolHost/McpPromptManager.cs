using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Feeder.ToolHost.Common.Hub.Client;
using Feeder.ToolHost.Common.Model;
using Feeder.Reflect;
using Feeder.Logging;
using Feeder.Rx;

namespace Feeder.ToolHost
{
public class McpPromptManager : IPromptManager, IClientPromptHub, IDisposable
{
	protected readonly ILogger _logger;

	protected readonly Reflector _reflector;

	protected readonly CompositeDisposable _disposables;

	private readonly PromptRunnerCollection _prompts;

	private readonly Subject<Unit> _onPromptsUpdated;

	public Reflector Reflector => _reflector;

	public Observable<Unit> OnPromptsUpdated => (Observable<Unit>)(object)_onPromptsUpdated;

	public int EnabledPromptsCount => _prompts.Count<KeyValuePair<string, IRunPrompt>>((KeyValuePair<string, IRunPrompt> kvp) => kvp.Value.Enabled);

	public int TotalPromptsCount => _prompts.Count;

	public IEnumerable<IRunPrompt> GetAllPrompts()
	{
		return _prompts.Values.ToList();
	}

	public McpPromptManager(ILogger<McpPromptManager> logger, Reflector reflector, PromptRunnerCollection prompts)
	{
		_disposables = new CompositeDisposable();
		_onPromptsUpdated = new Subject<Unit>();
		_logger = logger ?? throw new ArgumentNullException("logger");
		_logger.LogTrace("Ctor");
		_reflector = reflector ?? throw new ArgumentNullException("reflector");
		_prompts = prompts ?? throw new ArgumentNullException("prompts");
		if (!_logger.IsEnabled(LogLevel.Trace))
		{
			return;
		}
		_logger.LogTrace("Registered prompts [{0}]:", prompts.Count);
		foreach (KeyValuePair<string, IRunPrompt> prompt in prompts)
		{
			_logger.LogTrace("Prompt: {0}", prompt.Key);
		}
	}

	public bool HasPrompt(string name)
	{
		return _prompts.ContainsKey(name);
	}

	public bool AddPrompt(IRunPrompt runner)
	{
		if (runner == null)
		{
			throw new ArgumentNullException("runner");
		}
		if (HasPrompt(runner.Name))
		{
			_logger.LogWarning("Prompt with Name '{0}' already exists. Skipping addition.", runner.Name);
			return false;
		}
		_prompts[runner.Name] = runner;
		_onPromptsUpdated.OnNext(Unit.Default);
		return true;
	}

	public bool RemovePrompt(string name)
	{
		if (!HasPrompt(name))
		{
			_logger.LogWarning("Prompt with Name '{0}' not found. Cannot remove.", name);
			return false;
		}
		bool num = _prompts.Remove(name);
		if (num)
		{
			_onPromptsUpdated.OnNext(Unit.Default);
		}
		return num;
	}

	public bool IsPromptEnabled(string name)
	{
		if (!_prompts.TryGetValue(name, out IRunPrompt value))
		{
			_logger.LogWarning("Prompt with Name '{0}' not found.", name);
			return false;
		}
		return value.Enabled;
	}

	public bool SetPromptEnabled(string name, bool enabled)
	{
		if (!_prompts.TryGetValue(name, out IRunPrompt value))
		{
			_logger.LogWarning("Prompt with Name '{0}' not found.", name);
			return false;
		}
		_logger.LogInformation("Setting Prompt '{0}' enabled state to {1}.", name, enabled);
		value.Enabled = enabled;
		_onPromptsUpdated.OnNext(Unit.Default);
		return true;
	}

	public Task<ResponseData<ResponseGetPrompt>> RunGetPrompt(RequestGetPrompt request)
	{
		return RunGetPrompt(request, default(CancellationToken));
	}

	public async Task<ResponseData<ResponseGetPrompt>> RunGetPrompt(RequestGetPrompt request, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!_prompts.TryGetValue(request.Name, out IRunPrompt value))
		{
			return ResponseData<ResponseGetPrompt>.Error(request.RequestID, "Prompt with Name '" + request.Name + "' not found.").Log(_logger);
		}
		ResponseGetPrompt target = await value.Run(request.RequestID, request.Arguments, cancellationToken);
		target.Log(_logger);
		return target.Pack(request.RequestID);
	}

	public Task<ResponseData<ResponseListPrompts>> RunListPrompts(RequestListPrompts request)
	{
		return RunListPrompts(request, default(CancellationToken));
	}

	public Task<ResponseData<ResponseListPrompts>> RunListPrompts(RequestListPrompts request, CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			_logger.LogDebug("Listing prompts. [{Count}]", _prompts.Count);
			ResponseListPrompts responseListPrompts = new ResponseListPrompts
			{
				Prompts = _prompts.Values.Select((IRunPrompt p) => new ResponsePrompt(p.Name, p.Enabled, p.Title, p.Description, p.InputSchema.ToResponsePromptArguments())).ToList()
			};
			_logger.LogDebug("{0} Prompts listed.", responseListPrompts.Prompts.Count);
			return responseListPrompts.Log(_logger).Pack(request.RequestID).TaskFromResult();
		}
		catch (Exception ex)
		{
			return ResponseData<ResponseListPrompts>.Error(request.RequestID, $"Failed to list tools. Exception: {ex}").Log(_logger, "RunListTool", ex).TaskFromResult();
		}
	}

	public void Dispose()
	{
		_disposables.Dispose();
		_prompts.Clear();
	}
}
}
