using System;

namespace Feeder.ToolHost
{
/// <summary>
/// A tool failed because of what its caller sent (bad input, or code the caller asked to run), not
/// because of a fault in the tool. The caller gets the full message; the Console gets one warning line.
/// </summary>
public class ToolCallerException : Exception
{
	public ToolCallerException(string message)
		: base(message)
	{
	}

	public ToolCallerException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
}
