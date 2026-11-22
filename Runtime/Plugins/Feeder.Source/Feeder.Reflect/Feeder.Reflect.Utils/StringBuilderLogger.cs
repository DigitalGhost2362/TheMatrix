using System;
using System.Text;
using Feeder.Logging;

namespace Feeder.Reflect.Utils
{
public class StringBuilderLogger : ILogger
{
	private readonly string? _categoryName;

	private readonly StringBuilder _stringBuilder;

	public StringBuilderLogger(string? categoryName = null)
		: this(new StringBuilder(), categoryName)
	{
	}

	public StringBuilderLogger(StringBuilder stringBuilder, string? categoryName = null)
	{
		_stringBuilder = stringBuilder;
		_categoryName = ((categoryName == null || !categoryName.Contains(".")) ? categoryName : categoryName?.Substring(categoryName.LastIndexOf('.') + 1));
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
		string text = logLevel switch
		{
			LogLevel.Trace => "trce: ", 
			LogLevel.Debug => "dbug: ", 
			LogLevel.Information => "info: ", 
			LogLevel.Warning => "warn: ", 
			LogLevel.Error => "fail: ", 
			LogLevel.Critical => "crit: ", 
			_ => throw new ArgumentOutOfRangeException("logLevel", logLevel, null), 
		};
		string value = (string.IsNullOrEmpty(_categoryName) ? ("[Reflector] " + text + " " + formatter(state, exception)) : ("[Reflector] " + _categoryName + " " + text + " " + formatter(state, exception)));
		_stringBuilder.AppendLine(value);
	}

	public void Clear()
	{
		_stringBuilder.Clear();
	}

	public override string ToString()
	{
		return _stringBuilder.ToString();
	}
}
}
