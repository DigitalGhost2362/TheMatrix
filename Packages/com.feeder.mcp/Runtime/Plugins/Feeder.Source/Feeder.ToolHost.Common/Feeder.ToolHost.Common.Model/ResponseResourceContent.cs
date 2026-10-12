namespace Feeder.ToolHost.Common.Model
{
public class ResponseResourceContent
{
	public string Uri { get; set; } = string.Empty;

	public string? MimeType { get; set; }

	public string? Text { get; set; }

	public string? Blob { get; set; }

	public ResponseResourceContent()
	{
	}

	public ResponseResourceContent(string uri, string? mimeType = null, string? text = null, string? blob = null)
	{
		Uri = uri;
		MimeType = mimeType;
		Text = text;
		Blob = blob;
	}

	public static ResponseResourceContent CreateText(string uri, string text, string? mimeType = null)
	{
		return new ResponseResourceContent(uri, mimeType, text);
	}

	public static ResponseResourceContent CreateBlob(string uri, string blob, string? mimeType = null)
	{
		return new ResponseResourceContent(uri, mimeType, null, blob);
	}
}
}
