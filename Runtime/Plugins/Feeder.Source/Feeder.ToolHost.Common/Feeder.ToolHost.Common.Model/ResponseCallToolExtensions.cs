using System;
using Feeder.Logging;

namespace Feeder.ToolHost.Common.Model
{
public static class ResponseCallToolExtensions
{
	public static ResponseCallTool Log(this ResponseCallTool target, ILogger logger, Exception? ex = null)
	{
		if (target.Status == ResponseStatus.Error)
		{
			logger.LogError(ex, "Error Response to AI:\n" + target.GetMessage());
		}
		else if (target.Status == ResponseStatus.Success)
		{
			logger.LogInformation(ex, "Success Response to AI:\n" + target.GetMessage());
		}
		else if (target.Status == ResponseStatus.Processing)
		{
			logger.LogInformation(ex, "Processing Response to AI:\n" + target.GetMessage());
		}
		return target;
	}

	public static ResponseData<ResponseCallTool> Pack(this ResponseCallTool target, string requestId, string? message = null)
	{
		if (target.Status == ResponseStatus.Error)
		{
			return ResponseData<ResponseCallTool>.Error(requestId, message ?? target.GetMessage() ?? "Tool execution error.").SetData(target);
		}
		if (target.Status == ResponseStatus.Success)
		{
			return ResponseData<ResponseCallTool>.Success(requestId, message ?? target.GetMessage() ?? "Tool executed successfully.").SetData(target);
		}
		if (target.Status == ResponseStatus.Processing)
		{
			return ResponseData<ResponseCallTool>.Processing(requestId, message ?? target.GetMessage() ?? "Tool is processing.").SetData(target);
		}
		return ResponseData<ResponseCallTool>.Error(requestId, $"Unknown tool status `{target.Status}`.").SetData(target);
	}
}
}
