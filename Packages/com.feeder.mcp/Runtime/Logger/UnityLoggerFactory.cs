#nullable enable
using Feeder.Logging;

namespace Feeder.MCP.Utils
{
    public static class UnityLoggerFactory
    {
        private static ILoggerFactory? _loggerFactory;

        public static ILoggerFactory LoggerFactory
        {
            get
            {
                if (_loggerFactory == null)
                {
                    _loggerFactory = Feeder.Logging.LoggerFactory.Create(builder =>
                    {
                        builder.ClearProviders();
                        builder.AddProvider(new UnityLoggerProvider());
                        builder.SetMinimumLevel(Feeder.Logging.LogLevel.Trace);
                    });
                }
                return _loggerFactory;
            }
        }
    }
}
