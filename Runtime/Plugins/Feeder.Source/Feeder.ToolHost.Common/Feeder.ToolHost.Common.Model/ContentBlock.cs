using System;

namespace Feeder.ToolHost.Common.Model
{
public class ContentBlock
{
	public string Type { get; set; } = string.Empty;

	public string? Text { get; set; }

	public string? Data { get; set; }

	public string? MimeType { get; set; }

	public ResponseResourceContent? Resource { get; set; }

	public static ContentBlock CreateText(string text, string mimeType = "text/plain")
	{
		return new ContentBlock
		{
			Type = "text",
			Text = text,
			MimeType = mimeType
		};
	}

	public static ContentBlock CreateImage(byte[] data, string mimeType)
	{
		return new ContentBlock
		{
			Type = "image",
			Data = Convert.ToBase64String(data),
			MimeType = mimeType
		};
	}

	public static ContentBlock CreateImageBase64(string base64Data, string mimeType)
	{
		return new ContentBlock
		{
			Type = "image",
			Data = base64Data,
			MimeType = mimeType
		};
	}

	public static ContentBlock CreateAudio(byte[] data, string mimeType)
	{
		return new ContentBlock
		{
			Type = "audio",
			Data = Convert.ToBase64String(data),
			MimeType = mimeType
		};
	}

	public static ContentBlock CreateAudioBase64(string base64Data, string mimeType)
	{
		return new ContentBlock
		{
			Type = "audio",
			Data = base64Data,
			MimeType = mimeType
		};
	}

	public static ContentBlock CreateResource(ResponseResourceContent resource)
	{
		return new ContentBlock
		{
			Type = "resource",
			Resource = resource
		};
	}
}
}
