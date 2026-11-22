using Feeder.Logging;

namespace Feeder.ToolHost
{
public static class ExtensionsLogLevel
{
	public static bool IsEnabled(this LogLevel logLevel, LogLevel targetLogLevel)
	{
		return logLevel <= targetLogLevel;
	}

	public static string ToString(this LogLevel logLevel)
	{
		return logLevel switch
		{
			LogLevel.Trace => "Trace", 
			LogLevel.Debug => "Debug", 
			LogLevel.Information => "Information", 
			LogLevel.Warning => "Warning", 
			LogLevel.Error => "Error", 
			LogLevel.Critical => "Critical", 
			_ => "None", 
		};
	}
}
}
