using System.Collections.Generic;
using Feeder.Json;
using Feeder.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Feeder.ToolHost.Common.Model;

namespace Feeder.ToolHost
{
public interface IRunTool : IEnabled
{
	string Name { get; }

	string? Title { get; }

	string? Description { get; }

	string? SkillDescription { get; }

	string? SkillBody { get; }

	JsonNode? InputSchema { get; }

	JsonNode? OutputSchema { get; }

	McpToolType ToolType => McpToolType.Standard;

	bool? ReadOnlyHint { get; }

	bool? DestructiveHint { get; }

	bool? IdempotentHint { get; }

	bool? OpenWorldHint { get; }

	int TokenCount { get; }

	Task<ResponseCallTool> Run(string requestId, IReadOnlyDictionary<string, JsonElement>? namedParameters, CancellationToken cancellationToken = default(CancellationToken));
}
}
