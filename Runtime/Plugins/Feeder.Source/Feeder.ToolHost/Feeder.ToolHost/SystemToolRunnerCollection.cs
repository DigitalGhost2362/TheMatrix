using System;
using System.Collections.Generic;
using System.Linq;
using Feeder.Reflect;
using Feeder.Logging;

namespace Feeder.ToolHost
{
public class SystemToolRunnerCollection : Dictionary<string, IRunTool>
{
	private readonly Reflector reflector;

	private readonly ILogger? _logger;

	public SystemToolRunnerCollection(Reflector reflector, ILogger? logger)
	{
		this.reflector = reflector ?? throw new ArgumentNullException("reflector");
		_logger = logger;
		_logger?.LogTrace("Ctor.");
	}

	public SystemToolRunnerCollection Add(IEnumerable<ToolMethodData> methods)
	{
		foreach (ToolMethodData item in methods.Where((ToolMethodData m) => !string.IsNullOrEmpty(m.Attribute?.Name)))
		{
			base[item.Attribute.Name] = RunToolFactory.Create(item, reflector, _logger);
		}
		return this;
	}

	public SystemToolRunnerCollection Add(IDictionary<string, IRunTool> runners)
	{
		if (runners == null)
		{
			throw new ArgumentNullException("runners");
		}
		foreach (KeyValuePair<string, IRunTool> runner in runners)
		{
			base[runner.Key] = runner.Value;
		}
		return this;
	}
}
}
