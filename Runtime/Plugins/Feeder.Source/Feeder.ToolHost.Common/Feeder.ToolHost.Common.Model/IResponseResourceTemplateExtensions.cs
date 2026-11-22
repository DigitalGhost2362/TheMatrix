using System;
using Feeder.Logging;

namespace Feeder.ToolHost.Common.Model
{
public static class IResponseResourceTemplateExtensions
{
	public static ResponseResourceTemplate[] Log(this ResponseResourceTemplate[] target, ILogger logger, Exception? ex = null)
	{
		if (!logger.IsEnabled(LogLevel.Debug))
		{
			return target;
		}
		foreach (ResponseResourceTemplate responseResourceTemplate in target)
		{
			logger.LogDebug(ex, "Resource Template: {0}", responseResourceTemplate.UriTemplate);
		}
		return target;
	}

	public static ResponseData<ResponseResourceTemplate[]> Pack(this ResponseResourceTemplate[] target, string requestId, string? message = null)
	{
		return ResponseData<ResponseResourceTemplate[]>.Success(requestId, message ?? "List Tool execution completed.").SetData(target);
	}
}
}
