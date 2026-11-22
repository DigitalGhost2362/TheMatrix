using System;
using System.Collections.Generic;
using System.Linq;
using Feeder.Reflect;
using Feeder.Logging;

namespace Feeder.ToolHost
{
public class PromptRunnerCollection : Dictionary<string, IRunPrompt>
{
	private readonly Reflector reflector;

	private readonly ILogger? _logger;

	public PromptRunnerCollection(Reflector reflector, ILogger? logger)
	{
		this.reflector = reflector ?? throw new ArgumentNullException("reflector");
		_logger = logger;
		_logger?.LogTrace("Ctor.");
	}

	public PromptRunnerCollection Add(IEnumerable<PromptMethodData> methods)
	{
		foreach (PromptMethodData item in methods.Where((PromptMethodData resource) => !string.IsNullOrEmpty(resource.Attribute?.Name)))
		{
			AiPromptAttribute attribute = item.Attribute;
			base[attribute.Name] = (item.MethodInfo.IsStatic ? RunPrompt.CreateFromStaticMethod(reflector, attribute.Name, _logger, item.MethodInfo, null, attribute.EnabledValue) : RunPrompt.CreateFromClassMethod(reflector, attribute.Name, _logger, item.ClassType, item.MethodInfo, null, attribute.EnabledValue));
		}
		return this;
	}

	public PromptRunnerCollection Add(IDictionary<string, IRunPrompt> runners)
	{
		if (runners == null)
		{
			throw new ArgumentNullException("runners");
		}
		foreach (KeyValuePair<string, IRunPrompt> runner in runners)
		{
			Add(runner.Key, runner.Value);
		}
		return this;
	}
}
}
