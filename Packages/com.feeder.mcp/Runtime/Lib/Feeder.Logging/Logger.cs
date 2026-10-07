#nullable enable
using System;

namespace Feeder.Logging
{
    public class Logger<T> : ILogger<T>
    {
        readonly ILogger logger;

        public Logger(ILoggerFactory factory)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));
            logger = factory.CreateLogger(CategoryName.Of(typeof(T)));
        }

        IDisposable? ILogger.BeginScope<TState>(TState state) => logger.BeginScope(state);

        bool ILogger.IsEnabled(LogLevel logLevel) => logger.IsEnabled(logLevel);

        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => logger.Log(logLevel, eventId, state, exception, formatter);
    }

    internal static class CategoryName
    {
        // categories read Namespace.Outer.Inner for any type: no generic arity and no '+'
        public static string Of(Type type)
        {
            var name = type.Name;
            var arity = name.IndexOf('`');
            if (arity > 0)
                name = name.Substring(0, arity);
            if (type.IsNested)
                return Of(type.DeclaringType!) + "." + name;
            return string.IsNullOrEmpty(type.Namespace) ? name : type.Namespace + "." + name;
        }
    }
}
