using System;
using Feeder.ToolHost.Common.Utils;
using Feeder.Logging;

namespace Feeder.ToolHost.Common.Model
{
public static class ResponseListToolExtensions
{
	public static ResponseListTool[] Log(this ResponseListTool[] response, ILogger logger, Exception? ex = null)
	{
		if (!logger.IsEnabled(LogLevel.Debug))
		{
			return response;
		}
		foreach (ResponseListTool responseListTool in response)
		{
			logger.LogDebug(ex, "Tool: " + responseListTool.Name + ":\n" + responseListTool.ToPrettyJson());
		}
		return response;
	}

	public static ResponseData<ResponseListTool[]> Pack(this ResponseListTool[] response, string requestId, string? message = null)
	{
		return ResponseData<ResponseListTool[]>.Success(requestId, message ?? "List Tool execution completed.").SetData(response);
	}
}
}
