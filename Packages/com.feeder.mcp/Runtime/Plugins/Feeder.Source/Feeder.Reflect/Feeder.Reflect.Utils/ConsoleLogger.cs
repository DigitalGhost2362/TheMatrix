using System;
using Feeder.Logging;

namespace Feeder.Reflect.Utils
{
public class ConsoleLogger : ILogger
{
	private readonly string _categoryName;

	public ConsoleLogger(string categoryName)
	{
		_categoryName = (categoryName.Contains(".") ? categoryName.Substring(categoryName.LastIndexOf('.') + 1) : categoryName);
	}

	IDisposable ILogger.BeginScope<TState>(TState state)
	{
		return null;
	}

	public bool IsEnabled(LogLevel logLevel)
	{
		return true;
	}

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		if (formatter == null)
		{
			throw new ArgumentNullException("formatter");
		}
		if (state == null)
		{
			throw new ArgumentNullException("state");
		}
		Console.WriteLine(string.Format("{0} {1}{2}{3} {4}[{5}]{6} {7}", "[Reflector]", "", _categoryName, "", "", logLevel, "", formatter(state, exception)));
	}
}
}
