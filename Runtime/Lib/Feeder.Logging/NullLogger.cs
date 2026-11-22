#nullable enable
using System;

namespace Feeder.Logging
{
    public class NullLogger : ILogger
    {
        public static NullLogger Instance { get; } = new NullLogger();

        NullLogger()
        {
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }

    public class NullLogger<T> : ILogger<T>
    {
        public static readonly NullLogger<T> Instance = new NullLogger<T>();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }

    public class NullLoggerFactory : ILoggerFactory
    {
        public static readonly NullLoggerFactory Instance = new NullLoggerFactory();

        public ILogger CreateLogger(string name) => NullLogger.Instance;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }

    internal sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new NullScope();

        NullScope()
        {
        }

        public void Dispose()
        {
        }
    }
}
