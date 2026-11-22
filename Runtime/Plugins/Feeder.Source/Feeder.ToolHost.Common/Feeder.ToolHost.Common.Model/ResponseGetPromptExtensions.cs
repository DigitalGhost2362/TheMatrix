using System;
using Feeder.Logging;

namespace Feeder.ToolHost.Common.Model
{
public static class ResponseGetPromptExtensions
{
	public static ResponseGetPrompt Log(this ResponseGetPrompt target, ILogger logger, Exception? ex = null)
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

	public static ResponseData<ResponseGetPrompt> Pack(this ResponseGetPrompt target, string requestId, string? message = null)
	{
		if (target.Status == ResponseStatus.Error)
		{
			return ResponseData<ResponseGetPrompt>.Error(requestId, message ?? target.GetMessage() ?? "Prompt execution error.").SetData(target);
		}
		if (target.Status == ResponseStatus.Success)
		{
			return ResponseData<ResponseGetPrompt>.Success(requestId, message ?? target.GetMessage() ?? "Prompt executed successfully.").SetData(target);
		}
		if (target.Status == ResponseStatus.Processing)
		{
			return ResponseData<ResponseGetPrompt>.Processing(requestId, message ?? target.GetMessage() ?? "Prompt is processing.").SetData(target);
		}
		return ResponseData<ResponseGetPrompt>.Error(requestId, $"Unknown tool status `{target.Status}`.").SetData(target);
	}
}
}
