namespace Feeder.Reflect.Model
{
public static class LogTypeExtensions
{
	public static Logs? Trace(this Logs? logs, string message, int depth = 0)
	{
		logs?.AddLast(new LogEntry
		{
			Message = message,
			Type = LogType.Trace,
			Depth = depth
		});
		return logs;
	}

	public static Logs? Debug(this Logs? logs, string message, int depth = 0)
	{
		logs?.AddLast(new LogEntry
		{
			Message = message,
			Type = LogType.Debug,
			Depth = depth
		});
		return logs;
	}

	public static Logs? Info(this Logs? logs, string message, int depth = 0)
	{
		logs?.AddLast(new LogEntry
		{
			Message = message,
			Type = LogType.Info,
			Depth = depth
		});
		return logs;
	}

	public static Logs? Success(this Logs? logs, string message, int depth = 0)
	{
		logs?.AddLast(new LogEntry
		{
			Message = message,
			Type = LogType.Success,
			Depth = depth
		});
		return logs;
	}

	public static Logs? Warning(this Logs? logs, string message, int depth = 0)
	{
		logs?.AddLast(new LogEntry
		{
			Message = message,
			Type = LogType.Warning,
			Depth = depth
		});
		return logs;
	}

	public static Logs? Error(this Logs? logs, string message, int depth = 0)
	{
		logs?.AddLast(new LogEntry
		{
			Message = message,
			Type = LogType.Error,
			Depth = depth
		});
		return logs;
	}

	public static Logs? Critical(this Logs? logs, string message, int depth = 0)
	{
		logs?.AddLast(new LogEntry
		{
			Message = message,
			Type = LogType.Critical,
			Depth = depth
		});
		return logs;
	}
}
}
