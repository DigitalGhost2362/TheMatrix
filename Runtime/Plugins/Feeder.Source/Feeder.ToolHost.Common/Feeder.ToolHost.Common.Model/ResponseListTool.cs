using Feeder.Json;

namespace Feeder.ToolHost.Common.Model
{
public class ResponseListTool
{
	public string Name { get; set; } = string.Empty;

	public bool Enabled { get; set; } = true;

	public string? Title { get; set; }

	public string? Description { get; set; }

	public JsonElement InputSchema { get; set; }

	public JsonElement? OutputSchema { get; set; }

	public bool? ReadOnlyHint { get; set; }

	public bool? DestructiveHint { get; set; }

	public bool? IdempotentHint { get; set; }

	public bool? OpenWorldHint { get; set; }
}
}
