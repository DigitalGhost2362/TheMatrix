using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Feeder.ToolHost.Common.Model;
using Feeder.Reflect;
using Feeder.Logging;

namespace Feeder.ToolHost
{
public class RunResourceList : MethodWrapper, IRunResourceList
{
	public static RunResourceList CreateFromStaticMethod(Reflector reflector, ILogger? logger, MethodInfo methodInfo)
	{
		return new RunResourceList(reflector, logger, methodInfo);
	}

	public static RunResourceList CreateFromInstanceMethod(Reflector reflector, ILogger? logger, object targetInstance, MethodInfo methodInfo)
	{
		return new RunResourceList(reflector, logger, targetInstance, methodInfo);
	}

	public static RunResourceList CreateFromClassMethod(Reflector reflector, ILogger? logger, Type targetType, MethodInfo methodInfo)
	{
		return new RunResourceList(reflector, logger, targetType, methodInfo);
	}

	public RunResourceList(Reflector reflector, ILogger? logger, MethodInfo methodInfo)
		: base(reflector, logger, methodInfo)
	{
	}

	public RunResourceList(Reflector reflector, ILogger? logger, object targetInstance, MethodInfo methodInfo)
		: base(reflector, logger, targetInstance, methodInfo)
	{
	}

	public RunResourceList(Reflector reflector, ILogger? logger, Type targetType, MethodInfo methodInfo)
		: base(reflector, logger, targetType, methodInfo)
	{
	}

	public async Task<ResponseListResource[]> Run(params object?[] parameters)
	{
		object obj = await Invoke(parameters);
		return (obj as ResponseListResource[]) ?? throw new InvalidOperationException("The method did not return a valid ResponseListResource[]. Instead returned " + obj?.GetType().GetTypeShortName() + ".");
	}

	public async Task<ResponseListResource[]> Run(IDictionary<string, object?>? namedParameters)
	{
		object obj = await InvokeDict((IReadOnlyDictionary<string, object>)namedParameters);
		return (obj as ResponseListResource[]) ?? throw new InvalidOperationException("The method did not return a valid ResponseListResource[]. Instead returned " + obj?.GetType().GetTypeShortName() + ".");
	}
}
}
