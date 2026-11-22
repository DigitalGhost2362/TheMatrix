using System;

namespace Feeder.ToolHost.Common.Model
{
public class RequestResourceContent : IRequestID, IDisposable
{
	public string RequestID { get; set; } = string.Empty;

	public string Uri { get; set; } = string.Empty;

	public RequestResourceContent()
	{
	}

	public RequestResourceContent(string uri)
		: this(Guid.NewGuid().ToString(), uri)
	{
	}

	public RequestResourceContent(string requestId, string uri)
	{
		RequestID = requestId ?? throw new ArgumentNullException("requestId");
		Uri = uri ?? throw new ArgumentNullException("uri");
	}

	public virtual void Dispose()
	{
	}

	~RequestResourceContent()
	{
		Dispose();
	}
}
}
