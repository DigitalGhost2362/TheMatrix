namespace Feeder.ToolHost.Common.Model
{
public class ResponseListResource
{
	public string Uri { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public bool Enabled { get; set; } = true;

	public string? MimeType { get; set; }

	public string? Description { get; set; }

	public long? Size { get; set; }

	public ResponseListResource()
	{
	}

	public ResponseListResource(string uri, string name, bool enabled = true, string? mimeType = null, string? description = null, long? size = null)
	{
		Uri = uri;
		Name = name;
		Enabled = enabled;
		MimeType = mimeType;
		Description = description;
		Size = size;
	}
}
}
