#nullable enable
using Feeder.ToolHost;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_Console
    {
        public static class Error
        {
            public static string InvalidMaxEntries(int entriesCount)
                => $"Invalid maxEntries value '{entriesCount}'. Must be greater than 0.";

            public static string InvalidLogTypeFilter(string logType)
                => $"Invalid logType filter '{logType}'. Valid values: All, Error, Assert, Warning, Log, Exception.";
        }
    }
}
