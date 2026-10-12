using System;

namespace Feeder.ToolHost.Common.Model
{
public class RequestListTool : IRequestID, IDisposable
{
	public string RequestID { get; set; } = Guid.NewGuid().ToString();

	public RequestListTool()
	{
	}

	public RequestListTool(string requestId)
	{
		RequestID = requestId ?? throw new ArgumentNullException("requestId");
	}

	public virtual void Dispose()
	{
	}

	~RequestListTool()
	{
		Dispose();
	}
}
}
