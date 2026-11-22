using Feeder.Json.Serialization;

namespace Feeder.ToolHost
{
public class RunResource : IRunResource, IEnabled
{
	public bool Enabled { get; set; } = true;

	public string Route { get; set; }

	public string Name { get; set; }

	public string? Description { get; set; }

	public string? MimeType { get; set; }

	[JsonIgnore]
	public IRunResourceContent RunGetContent { get; set; }

	[JsonIgnore]
	public IRunResourceList RunListContext { get; set; }

	public RunResource(string route, string name, IRunResourceContent runnerGetContent, IRunResourceList runnerListContext, string? description = null, string? mimeType = null, bool? enabled = null)
	{
		Route = route;
		Name = name;
		RunGetContent = runnerGetContent;
		RunListContext = runnerListContext;
		Description = description;
		MimeType = mimeType;
		Enabled = enabled ?? true;
	}
}
}
