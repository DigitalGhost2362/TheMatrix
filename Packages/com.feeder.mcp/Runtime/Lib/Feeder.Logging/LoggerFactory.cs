#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace Feeder.Logging
{
    public class LoggerFactory : ILoggerFactory
    {
        readonly object gate = new object();
        readonly Dictionary<string, AggregateLogger> loggers = new Dictionary<string, AggregateLogger>(StringComparer.Ordinal);
        readonly List<(ILoggerProvider Provider, bool Owned)> providers = new List<(ILoggerProvider Provider, bool Owned)>();
        readonly LogLevel minimumLevel;
        bool disposed;

        public LoggerFactory() : this(LogLevel.Trace)
        {
        }

        LoggerFactory(LogLevel minimumLevel) => this.minimumLevel = minimumLevel;

        // the minimum level starts at Information; providers added here are not disposed with the factory
        public static ILoggerFactory Create(Action<ILoggingBuilder> configure)
        {
            var builder = new Builder();
            configure(builder);
            var factory = new LoggerFactory(builder.MinimumLevel);
            foreach (var provider in builder.Providers)
                factory.providers.Add((provider, false));
            return factory;
        }

        public ILogger CreateLogger(string categoryName)
        {
            lock (gate)
            {
                if (disposed)
                    throw new ObjectDisposedException(nameof(LoggerFactory));
                if (!loggers.TryGetValue(categoryName, out var logger))
                {
                    var providerLoggers = new List<ILogger>(providers.Count);
                    foreach (var entry in providers)
                        providerLoggers.Add(entry.Provider.CreateLogger(categoryName));
                    logger = new AggregateLogger(minimumLevel, providerLoggers.ToArray());
                    loggers[categoryName] = logger;
                }
                return logger;
            }
        }

        // loggers that already exist start writing to the new provider too
        public void AddProvider(ILoggerProvider provider)
        {
            lock (gate)
            {
                if (disposed)
                    throw new ObjectDisposedException(nameof(LoggerFactory));
                if (provider == null)
                    throw new ArgumentNullException(nameof(provider));
                providers.Add((provider, true));
                foreach (var pair in loggers)
                    pair.Value.Add(provider.CreateLogger(pair.Key));
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                    return;
                disposed = true;
                foreach (var entry in providers)
                {
                    try
                    {
                        if (entry.Owned)
                            entry.Provider.Dispose();
                    }
                    catch
                    {
                        // a provider that fails to dispose must not keep the others alive
                    }
                }
            }
        }

        sealed class Builder : ILoggingBuilder
        {
            public readonly List<ILoggerProvider> Providers = new List<ILoggerProvider>();
            public LogLevel MinimumLevel = LogLevel.Information;

            public ILoggingBuilder ClearProviders()
            {
                Providers.Clear();
                return this;
            }

            public ILoggingBuilder AddProvider(ILoggerProvider provider)
            {
                Providers.Add(provider);
                return this;
            }

            public ILoggingBuilder SetMinimumLevel(LogLevel level)
            {
                MinimumLevel = level;
                return this;
            }
        }
    }

    // one logger per category: fans a call out to the logger of every provider; a failing provider does not hide the others
    internal sealed class AggregateLogger : ILogger
    {
        readonly LogLevel minimumLevel;
        ILogger[] loggers;

        public AggregateLogger(LogLevel minimumLevel, ILogger[] loggers)
        {
            this.minimumLevel = minimumLevel;
            this.loggers = loggers;
        }

        public void Add(ILogger logger)
        {
            var grown = new ILogger[loggers.Length + 1];
            loggers.CopyTo(grown, 0);
            grown[loggers.Length] = logger;
            Volatile.Write(ref loggers, grown);
        }

        bool Passes(LogLevel logLevel) => minimumLevel <= LogLevel.Critical && logLevel >= minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!Passes(logLevel))
                return;
            List<Exception>? errors = null;
            foreach (var logger in Volatile.Read(ref loggers))
            {
                try
                {
                    logger.Log(logLevel, eventId, state, exception, formatter);
                }
                catch (Exception e)
                {
                    (errors ??= new List<Exception>()).Add(e);
                }
            }
            ThrowIfAny(errors);
        }

        // true as soon as one provider is enabled; a provider that throws before that is reported even so
        public bool IsEnabled(LogLevel logLevel)
        {
            if (!Passes(logLevel))
                return false;
            List<Exception>? errors = null;
            var enabled = false;
            foreach (var logger in Volatile.Read(ref loggers))
            {
                try
                {
                    enabled = logger.IsEnabled(logLevel);
                }
                catch (Exception e)
                {
                    (errors ??= new List<Exception>()).Add(e);
                }
                if (enabled)
                    break;
            }
            ThrowIfAny(errors);
            return enabled;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            var current = Volatile.Read(ref loggers);
            if (current.Length == 1)
                return current[0].BeginScope(state);
            var scopes = new IDisposable?[current.Length];
            List<Exception>? errors = null;
            for (var i = 0; i < current.Length; i++)
            {
                try
                {
                    scopes[i] = current[i].BeginScope(state);
                }
                catch (Exception e)
                {
                    (errors ??= new List<Exception>()).Add(e);
                }
            }
            ThrowIfAny(errors);
            return new Scope(scopes);
        }

        static void ThrowIfAny(List<Exception>? errors)
        {
            if (errors != null)
                throw new AggregateException("An error occurred while writing to logger(s).", errors);
        }

        sealed class Scope : IDisposable
        {
            IDisposable?[]? scopes;

            public Scope(IDisposable?[] scopes) => this.scopes = scopes;

            public void Dispose()
            {
                var disposing = Interlocked.Exchange(ref scopes, null);
                if (disposing == null)
                    return;
                foreach (var scope in disposing)
                    scope?.Dispose();
            }
        }
    }
}
