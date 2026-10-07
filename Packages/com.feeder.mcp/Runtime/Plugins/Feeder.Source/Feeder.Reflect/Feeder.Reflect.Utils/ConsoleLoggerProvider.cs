using System;
using Feeder.Logging;

namespace Feeder.Reflect.Utils
{
public class ConsoleLoggerProvider : ILoggerProvider, IDisposable
{
	public void Dispose()
	{
	}

	ILogger ILoggerProvider.CreateLogger(string categoryName)
	{
		return new ConsoleLogger(categoryName);
	}
}
}
