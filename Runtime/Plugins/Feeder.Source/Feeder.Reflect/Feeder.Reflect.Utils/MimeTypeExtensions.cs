using System;

namespace Feeder.Reflect.Utils
{
public static class MimeTypeExtensions
{
	public static string ToString(this MimeType mimeType)
	{
		return mimeType switch
		{
			MimeType.TextPlain => "text/plain", 
			MimeType.TextHtml => "text/html", 
			MimeType.TextJson => "application/json", 
			MimeType.TextXml => "application/xml", 
			MimeType.TextYaml => "application/x-yaml", 
			MimeType.TextCsv => "text/csv", 
			MimeType.TextMarkdown => "text/markdown", 
			MimeType.TextJavascript => "application/javascript", 
			_ => throw new ArgumentOutOfRangeException("mimeType", mimeType, null), 
		};
	}
}
}
