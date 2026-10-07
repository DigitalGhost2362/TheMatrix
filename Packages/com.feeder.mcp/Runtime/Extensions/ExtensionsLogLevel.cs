#nullable enable
using Feeder.Logging;

namespace Feeder.MCP.Runtime.Extensions
{
    public static class ExtensionsLogLevel
    {
        public static bool IsEnabled(this LogLevel logLevel, LogLevel targetLogLevel)
            => logLevel <= targetLogLevel;

        public static string ToString(this LogLevel logLevel) => logLevel switch
        {
            LogLevel.Trace => "Trace",
            LogLevel.Debug => "Debug",
            LogLevel.Information => "Information",
            LogLevel.Warning => "Warning",
            LogLevel.Error => "Error",
            LogLevel.Critical => "Critical",
            _ => "None"
        };
    }
}
