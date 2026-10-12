using System;
using Feeder.Logging;

namespace Feeder.ToolHost.Common.Model
{
public static class ResponseResourceContentExtensions
{
	public static ResponseResourceContent[] Log(this ResponseResourceContent[] target, ILogger logger, Exception? ex = null)
	{
		if (!logger.IsEnabled(LogLevel.Debug))
		{
			return target;
		}
		foreach (ResponseResourceContent responseResourceContent in target)
		{
			logger.LogDebug(ex, "Resource: {0}", responseResourceContent.Uri);
		}
		return target;
	}

	public static ResponseData<ResponseResourceContent[]> Pack(this ResponseResourceContent[] target, string requestId, string? message = null)
	{
		return ResponseData<ResponseResourceContent[]>.Success(requestId, message ?? "List Tool execution completed.").SetData(target);
	}
}
}
