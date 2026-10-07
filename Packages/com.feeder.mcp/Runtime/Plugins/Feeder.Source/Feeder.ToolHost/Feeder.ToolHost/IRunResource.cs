using Feeder.Json.Serialization;

namespace Feeder.ToolHost
{
public interface IRunResource : IEnabled
{
	string Route { get; set; }

	string Name { get; set; }

	string? Description { get; set; }

	string? MimeType { get; set; }

	[JsonIgnore]
	IRunResourceContent RunGetContent { get; set; }

	[JsonIgnore]
	IRunResourceList RunListContext { get; set; }
}
}
