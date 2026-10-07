using System.Text;
using Feeder.Logging.Tests.PackageLoggers;

namespace Feeder.Logging.Tests;

public class PackageLoggersTests
{
    [Fact]
    public void String_builder_logger_receives_the_formatted_message_from_the_extension_methods()
    {
        var builder = new StringBuilder();
        ILogger logger = new StringBuilderLogger(builder, "Feeder.Reflect.Reflector");

        logger.Log(LogLevel.Information, "converted {Count} fields in {Seconds:F1}s", 3, 0.26);
        logger.LogError(new InvalidOperationException("boom"), "failed {Step}", "serialize");

        Assert.Equal(
            "[Reflector] Reflector info:  converted 3 fields in 0.3s" + Environment.NewLine +
            "[Reflector] Reflector fail:  failed serialize" + Environment.NewLine,
            builder.ToString());
    }

    [Fact]
    public void A_printer_style_call_with_braces_in_the_text_keeps_them()
    {
        var builder = new StringBuilder();
        ILogger logger = new StringBuilderLogger(builder);
        var message = "{ \"json\": true } and {{escaped}}";

        logger.Log(LogLevel.Debug, message);

        Assert.Equal("[Reflector] dbug:  " + message + Environment.NewLine, builder.ToString());
    }

    [Fact]
    public void String_builder_logger_rejects_the_None_level_as_before()
    {
        ILogger logger = new StringBuilderLogger();

        Assert.Throws<ArgumentOutOfRangeException>(() => logger.Log(LogLevel.None, "x"));
    }

    [Fact]
    public void Console_logger_writes_category_level_and_message()
    {
        var original = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            var factory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddProvider(new ConsoleLoggerProvider());
            });

            factory.CreateLogger("Feeder.Tool.Scene").LogWarning("loaded {Name}", "level1");
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal("[Reflector] Scene [Warning] loaded level1" + Environment.NewLine, captured.ToString());
    }

    [Fact]
    public void Forwarding_provider_relays_text_and_adds_the_hint_to_errors()
    {
        var inner = new StringBuilderLogger("inner");
        var factory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddProvider(new ForwardLoggerProvider(inner, "stop the connection to silence this"));
            builder.SetMinimumLevel(LogLevel.Trace);
        });
        var logger = factory.CreateLogger("hub");

        logger.LogInformation("connected to {Url}", "ws://localhost");
        logger.LogError(new InvalidOperationException("reset"), "lost {Url}", "ws://localhost");

        Assert.Equal(
            "[Reflector] inner info:  connected to ws://localhost" + Environment.NewLine +
            "[Reflector] inner fail:  lost ws://localhost" + Environment.NewLine +
            "[Reflector] inner fail:  stop the connection to silence this" + Environment.NewLine,
            inner.ToString());
    }

    [Fact]
    public void Forwarding_provider_requires_a_logger()
    {
        Assert.Throws<ArgumentNullException>(() => new ForwardLoggerProvider(null!));
    }

    [Fact]
    public void Unity_style_logger_filters_by_its_own_threshold_and_keeps_the_exception_first()
    {
        UnityLogger.Output.Clear();
        UnityLogger.Threshold = LogLevel.Warning;
        var logger = UnityLoggerFactory.LoggerFactory.CreateLogger<PackageLoggersTests>();

        logger.LogInformation("dropped {A}", 1);
        logger.LogWarning("kept {A}", 2);
        logger.LogError(new InvalidOperationException("bad"), "failed {A}", 3);

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Critical));
        Assert.Equal(
            new[]
            {
                "warning warn: [AI] PackageLoggersTests kept 2",
                "exception bad",
                "error fail: [AI] PackageLoggersTests failed 3",
            },
            UnityLogger.Output);
    }

    [Fact]
    public void Unity_style_factory_is_created_with_the_Trace_minimum_so_the_provider_decides()
    {
        UnityLogger.Output.Clear();
        UnityLogger.Threshold = LogLevel.Trace;

        UnityLoggerFactory.LoggerFactory.CreateLogger("Tool_Scene").LogTrace("trace {A}", 1);

        Assert.Equal(new[] { "log trce: [AI] Tool_Scene trace 1" }, UnityLogger.Output);
        UnityLogger.Threshold = LogLevel.Warning;
    }

    [Fact]
    public void The_helper_forms_of_the_runtime_logger_extension_work()
    {
        UnityLogger.Output.Clear();
        UnityLogger.Threshold = LogLevel.Trace;
        var factory = UnityLoggerFactory.LoggerFactory;
        var error = new InvalidOperationException("save failed");

        // the shapes used by Safe.cs and MatrixPlugin.Log.cs
        factory.CreateLogger("Safe").LogError(eventId: default, message: error.Message, exception: error);
        factory.CreateLogger(typeof(PackageLoggersTests).Name).LogCritical("fatal {Reason}", "disk");

        Assert.Equal(
            new[]
            {
                "exception save failed",
                "error fail: [AI] Safe save failed",
                "error crit: [AI] PackageLoggersTests fatal disk",
            },
            UnityLogger.Output);
        UnityLogger.Threshold = LogLevel.Warning;
    }

    [Fact]
    public async Task Many_threads_creating_loggers_and_adding_providers_keep_every_logger_wired()
    {
        var factory = new LoggerFactory();
        var seen = new List<string>();
        var provider = new RecordingProvider(seen);
        factory.AddProvider(provider);

        var work = Enumerable.Range(0, 8)
            .Select(worker => Task.Run(() =>
            {
                for (var i = 0; i < 200; i++)
                {
                    var logger = factory.CreateLogger("category-" + (i % 10));
                    logger.LogInformation("worker {Worker} step {Step}", worker, i);
                }
            }))
            .ToList();
        work.Add(Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
                factory.AddProvider(new RecordingProvider(new List<string>()));
        }));
        await Task.WhenAll(work).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(8 * 200, seen.Count);
        Assert.Equal(10, provider.Categories.Count);
    }

    sealed class RecordingProvider : ILoggerProvider
    {
        readonly List<string> messages;

        public readonly HashSet<string> Categories = new();

        public RecordingProvider(List<string> messages) => this.messages = messages;

        public ILogger CreateLogger(string categoryName)
        {
            lock (Categories)
                Categories.Add(categoryName);
            return new Sink(messages);
        }

        public void Dispose()
        {
        }

        sealed class Sink : ILogger
        {
            readonly List<string> messages;

            public Sink(List<string> messages) => this.messages = messages;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var text = formatter(state, exception);
                lock (messages)
                    messages.Add(text);
            }
        }
    }
}
