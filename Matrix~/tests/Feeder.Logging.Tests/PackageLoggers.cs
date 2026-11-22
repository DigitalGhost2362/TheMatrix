#nullable enable
#pragma warning disable CS8603 // the package returns null from a non-nullable BeginScope on purpose
using System.Text;

namespace Feeder.Logging.Tests.PackageLoggers;

// the package's own logger classes with only the Unity calls replaced: they must compile against Feeder.Logging as they are

public class ConsoleLogger : ILogger
{
    private readonly string _categoryName;

    public ConsoleLogger(string categoryName)
    {
        _categoryName = (categoryName.Contains(".") ? categoryName.Substring(categoryName.LastIndexOf('.') + 1) : categoryName);
    }

    IDisposable ILogger.BeginScope<TState>(TState state)
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (formatter == null)
        {
            throw new ArgumentNullException("formatter");
        }
        if (state == null)
        {
            throw new ArgumentNullException("state");
        }
        Console.WriteLine(string.Format("{0} {1}{2}{3} {4}[{5}]{6} {7}", "[Reflector]", "", _categoryName, "", "", logLevel, "", formatter(state, exception)));
    }
}

public class ConsoleLoggerProvider : ILoggerProvider, IDisposable
{
    public void Dispose()
    {
    }

    ILogger ILoggerProvider.CreateLogger(string categoryName)
    {
        return new ConsoleLogger(categoryName);
    }
}

public class StringBuilderLogger : ILogger
{
    private readonly string? _categoryName;

    private readonly StringBuilder _stringBuilder;

    public StringBuilderLogger(string? categoryName = null)
        : this(new StringBuilder(), categoryName)
    {
    }

    public StringBuilderLogger(StringBuilder stringBuilder, string? categoryName = null)
    {
        _stringBuilder = stringBuilder;
        _categoryName = ((categoryName == null || !categoryName.Contains(".")) ? categoryName : categoryName?.Substring(categoryName.LastIndexOf('.') + 1));
    }

    IDisposable ILogger.BeginScope<TState>(TState state)
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (formatter == null)
        {
            throw new ArgumentNullException("formatter");
        }
        if (state == null)
        {
            throw new ArgumentNullException("state");
        }
        string text = logLevel switch
        {
            LogLevel.Trace => "trce: ",
            LogLevel.Debug => "dbug: ",
            LogLevel.Information => "info: ",
            LogLevel.Warning => "warn: ",
            LogLevel.Error => "fail: ",
            LogLevel.Critical => "crit: ",
            _ => throw new ArgumentOutOfRangeException("logLevel", logLevel, null),
        };
        string value = (string.IsNullOrEmpty(_categoryName) ? ("[Reflector] " + text + " " + formatter(state, exception)) : ("[Reflector] " + _categoryName + " " + text + " " + formatter(state, exception)));
        _stringBuilder.AppendLine(value);
    }

    public void Clear()
    {
        _stringBuilder.Clear();
    }

    public override string ToString()
    {
        return _stringBuilder.ToString();
    }
}

public class ForwardLoggerProvider : ILoggerProvider, IDisposable
{
    private class ForwardingLogger : ILogger
    {
        private readonly ILogger _logger;

        private readonly string _categoryName;

        private readonly string? _additionalErrorMessage;

        public ForwardingLogger(ILogger logger, string categoryName, string? additionalErrorMessage)
        {
            _logger = logger;
            _categoryName = categoryName;
            _additionalErrorMessage = additionalErrorMessage;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return _logger.BeginScope(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _logger.IsEnabled(logLevel);
        }

        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                string state2 = formatter(state, exception) ?? "";
                _logger.Log(logLevel, eventId, state2, exception, (object s, Exception? e) => s?.ToString() ?? string.Empty);
                if (logLevel >= LogLevel.Error)
                {
                    _logger.Log(logLevel, _additionalErrorMessage);
                }
            }
        }
    }

    private readonly ILogger _logger;

    private readonly string? _additionalErrorMessage;

    public ForwardLoggerProvider(ILogger logger, string? additionalErrorMessage = null)
    {
        _logger = logger ?? throw new ArgumentNullException("logger");
        _additionalErrorMessage = additionalErrorMessage;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new ForwardingLogger(_logger, categoryName, _additionalErrorMessage);
    }

    public void Dispose()
    {
    }
}

public class UnityLogger : ILogger
{
    readonly string _categoryName;

    public static readonly List<string> Output = new();

    public static LogLevel Threshold = LogLevel.Warning;

    public UnityLogger(string categoryName)
    {
        _categoryName = categoryName.Contains('.')
            ? categoryName.Substring(categoryName.LastIndexOf('.') + 1)
            : categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null!;

    public bool IsEnabled(LogLevel logLevel) => Threshold <= logLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        if (formatter == null) throw new ArgumentNullException(nameof(formatter));
        if (state == null) throw new ArgumentNullException(nameof(state));

        string logLevelShort = logLevel switch
        {
            LogLevel.Critical => "crit:",
            LogLevel.Error => "fail:",
            LogLevel.Warning => "warn:",
            LogLevel.Information => "info:",
            LogLevel.Debug => "dbug:",
            LogLevel.Trace => "trce:",
            _ => "none"
        };
        var message = $"{logLevelShort} [AI] {_categoryName} {formatter(state, exception)}";

        switch (logLevel)
        {
            case LogLevel.Critical:
            case LogLevel.Error:
                if (exception != null)
                    Output.Add("exception " + exception.Message);
                Output.Add("error " + message);
                break;

            case LogLevel.Warning:
                if (exception != null)
                    Output.Add("warning-exception " + exception.Message);
                Output.Add("warning " + message);
                break;

            default:
                if (exception != null)
                    Output.Add("log-exception " + exception.Message);
                Output.Add("log " + message);
                break;
        }
    }
}

public class UnityLoggerProvider : ILoggerProvider
{
    public void Dispose() { /* No resources to dispose of */ }
    ILogger ILoggerProvider.CreateLogger(string categoryName) => new UnityLogger(categoryName);
}

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
