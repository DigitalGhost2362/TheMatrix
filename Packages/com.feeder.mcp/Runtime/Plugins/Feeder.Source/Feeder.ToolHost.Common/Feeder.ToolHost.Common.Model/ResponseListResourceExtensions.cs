using System;
using Feeder.Logging;

namespace Feeder.ToolHost.Common.Model
{
public static class ResponseListResourceExtensions
{
	public static ResponseListResource[] Log(this ResponseListResource[] target, ILogger logger, Exception? ex = null)
	{
		if (!logger.IsEnabled(LogLevel.Debug))
		{
			return target;
		}
		foreach (ResponseListResource responseListResource in target)
		{
			logger.LogDebug(ex, "Resource: {0}", responseListResource.Uri);
		}
		return target;
	}

	public static ResponseData<ResponseListResource[]> Pack(this ResponseListResource[] target, string requestId, string? message = null)
	{
		return ResponseData<ResponseListResource[]>.Success(requestId, message ?? "List Tool execution completed.").SetData(target);
	}
}
}
