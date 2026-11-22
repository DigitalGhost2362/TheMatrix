#nullable enable
using System;

namespace Feeder.Logging
{
    public interface ILogger
    {
        void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter);

        bool IsEnabled(LogLevel logLevel);

        IDisposable? BeginScope<TState>(TState state) where TState : notnull;
    }

    public interface ILogger<out TCategoryName> : ILogger
    {
    }

    public interface ILoggerProvider : IDisposable
    {
        ILogger CreateLogger(string categoryName);
    }

    public interface ILoggerFactory : IDisposable
    {
        ILogger CreateLogger(string categoryName);

        void AddProvider(ILoggerProvider provider);
    }

    public interface ILoggingBuilder
    {
        ILoggingBuilder ClearProviders();

        ILoggingBuilder AddProvider(ILoggerProvider provider);

        ILoggingBuilder SetMinimumLevel(LogLevel level);
    }
}
