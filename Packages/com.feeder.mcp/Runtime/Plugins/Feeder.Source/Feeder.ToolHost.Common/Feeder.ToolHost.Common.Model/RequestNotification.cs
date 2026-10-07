using System;
using System.Collections.Generic;

namespace Feeder.ToolHost.Common.Model
{
public class RequestNotification : IRequestNotification, IRequestID, IDisposable
{
	public string RequestID { get; set; } = string.Empty;

	public string? Path { get; set; }

	public string? Name { get; set; }

	public IDictionary<string, object?>? Parameters { get; set; } = new Dictionary<string, object>();

	public RequestNotification()
	{
	}

	public RequestNotification(string path, string name)
		: this(Guid.NewGuid().ToString(), path, name)
	{
	}

	public RequestNotification(string requestId, string path, string name)
		: this()
	{
		RequestID = requestId ?? throw new ArgumentNullException("requestId");
		Path = path ?? throw new ArgumentNullException("path");
		Name = name ?? throw new ArgumentNullException("name");
	}

	public virtual void Dispose()
	{
		Parameters?.Clear();
	}

	~RequestNotification()
	{
		Dispose();
	}
}
}
