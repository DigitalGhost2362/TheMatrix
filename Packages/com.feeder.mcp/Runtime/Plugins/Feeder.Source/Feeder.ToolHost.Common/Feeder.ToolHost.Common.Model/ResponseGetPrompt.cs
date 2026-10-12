using System;
using System.Collections.Generic;

namespace Feeder.ToolHost.Common.Model
{
public class ResponseGetPrompt : IRequestID
{
	public string RequestID { get; set; } = string.Empty;

	public virtual ResponseStatus Status { get; set; }

	public virtual string? Description { get; set; }

	public virtual List<ResponsePromptMessage> Messages { get; set; } = new List<ResponsePromptMessage>();

	public ResponseGetPrompt()
	{
	}

	public ResponseGetPrompt(ResponseStatus status, List<ResponsePromptMessage> messages, string? description = null)
		: this(string.Empty, status, messages, description)
	{
	}

	public ResponseGetPrompt(string requestId, ResponseStatus status, List<ResponsePromptMessage> messages, string? description = null)
	{
		RequestID = requestId;
		Status = status;
		Messages = messages;
		Description = description;
	}

	public ResponseGetPrompt SetRequestID(string requestId)
	{
		RequestID = requestId;
		return this;
	}

	public string? GetMessage()
	{
		return Description;
	}

	public static ResponseGetPrompt Error(Exception exception)
	{
		return Error("[Error] " + exception?.Message + "\n" + exception?.StackTrace);
	}

	public static ResponseGetPrompt Error(string? description = null)
	{
		return new ResponseGetPrompt(ResponseStatus.Error, new List<ResponsePromptMessage>(), description);
	}

	public static ResponseGetPrompt Success(string message, Role role, string? description = null)
	{
		return Success(new List<ResponsePromptMessage>
		{
			new ResponsePromptMessage(message, role)
		}, description);
	}

	public static ResponseGetPrompt Success(ResponsePromptMessage message, string? description = null)
	{
		return Success(new List<ResponsePromptMessage> { message }, description);
	}

	public static ResponseGetPrompt Success(List<ResponsePromptMessage> messages, string? description = null)
	{
		return new ResponseGetPrompt(ResponseStatus.Success, description: description, messages: messages ?? new List<ResponsePromptMessage>());
	}

	public static ResponseGetPrompt Processing(List<ResponsePromptMessage>? messages = null, string? description = null)
	{
		return new ResponseGetPrompt(ResponseStatus.Processing, description: description, messages: messages ?? new List<ResponsePromptMessage>());
	}
}
}
