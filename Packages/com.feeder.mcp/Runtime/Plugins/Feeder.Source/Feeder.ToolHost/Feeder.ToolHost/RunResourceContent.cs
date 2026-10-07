using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Feeder.ToolHost.Common.Model;
using Feeder.Reflect;
using Feeder.Logging;

namespace Feeder.ToolHost
{
public class RunResourceContent : MethodWrapper, IRunResourceContent
{
	public static RunResourceContent CreateFromStaticMethod(Reflector reflector, ILogger? logger, MethodInfo methodInfo)
	{
		return new RunResourceContent(reflector, logger, methodInfo);
	}

	public static RunResourceContent CreateFromInstanceMethod(Reflector reflector, ILogger? logger, object targetInstance, MethodInfo methodInfo)
	{
		return new RunResourceContent(reflector, logger, targetInstance, methodInfo);
	}

	public static RunResourceContent CreateFromClassMethod(Reflector reflector, ILogger? logger, Type targetType, MethodInfo methodInfo)
	{
		return new RunResourceContent(reflector, logger, targetType, methodInfo);
	}

	public RunResourceContent(Reflector reflector, ILogger? logger, MethodInfo methodInfo)
		: base(reflector, logger, methodInfo)
	{
	}

	public RunResourceContent(Reflector reflector, ILogger? logger, object targetInstance, MethodInfo methodInfo)
		: base(reflector, logger, targetInstance, methodInfo)
	{
	}

	public RunResourceContent(Reflector reflector, ILogger? logger, Type targetType, MethodInfo methodInfo)
		: base(reflector, logger, targetType, methodInfo)
	{
	}

	public async Task<ResponseResourceContent[]> Run(params object?[] parameters)
	{
		object obj = await Invoke(parameters);
		if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
		{
			_logger.LogTrace("Result: {result}", obj.ToJson(_reflector));
		}
		return (obj as ResponseResourceContent[]) ?? throw new InvalidOperationException("The method did not return a valid ResponseResourceContent[]. Instead returned " + obj?.GetType().GetTypeShortName() + ".");
	}

	public async Task<ResponseResourceContent[]> Run(IDictionary<string, object?>? namedParameters)
	{
		object obj = await InvokeDict((IReadOnlyDictionary<string, object>)namedParameters);
		if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
		{
			_logger.LogTrace("Result: {result}", obj.ToJson(_reflector));
		}
		return (obj as ResponseResourceContent[]) ?? throw new InvalidOperationException("The method did not return a valid ResponseResourceContent[]. Instead returned " + obj?.GetType().GetTypeShortName() + ".");
	}
}
}
