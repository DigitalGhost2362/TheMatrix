using System;
using System.Collections.Generic;
using System.Linq;
using Feeder.Json.Nodes;

namespace Feeder.ToolHost.Common.Model
{
public class ResponseCallTool : IRequestID
{
	public string RequestID { get; set; } = string.Empty;

	public virtual ResponseStatus Status { get; set; }

	public virtual List<ContentBlock> Content { get; set; } = new List<ContentBlock>();

	public virtual JsonNode? StructuredContent { get; set; }

	public ResponseCallTool()
	{
	}

	public ResponseCallTool(ResponseStatus status, List<ContentBlock> content)
		: this(string.Empty, status, content)
	{
	}

	public ResponseCallTool(string requestId, ResponseStatus status, List<ContentBlock> content)
	{
		RequestID = requestId;
		Status = status;
		Content = content;
	}

	public ResponseCallTool(JsonNode? structuredContent, ResponseStatus status)
		: this(string.Empty, structuredContent, status)
	{
	}

	public ResponseCallTool(string requestId, JsonNode? structuredContent, ResponseStatus status)
	{
		RequestID = requestId;
		Status = status;
		StructuredContent = new JsonObject { ["result"] = structuredContent };
		Content = new List<ContentBlock>
		{
			new ContentBlock
			{
				Type = "text",
				Text = StructuredContent.ToJsonString(),
				MimeType = "application/json"
			}
		};
	}

	public ResponseCallTool SetRequestID(string requestId)
	{
		RequestID = requestId;
		return this;
	}

	public string? GetMessage()
	{
		return Content?.FirstOrDefault((ContentBlock item) => item.Type == "text" && !string.IsNullOrEmpty(item.Text))?.Text;
	}

	public static ResponseCallTool Error(Exception exception)
	{
		return Error("[Error] " + exception?.Message + "\n" + exception?.StackTrace);
	}

	public static ResponseCallTool Error(string? message = null)
	{
		return new ResponseCallTool(ResponseStatus.Error, new List<ContentBlock>
		{
			new ContentBlock
			{
				Type = "text",
				Text = message,
				MimeType = "text/plain"
			}
		});
	}

	public static ResponseCallTool Success(string? message = null)
	{
		return new ResponseCallTool(ResponseStatus.Success, new List<ContentBlock>
		{
			new ContentBlock
			{
				Type = "text",
				Text = message,
				MimeType = "text/plain"
			}
		});
	}

	public static ResponseCallTool SuccessStructured(JsonNode? structuredContent)
	{
		return new ResponseCallTool(structuredContent, ResponseStatus.Success);
	}

	public static ResponseCallTool ErrorStructured(JsonNode? structuredContent)
	{
		return new ResponseCallTool(structuredContent, ResponseStatus.Error);
	}

	public static ResponseCallTool Processing(string? message = null)
	{
		return new ResponseCallTool(ResponseStatus.Processing, new List<ContentBlock>
		{
			new ContentBlock
			{
				Type = "text",
				Text = message,
				MimeType = "text/plain"
			}
		});
	}

	public static ResponseCallTool Image(byte[] data, string mimeType, string? message = null)
	{
		List<ContentBlock> list = new List<ContentBlock> { ContentBlock.CreateImage(data, mimeType) };
		if (!string.IsNullOrEmpty(message))
		{
			list.Insert(0, ContentBlock.CreateText(message));
		}
		return new ResponseCallTool(ResponseStatus.Success, list);
	}

	public static ResponseCallTool Audio(byte[] data, string mimeType, string? message = null)
	{
		List<ContentBlock> list = new List<ContentBlock> { ContentBlock.CreateAudio(data, mimeType) };
		if (!string.IsNullOrEmpty(message))
		{
			list.Insert(0, ContentBlock.CreateText(message));
		}
		return new ResponseCallTool(ResponseStatus.Success, list);
	}

	public static ResponseCallTool WithContent(ResponseStatus status, params ContentBlock[] contentBlocks)
	{
		return new ResponseCallTool(status, contentBlocks.ToList());
	}
}
}
