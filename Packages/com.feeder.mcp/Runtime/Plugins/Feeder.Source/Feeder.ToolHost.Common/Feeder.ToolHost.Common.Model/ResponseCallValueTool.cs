using System;
using System.Collections.Generic;
using Feeder.Json.Nodes;

namespace Feeder.ToolHost.Common.Model
{
public class ResponseCallValueTool<T> : ResponseCallTool
{
	public ResponseCallValueTool()
	{
	}

	public ResponseCallValueTool(ResponseStatus status, List<ContentBlock> content)
		: base(string.Empty, status, content)
	{
	}

	public ResponseCallValueTool(string requestId, ResponseStatus status, List<ContentBlock> content)
		: base(requestId, status, content)
	{
	}

	public ResponseCallValueTool(JsonNode? structuredContent, ResponseStatus status)
		: base(string.Empty, structuredContent, status)
	{
	}

	public ResponseCallValueTool(string requestId, JsonNode? structuredContent, ResponseStatus status)
		: base(requestId, structuredContent, status)
	{
	}

	public new ResponseCallValueTool<T> SetRequestID(string requestId)
	{
		base.RequestID = requestId;
		return this;
	}

	public new static ResponseCallValueTool<T> Error(Exception exception)
	{
		return Error("[Error] " + exception?.Message + "\n" + exception?.StackTrace);
	}

	public new static ResponseCallValueTool<T> Error(string? message = null)
	{
		return new ResponseCallValueTool<T>(ResponseStatus.Error, new List<ContentBlock>
		{
			new ContentBlock
			{
				Type = "text",
				Text = message,
				MimeType = "text/plain"
			}
		});
	}

	public new static ResponseCallValueTool<T> Success(string? message = null)
	{
		return new ResponseCallValueTool<T>(ResponseStatus.Success, new List<ContentBlock>
		{
			new ContentBlock
			{
				Type = "text",
				Text = message,
				MimeType = "text/plain"
			}
		});
	}

	public new static ResponseCallValueTool<T> SuccessStructured(JsonNode? structuredContent)
	{
		return new ResponseCallValueTool<T>(structuredContent, ResponseStatus.Success);
	}

	public new static ResponseCallValueTool<T> ErrorStructured(JsonNode? structuredContent)
	{
		return new ResponseCallValueTool<T>(structuredContent, ResponseStatus.Error);
	}

	public new static ResponseCallValueTool<T> Processing(string? message = null)
	{
		return new ResponseCallValueTool<T>(ResponseStatus.Processing, new List<ContentBlock>
		{
			new ContentBlock
			{
				Type = "text",
				Text = message,
				MimeType = "text/plain"
			}
		});
	}
}
}
