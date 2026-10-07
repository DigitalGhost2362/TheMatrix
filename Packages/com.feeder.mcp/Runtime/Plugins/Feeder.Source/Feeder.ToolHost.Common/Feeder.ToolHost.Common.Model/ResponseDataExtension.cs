using System;
using System.Threading.Tasks;
using Feeder.Logging;

namespace Feeder.ToolHost.Common.Model
{
public static class ResponseDataExtension
{
	public static ResponseData<T> Log<T>(this ResponseData<T> response, ILogger logger, string? operationContext = null, Exception? ex = null)
	{
		string text = (string.IsNullOrEmpty(operationContext) ? string.Empty : ("[" + operationContext + "] "));
		switch (response.Status)
		{
		case ResponseStatus.Error:
		{
			string text3 = response.Message ?? "Operation failed without specific error details";
			logger.LogError(ex, "{Context}Error: {Message}", text, text3);
			break;
		}
		case ResponseStatus.Success:
		{
			string text2 = response.Message ?? "Operation completed successfully";
			logger.LogInformation("{Context}Success: {Message}", text, text2);
			break;
		}
		case ResponseStatus.Processing:
		{
			string text4 = response.Message ?? "Operation is currently being processed";
			logger.LogInformation("{Context}Processing: {Message}", text, text4);
			break;
		}
		default:
			logger.LogWarning("{Context}Unknown status '{Status}' with message: {Message}", text, response.Status, response.Message ?? "No message provided");
			break;
		}
		return response;
	}

	public static ResponseData<T> SetData<T>(this ResponseData<T> response, T? data)
	{
		response.Value = data;
		return response;
	}

	public static ResponseData<T> SetError<T>(this ResponseData<T> response, string? message = null, Exception? exception = null)
	{
		response.Status = ResponseStatus.Error;
		if (exception != null && string.IsNullOrEmpty(message))
		{
			response.Message = "Operation failed: " + exception.Message;
		}
		else if (exception != null && !string.IsNullOrEmpty(message))
		{
			response.Message = message + ". Details: " + exception.Message;
		}
		else
		{
			response.Message = message ?? "Operation failed without specific error details";
		}
		return response;
	}

	public static ResponseData<T> SetSuccess<T>(this ResponseData<T> response, string? message = null)
	{
		response.Status = ResponseStatus.Success;
		response.Message = message ?? "Operation completed successfully";
		return response;
	}

	public static ResponseData<T> SetProcessing<T>(this ResponseData<T> response, string? message = null)
	{
		response.Status = ResponseStatus.Processing;
		response.Message = message ?? "Operation is currently being processed";
		return response;
	}

	public static Task<ResponseData<T>> TaskFromResult<T>(this ResponseData<T> response)
	{
		return Task.FromResult(response);
	}

	public static Task<ResponseData> TaskFromResult(this ResponseData response)
	{
		return Task.FromResult(response);
	}
}
}
