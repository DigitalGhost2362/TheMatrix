namespace Feeder.ToolHost.Common.Model
{
public class ResponseResourceTemplate
{
	public string UriTemplate { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public bool Enabled { get; set; } = true;

	public string? MimeType { get; set; }

	public string? Description { get; set; }

	public ResponseResourceTemplate()
	{
	}

	public ResponseResourceTemplate(string uri, string name, bool enabled = true, string? mimeType = null, string? description = null)
	{
		UriTemplate = uri;
		Name = name;
		Enabled = enabled;
		MimeType = mimeType;
		Description = description;
	}
}
}
