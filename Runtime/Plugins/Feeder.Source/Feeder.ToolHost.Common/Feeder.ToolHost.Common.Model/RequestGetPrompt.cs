using System;
using System.Collections.Generic;
using Feeder.Json;

namespace Feeder.ToolHost.Common.Model
{
public class RequestGetPrompt : IRequestID, IDisposable
{
	public string RequestID { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public IReadOnlyDictionary<string, JsonElement> Arguments { get; set; } = new Dictionary<string, JsonElement>();

	public RequestGetPrompt()
	{
	}

	public RequestGetPrompt(string name, IReadOnlyDictionary<string, JsonElement> arguments)
		: this(Guid.NewGuid().ToString(), name, arguments)
	{
	}

	public RequestGetPrompt(string requestId, string name, IReadOnlyDictionary<string, JsonElement> arguments)
	{
		RequestID = requestId ?? throw new ArgumentNullException("requestId");
		Name = name ?? throw new ArgumentNullException("name");
		Arguments = arguments ?? throw new ArgumentNullException("arguments");
	}

	public virtual void Dispose()
	{
	}

	~RequestGetPrompt()
	{
		Dispose();
	}
}
}
