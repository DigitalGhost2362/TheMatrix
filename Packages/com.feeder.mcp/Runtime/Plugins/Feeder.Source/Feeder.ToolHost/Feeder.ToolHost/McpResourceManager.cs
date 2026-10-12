using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Feeder.ToolHost.Common.Hub.Client;
using Feeder.ToolHost.Common.Model;
using Feeder.Reflect;
using Feeder.Logging;
using Feeder.Rx;

namespace Feeder.ToolHost
{
public class McpResourceManager : IResourceManager, IClientResourceHub, IDisposable
{
	protected readonly ILogger _logger;

	protected readonly Reflector _reflector;

	protected readonly CompositeDisposable _disposables;

	private readonly ResourceRunnerCollection _resources;

	private readonly Subject<Unit> _onResourcesUpdated;

	public Reflector Reflector => _reflector;

	public Observable<Unit> OnResourcesUpdated => (Observable<Unit>)(object)_onResourcesUpdated;

	public int EnabledResourcesCount => _resources.Count<KeyValuePair<string, IRunResource>>((KeyValuePair<string, IRunResource> kvp) => kvp.Value.Enabled);

	public int TotalResourcesCount => _resources.Count;

	public IEnumerable<IRunResource> GetAllResources()
	{
		return _resources.Values.ToList();
	}

	public McpResourceManager(ILogger<McpResourceManager> logger, Reflector reflector, ResourceRunnerCollection resources)
	{
		_disposables = new CompositeDisposable();
		_onResourcesUpdated = new Subject<Unit>();
		_logger = logger ?? throw new ArgumentNullException("logger");
		_logger.LogTrace("Ctor");
		_reflector = reflector ?? throw new ArgumentNullException("reflector");
		_resources = resources ?? throw new ArgumentNullException("resources");
		if (!_logger.IsEnabled(LogLevel.Trace))
		{
			return;
		}
		_logger.LogTrace("Registered resources [{0}]:", resources.Count);
		foreach (KeyValuePair<string, IRunResource> resource in resources)
		{
			_logger.LogTrace("Resource: {Name}. Route: {Route}", resource.Key, resource.Value.Route);
		}
	}

	public bool HasResource(string name)
	{
		return _resources.ContainsKey(name);
	}

	public bool AddResource(IRunResource resourceParams)
	{
		if (resourceParams == null)
		{
			throw new ArgumentNullException("resourceParams");
		}
		if (HasResource(resourceParams.Name))
		{
			_logger.LogWarning("Resource with Name '{0}' already exists. Skipping addition.", resourceParams.Name);
			return false;
		}
		_resources[resourceParams.Name] = resourceParams;
		_onResourcesUpdated.OnNext(Unit.Default);
		return true;
	}

	public bool RemoveResource(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			throw new ArgumentException("Resource name is null or empty.", "name");
		}
		if (!HasResource(name))
		{
			_logger.LogWarning("Resource with Name '{0}' does not exist. Skipping removal.", name);
			return false;
		}
		_resources.Remove(name);
		_onResourcesUpdated.OnNext(Unit.Default);
		return true;
	}

	public bool IsResourceEnabled(string name)
	{
		if (!_resources.TryGetValue(name, out IRunResource value))
		{
			_logger.LogWarning("Resource with Name '{0}' not found.", name);
			return false;
		}
		return value.Enabled;
	}

	public bool SetResourceEnabled(string name, bool enabled)
	{
		if (!_resources.TryGetValue(name, out IRunResource value))
		{
			_logger.LogWarning("Resource with Name '{0}' not found.", name);
			return false;
		}
		value.Enabled = enabled;
		_onResourcesUpdated.OnNext(Unit.Default);
		return true;
	}

	public Task<ResponseData<ResponseResourceContent[]>> RunResourceContent(RequestResourceContent data)
	{
		return RunResourceContent(data, default(CancellationToken));
	}

	public async Task<ResponseData<ResponseResourceContent[]>> RunResourceContent(RequestResourceContent data, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (data == null)
		{
			throw new ArgumentException("Resource data is null.");
		}
		if (data.Uri == null)
		{
			throw new ArgumentException("Resource.Uri is null.");
		}
		IRunResourceContent runResourceContent = FindResourceContentRunner(data.Uri, _resources, out string uriTemplate)?.RunGetContent;
		if (runResourceContent == null || uriTemplate == null)
		{
			throw new ArgumentException("No route matches the URI: " + data.Uri + "\nAvailable routes:\n" + string.Join("\n", _resources.Values.Select((IRunResource r) => r.Route)));
		}
		_logger.LogInformation("Executing resource '{0}'.", data.Uri);
		IDictionary<string, object> dictionary = ParseUriParameters(uriTemplate, data.Uri);
		PrintParameters(dictionary);
		return (await runResourceContent.Run(dictionary)).Pack(data.RequestID);
	}

	public Task<ResponseData<ResponseListResource[]>> RunListResources(RequestListResources data)
	{
		return RunListResources(data, default(CancellationToken));
	}

	public async Task<ResponseData<ResponseListResource[]>> RunListResources(RequestListResources data, CancellationToken cancellationToken = default(CancellationToken))
	{
		_logger.LogDebug("Listing resources. [{Count}]", _resources.Count);
		IEnumerable<Task<ResponseListResource[]>> tasks = from resource in _resources.Values
			where resource.Enabled
			select resource.RunListContext.Run();
		await Task.WhenAll(tasks);
		return tasks.SelectMany((Task<ResponseListResource[]> x) => x.Result).ToArray().Pack(data.RequestID);
	}

	public Task<ResponseData<ResponseResourceTemplate[]>> RunResourceTemplates(RequestListResourceTemplates data)
	{
		return RunResourceTemplates(data, default(CancellationToken));
	}

	public Task<ResponseData<ResponseResourceTemplate[]>> RunResourceTemplates(RequestListResourceTemplates data, CancellationToken cancellationToken = default(CancellationToken))
	{
		_logger.LogDebug("Listing resource templates. [{Count}]", _resources.Count);
		return _resources.Values.Select((IRunResource resource) => new ResponseResourceTemplate(resource.Route, resource.Name, resource.Enabled, resource.MimeType, resource.Description)).ToArray().Pack(data.RequestID)
			.TaskFromResult();
	}

	internal IRunResource? FindResourceContentRunner(string uri, IDictionary<string, IRunResource> resources, out string? uriTemplate)
	{
		foreach (KeyValuePair<string, IRunResource> resource in resources)
		{
			if (IsMatch(resource.Value.Route, uri))
			{
				uriTemplate = resource.Value.Route;
				return resource.Value;
			}
		}
		uriTemplate = null;
		return null;
	}

	internal bool IsMatch(string uriTemplate, string uri)
	{
		string pattern = "^" + Regex.Replace(uriTemplate, "\\{(\\w+)\\}", "(?<$1>[^/]+)") + "(?:/.*)?$";
		return Regex.IsMatch(uri, pattern);
	}

	internal IDictionary<string, object?> ParseUriParameters(string pattern, string uri)
	{
		Dictionary<string, object> dictionary = new Dictionary<string, object> { { "uri", uri } };
		Regex regex = new Regex("^" + Regex.Replace(pattern, "\\{(\\w+)\\}", "(?<$1>.+)") + "(?:/.*)?$");
		Match match = regex.Match(uri);
		if (match.Success)
		{
			string[] groupNames = regex.GetGroupNames();
			foreach (string text in groupNames)
			{
				if (text != "0")
				{
					dictionary[text] = match.Groups[text].Value;
				}
			}
		}
		return dictionary;
	}

	private void PrintParameters(IDictionary<string, object?> parameters)
	{
		if (_logger.IsEnabled(LogLevel.Debug))
		{
			string text = string.Join(Environment.NewLine, parameters.Select<KeyValuePair<string, object>, string>((KeyValuePair<string, object> kvp) => string.Format("{0} = {1}", kvp.Key, kvp.Value ?? "null")));
			_logger.LogDebug("Parsed Parameters [{0}]:\n{1}", parameters.Count, text);
		}
	}

	public void Dispose()
	{
		_disposables.Dispose();
		_resources.Clear();
	}
}
}
