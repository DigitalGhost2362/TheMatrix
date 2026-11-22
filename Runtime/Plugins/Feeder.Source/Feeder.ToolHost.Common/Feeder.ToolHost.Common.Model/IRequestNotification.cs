using System;
using System.Collections.Generic;

namespace Feeder.ToolHost.Common.Model
{
public interface IRequestNotification : IRequestID, IDisposable
{
	string? Path { get; set; }

	string? Name { get; set; }

	IDictionary<string, object?>? Parameters { get; set; }
}
}
