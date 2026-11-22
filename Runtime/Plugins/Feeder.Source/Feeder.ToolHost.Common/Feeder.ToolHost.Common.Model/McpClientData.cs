using Feeder.Json.Serialization;

namespace Feeder.ToolHost.Common.Model
{
public class McpClientData
{
	[JsonPropertyName("isConnected")]
	public bool IsConnected { get; set; }

	[JsonPropertyName("sessionId")]
	public string? SessionId { get; set; }

	[JsonPropertyName("clientTitle")]
	public string? ClientTitle { get; set; }

	[JsonPropertyName("clientName")]
	public string? ClientName { get; set; }

	[JsonPropertyName("clientVersion")]
	public string? ClientVersion { get; set; }

	[JsonPropertyName("clientDescription")]
	public string? ClientDescription { get; set; }

	[JsonPropertyName("clientWebsiteUrl")]
	public string? ClientWebsiteUrl { get; set; }
}
}
