using System;
using System.Collections.Generic;
using Feeder.Logging;

namespace Feeder.Reflect.Utils
{
public class Printer : IDisposable
{
	private class LazyLine
	{
		public Func<string> Line { get; }

		public int Depth { get; }

		public LazyLine(Func<string> line, int depth)
		{
			Line = line;
			Depth = depth;
		}
	}

	private readonly LinkedList<LazyLine> _lazyLines = new LinkedList<LazyLine>();

	private readonly ILogger? _aiLogger;

	private readonly ILogger? _systemLogger;

	private int lastDepth = -1;

	public Printer(ILogger? logger)
		: this(new StringBuilderLogger(), logger)
	{
	}

	public Printer(ILogger? aiLogger = null, ILogger? systemLogger = null)
	{
		_aiLogger = aiLogger;
		_systemLogger = systemLogger;
	}

	public void TraceLog(Func<string> lazyLine, int depth = 0)
	{
		LogLine(LogLevel.Trace, lazyLine, depth);
	}

	public void DebugLog(Func<string> lazyLine, int depth = 0)
	{
		LogLine(LogLevel.Debug, lazyLine, depth);
	}

	public void InfoLog(Func<string> lazyLine, int depth = 0)
	{
		LogLine(LogLevel.Information, lazyLine, depth);
	}

	public void WarningLog(Func<string> lazyLine, int depth = 0)
	{
		LogLine(LogLevel.Warning, lazyLine, depth);
	}

	public void ErrorLog(Func<string> lazyLine, int depth = 0)
	{
		LogLine(LogLevel.Error, lazyLine, depth);
	}

	public void CriticalLog(Func<string> lazyLine, int depth = 0)
	{
		LogLine(LogLevel.Critical, lazyLine, depth);
	}

	public void LogLine(LogLevel level, Func<string> lazyLine, int depth = 0)
	{
		ILogger? aiLogger = _aiLogger;
		bool flag = (aiLogger != null && aiLogger.IsEnabled(level)) || (_systemLogger?.IsEnabled(level) ?? false);
		while (_lazyLines.Count > 0 && _lazyLines.Last.Value.Depth > depth)
		{
			_lazyLines.RemoveLast();
		}
		if (flag)
		{
			while (_lazyLines.Count > 0 && _lazyLines.First.Value.Depth < depth)
			{
				PrintAndRemove(_lazyLines.First, level);
			}
			while (_lazyLines.Count > 0 && _lazyLines.First.Value.Depth == depth)
			{
				_lazyLines.RemoveFirst();
			}
			if (_lazyLines.Count == 0)
			{
				lastDepth = -1;
			}
			Print(lazyLine, depth, level);
		}
		else
		{
			_lazyLines.AddLast(new LazyLine(lazyLine, depth));
		}
		lastDepth = ((_lazyLines.Count == 0) ? (-1) : depth);
	}

	public void Dispose()
	{
		_lazyLines.Clear();
	}

	private void PrintAndRemove(LinkedListNode<LazyLine> node, LogLevel level)
	{
		Print(node.Value.Line, node.Value.Depth, level);
		_lazyLines.Remove(node);
	}

	private void Print(Func<string> lazyLine, int depth, LogLevel level)
	{
		string message = StringUtils.GetPadding(depth) + lazyLine();
		_aiLogger?.Log(level, message);
		_systemLogger?.Log(level, message);
	}
}
}
