using System;
using System.Collections.Generic;
using System.Linq;
using Feeder.Reflect;
using Feeder.Logging;

namespace Feeder.ToolHost
{
public class ResourceRunnerCollection : Dictionary<string, IRunResource>
{
	private readonly Reflector reflector;

	private readonly ILogger? _logger;

	public ResourceRunnerCollection(Reflector reflector, ILogger? logger)
	{
		this.reflector = reflector ?? throw new ArgumentNullException("reflector");
		_logger = logger;
		_logger?.LogTrace("Ctor.");
	}

	public ResourceRunnerCollection Add(IEnumerable<ResourceMethodData> methods)
	{
		foreach (ResourceMethodData item in methods.Where((ResourceMethodData resource) => !string.IsNullOrEmpty(resource.Attribute?.Name)))
		{
			AiResourceAttribute attribute = item.Attribute;
			string? name = attribute.Name;
			if (string.IsNullOrWhiteSpace(attribute.Route))
			{
				throw new InvalidOperationException("Method " + item.ClassType.FullName + "." + item.GetContentMethod.Name + " does not have a 'route'.");
			}
			base[name] = new RunResource(attribute.Route, attribute.Name ?? throw new InvalidOperationException("Method " + item.ClassType.FullName + "." + item.GetContentMethod.Name + " does not have a 'name'."), description: attribute.Description, mimeType: attribute.MimeType, runnerGetContent: item.GetContentMethod.IsStatic ? RunResourceContent.CreateFromStaticMethod(reflector, _logger, item.GetContentMethod) : RunResourceContent.CreateFromClassMethod(reflector, _logger, item.ClassType, item.GetContentMethod), runnerListContext: item.ListResourcesMethod.IsStatic ? RunResourceList.CreateFromStaticMethod(reflector, _logger, item.ListResourcesMethod) : RunResourceList.CreateFromClassMethod(reflector, _logger, item.ClassType, item.ListResourcesMethod), enabled: attribute.EnabledValue);
		}
		return this;
	}

	public ResourceRunnerCollection Add(IDictionary<string, IRunResource> runners)
	{
		if (runners == null)
		{
			throw new ArgumentNullException("runners");
		}
		foreach (KeyValuePair<string, IRunResource> runner in runners)
		{
			Add(runner.Key, runner.Value);
		}
		return this;
	}
}
}
