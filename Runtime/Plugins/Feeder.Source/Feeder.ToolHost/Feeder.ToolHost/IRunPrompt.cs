using System.Collections.Generic;
using Feeder.Json;
using Feeder.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Feeder.ToolHost.Common.Model;

namespace Feeder.ToolHost
{
public interface IRunPrompt : IEnabled
{
	string Name { get; }

	string? Title { get; }

	string? Description { get; }

	Role Role { get; }

	JsonNode? InputSchema { get; }

	Task<ResponseGetPrompt> Run(string requestId, IReadOnlyDictionary<string, JsonElement>? namedParameters, CancellationToken cancellationToken = default(CancellationToken));
}
}
