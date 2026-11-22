using System;

namespace Feeder.ToolHost.Common.Model
{
public class RequestListResources : IRequestID, IDisposable
{
	public string RequestID { get; set; } = Guid.NewGuid().ToString();

	public string? Cursor { get; set; }

	public RequestListResources()
	{
	}

	public RequestListResources(string? cursor = null)
		: this(Guid.NewGuid().ToString(), cursor)
	{
	}

	public RequestListResources(string requestId, string? cursor = null)
	{
		RequestID = requestId ?? throw new ArgumentNullException("requestId");
		Cursor = cursor;
	}

	public virtual void Dispose()
	{
	}

	~RequestListResources()
	{
		Dispose();
	}
}
}
