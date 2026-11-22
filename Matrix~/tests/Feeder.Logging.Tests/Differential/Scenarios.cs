#nullable enable
#pragma warning disable CS0105 // the mirrored copy imports the same namespace twice
#pragma warning disable CA2017, CA2254 // templates are mismatched or dynamic on purpose
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Differential.Real;

// compiled twice, against the reference package and against Feeder.Logging; every scenario returns a transcript and the two must be identical
public static class Scenarios
{
    static readonly Dictionary<string, Func<string>> Table = new();

    static readonly char[] ExhaustiveAlphabet = { '{', '}', 'a', ':', ',', '0', ' ' };

    static readonly LogLevel[] AllLevels = { LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Critical, LogLevel.None };

    static readonly (string Name, string? Template, object?[]? Args)[] Formats =
    {
        ("plain-text", "plain text", Array.Empty<object?>()),
        ("escapes-are-kept-without-arguments", "{{kept}} {Name} }", Array.Empty<object?>()),
        ("null-argument-array", "value {Name}", null),
        ("null-template", null, new object?[] { 1 }),
        ("null-template-no-arguments", null, Array.Empty<object?>()),
        ("one-named-hole", "{Name} logged in", new object?[] { "bob" }),
        ("positional-holes", "{0} then {1}", new object?[] { "a", "b" }),
        ("positional-holes-reversed-names", "{1} then {0}", new object?[] { "a", "b" }),
        ("two-named-holes", "{First} and {Second}", new object?[] { 1, 2 }),
        ("repeated-name-takes-the-next-argument", "{Same} {Same}", new object?[] { 1, 2 }),
        ("spec-fixed-point", "{Value:F1}", new object?[] { 3.14159 }),
        ("spec-fixed-point-two-values", "{A:F1} / {B:F3}", new object?[] { 2.55, 1.0 }),
        ("spec-number-group", "{Value:N0}", new object?[] { 1234567 }),
        ("spec-hex", "{Value:X4}", new object?[] { 255 }),
        ("spec-percent", "{Value:P1}", new object?[] { 0.256 }),
        ("spec-date", "{When:yyyy-MM-dd HH:mm}", new object?[] { new DateTime(2024, 1, 31, 13, 5, 9, DateTimeKind.Utc) }),
        ("spec-on-null", "{Value:F1}", new object?[] { null }),
        ("alignment-right", "[{Value,6}]", new object?[] { 42 }),
        ("alignment-left", "[{Value,-6}]", new object?[] { 42 }),
        ("alignment-and-spec", "[{Value,8:F2}]", new object?[] { 3.14159 }),
        ("escaped-braces-around-a-hole", "{{{Name}}}", new object?[] { "x" }),
        ("escaped-braces-only-with-arguments", "{{Name}} {{literal}}", new object?[] { "x" }),
        ("escaped-open-brace-then-hole", "{{ {Name}", new object?[] { "x" }),
        ("hole-then-escaped-close", "{Name}}}", new object?[] { "x" }),
        ("lone-close-brace-is-an-error", "a } b {Name}", new object?[] { "x" }),
        ("unterminated-hole", "{Name", new object?[] { "x" }),
        ("empty-hole", "{} and {}", new object?[] { 1, 2 }),
        ("hole-with-spaces", "{ Name } here", new object?[] { "x" }),
        ("adjacent-holes", "{A}{B}{C}", new object?[] { 1, 2, 3 }),
        ("hole-with-nested-braces-in-spec", "{A:{B}}", new object?[] { 1, 2 }),
        ("name-with-dots-and-at", "{@user.Name} {$x}", new object?[] { "a", "b" }),
        ("missing-arguments", "{First} {Second} {Third}", new object?[] { 1 }),
        ("no-arguments-for-a-hole", "{Name}", Array.Empty<object?>()),
        ("extra-arguments", "{Only}", new object?[] { 1, 2, 3 }),
        ("arguments-without-any-hole", "no hole here", new object?[] { 1, 2 }),
        ("escape-without-hole-but-with-arguments", "{{}} ok", new object?[] { 1 }),
        ("null-argument", "value {A}", new object?[] { null }),
        ("null-and-text-arguments", "{A} {B} {C}", new object?[] { null, "x", null }),
        ("string-argument-is-not-enumerated", "{A}", new object?[] { "abc" }),
        ("list-argument", "{Items}", new object?[] { new List<int> { 1, 2, 3 } }),
        ("array-argument-with-null-item", "{Items}", new object?[] { new object?[] { 1, null, "a" } }),
        ("empty-list-argument", "{Items}", new object?[] { new List<int>() }),
        ("char-array-argument", "{Chars}", new object?[] { new[] { 'a', 'b' } }),
        ("dictionary-argument", "{Map}", new object?[] { new Dictionary<string, int> { ["k"] = 1 } }),
        ("enum-argument", "{Level}", new object?[] { LogLevel.Warning }),
        ("enum-argument-with-spec", "{Level:D}", new object?[] { LogLevel.Warning }),
        ("bool-and-char-arguments", "{A} {B}", new object?[] { true, 'c' }),
        ("decimal-and-double-arguments", "{A} {B}", new object?[] { 1.50m, 0.1 }),
        ("guid-argument", "{Id:N}", new object?[] { new Guid("0f8fad5b-d9cb-469f-a165-70867728950e") }),
        ("timespan-argument", "{Span}", new object?[] { TimeSpan.FromMilliseconds(1500) }),
        ("formattable-argument", "{Thing:custom}", new object?[] { new Formattable() }),
        ("exception-argument", "{Error}", new object?[] { new InvalidOperationException("bad") }),
        ("unicode-and-percent", "caf\u00e9 100% {A} \u2713", new object?[] { 1 }),
        ("multi-line-template", "line one {A}\nline two {B}\r\n", new object?[] { 1, 2 }),
        ("very-long-argument", "{A}", new object?[] { new string('x', 5000) }),
    };

    sealed class Formattable : IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider) => "<" + format + ">";
    }

    public static class Types
    {
        public class Plain { }
        public interface IThing { }
        public class Outer
        {
            public class Inner { }
            public class InnerGeneric<T> { }
        }
        public class Generic<T> { }
        public class Generic2<TA, TB> { }
        public class GenericOuter<T>
        {
            public class Nested { }
        }
    }

    static Scenarios()
    {
        foreach (var format in Formats)
        {
            var captured = format;
            Table["format/" + captured.Name] = () => Format(captured.Template, captured.Args);
        }
        Table["format/current-culture-does-not-leak-into-numbers"] = CultureDoesNotLeak;
        Table["format/same-template-twice-uses-the-cache"] = SameTemplateTwice;
        for (var i = 0; i < ExhaustiveAlphabet.Length; i++)
        {
            var first = ExhaustiveAlphabet[i];
            Table["format/exhaustive-templates-starting-with-" + i] = () => ExhaustiveTemplates(first);
        }
        Table["format/seeded-random-templates"] = RandomTemplates;
        Table["format/state-is-not-null-and-formatter-gets-the-exception"] = FormatterGetsTheException;

        Table["extensions/every-overload-of-every-level"] = EveryOverload;
        Table["extensions/named-arguments-as-the-package-writes-them"] = NamedArguments;
        Table["extensions/null-logger-argument"] = NullLoggerArgument;
        Table["extensions/null-factory-and-type-arguments"] = NullFactoryArguments;

        Table["levels/default-of-Create-is-Information"] = () => MinimumLevel(null);
        foreach (var level in AllLevels)
        {
            var captured = level;
            Table["levels/Create-with-minimum-" + captured] = () => MinimumLevel(captured);
        }
        Table["levels/last-SetMinimumLevel-wins"] = LastMinimumWins;
        Table["levels/instance-factory-starts-at-Trace"] = InstanceFactoryLevel;

        Table["providers/single-provider-receives-everything-at-Trace"] = SingleProvider;
        Table["providers/two-providers-in-registration-order"] = TwoProviders;
        Table["providers/two-instances-of-the-same-type-both-receive"] = TwoSameType;
        Table["providers/ClearProviders-drops-earlier-ones"] = ClearProviders;
        Table["providers/ClearProviders-then-add"] = ClearThenAdd;
        Table["providers/provider-logger-is-created-once-per-category"] = CreatedOncePerCategory;
        Table["providers/provider-added-later-reaches-existing-loggers"] = LateProvider;
        Table["providers/provider-added-before-the-logger-is-created"] = EarlyProvider;
        Table["providers/no-provider-means-nothing-happens"] = NoProvider;

        Table["errors/provider-throws-on-Log"] = ProviderThrowsOnLog;
        Table["errors/every-provider-throws-on-Log"] = EveryProviderThrowsOnLog;
        Table["errors/provider-throws-on-IsEnabled-before-an-enabled-one"] = ThrowsBeforeEnabled;
        Table["errors/provider-throws-on-IsEnabled-after-an-enabled-one"] = ThrowsAfterEnabled;
        Table["errors/IsEnabled-needs-only-one-enabled-provider"] = IsEnabledNeedsOne;
        Table["errors/formatter-failure-is-wrapped"] = FormatterFailureIsWrapped;

        Table["scope/no-provider"] = ScopeWithoutProvider;
        Table["scope/one-provider-returns-its-own-scope"] = ScopeOneProvider;
        Table["scope/one-provider-returning-null"] = ScopeNullFromProvider;
        Table["scope/two-providers-are-disposed-together"] = ScopeTwoProviders;
        Table["scope/provider-failing-to-begin-a-scope"] = ScopeProviderThrows;

        Table["dispose/factory-from-Create-leaves-providers-alone"] = DisposeCreated;
        Table["dispose/instance-factory-disposes-providers-it-was-given"] = DisposeInstance;
        Table["dispose/failing-provider-dispose-is-swallowed"] = DisposeFailingProvider;
        Table["dispose/loggers-keep-working-after-the-factory-is-gone"] = LoggersAfterDispose;
        Table["dispose/CreateLogger-and-AddProvider-throw-afterwards"] = UseAfterDispose;

        Table["categories/generic-and-nested-types"] = Categories;
        Table["categories/loggers-are-cached-by-exact-name"] = CachedByName;
        Table["categories/typed-logger-forwards-everything"] = TypedLoggerForwards;
        Table["categories/typed-logger-requires-a-factory"] = TypedLoggerNullFactory;

        Table["null-objects/NullLogger-and-friends"] = NullObjects;
        Table["event-id/equality-hash-and-text"] = EventIds;
        Table["log-level/numeric-values"] = LogLevels;
    }

    public static IEnumerable<string> Names => Table.Keys;

    public static string LibraryNamespace => typeof(ILoggerFactory).Namespace!;

    public static string Run(string name) => Table[name]();

    sealed class Transcript
    {
        readonly StringBuilder text = new();

        public void Line(string line) => text.Append(line).Append('\n');

        public override string ToString() => text.ToString();
    }

    sealed class Recorder : ILoggerProvider
    {
        readonly Transcript transcript;

        public readonly string Name;
        public Func<LogLevel, bool> Enabled = _ => true;
        public bool ThrowOnLog;
        public bool ThrowOnIsEnabled;
        public bool ThrowOnDispose;
        public bool ThrowOnScope;
        public Func<IDisposable?>? Scope;

        public Recorder(string name, Transcript transcript)
        {
            Name = name;
            this.transcript = transcript;
        }

        public ILogger CreateLogger(string categoryName)
        {
            transcript.Line($"{Name}.CreateLogger({categoryName})");
            return new RecorderLogger(this, categoryName);
        }

        public void Dispose()
        {
            transcript.Line($"{Name}.Dispose");
            if (ThrowOnDispose)
                throw new InvalidOperationException(Name + " dispose failed");
        }

        sealed class RecorderLogger : ILogger
        {
            readonly Recorder owner;
            readonly string category;

            public RecorderLogger(Recorder owner, string category)
            {
                this.owner = owner;
                this.category = category;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                owner.transcript.Line($"{owner.Name}.BeginScope({state})");
                if (owner.ThrowOnScope)
                    throw new InvalidOperationException(owner.Name + " scope failed");
                return owner.Scope?.Invoke();
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                owner.transcript.Line($"{owner.Name}.IsEnabled({logLevel})");
                if (owner.ThrowOnIsEnabled)
                    throw new InvalidOperationException(owner.Name + " IsEnabled failed");
                return owner.Enabled(logLevel);
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (owner.ThrowOnLog)
                    throw new InvalidOperationException(owner.Name + " Log failed");
                var error = exception == null ? "-" : exception.GetType().Name + ":" + exception.Message;
                owner.transcript.Line($"{owner.Name} {category} {logLevel} {eventId.Id}/{eventId.Name} [{formatter(state, exception)}] {error}");
            }
        }
    }

    sealed class Scope : IDisposable
    {
        readonly Transcript transcript;
        readonly string name;

        public Scope(string name, Transcript transcript)
        {
            this.name = name;
            this.transcript = transcript;
        }

        public void Dispose() => transcript.Line(name + ".Dispose");
    }

    static string Unwrap(Exception e)
        => e is AggregateException aggregate && aggregate.InnerExceptions.Count == 1 ? Unwrap(aggregate.InnerExceptions[0]) : e.GetType().Name;

    static string Describe(Exception e)
        => e is AggregateException aggregate
            ? "AggregateException(" + e.Message + ")[" + string.Join(",", aggregate.InnerExceptions.Select(Describe)) + "]"
            : e.GetType().Name + (e is ArgumentException argument && argument.ParamName != null ? "(" + argument.ParamName + ")" : "");

    static string Outcome(Action action)
    {
        try
        {
            action();
            return "ok";
        }
        catch (Exception e)
        {
            return "threw " + Describe(e);
        }
    }

    static ILoggerFactory Create(LogLevel level, params Recorder[] providers)
        => LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(level);
            foreach (var provider in providers)
                builder.AddProvider(provider);
        });

    static string Format(string? template, object?[]? args)
    {
        var t = new Transcript();
        var logger = Create(LogLevel.Trace, new Recorder("p", t)).CreateLogger("format");
        try
        {
            logger.LogInformation(template, args!);
        }
        catch (Exception e)
        {
            // the reference package may reject a malformed template when the call is made or when the provider formats it
            t.Line("threw " + Unwrap(e));
        }
        return t.ToString();
    }

    static string CultureDoesNotLeak()
    {
        var t = new Transcript();
        var logger = Create(LogLevel.Trace, new Recorder("p", t)).CreateLogger("culture");
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NumberGroupSeparator = ".";
        culture.NumberFormat.PercentSymbol = "pct";
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            logger.LogInformation("{A:F1} {B:N0} {C:P0} {D}", 1234.5, 1234567, 0.5, 1.25m);
            t.Line("current culture still applies elsewhere: " + 1.5.ToString(CultureInfo.CurrentCulture));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
        return t.ToString();
    }

    static string SameTemplateTwice()
    {
        var t = new Transcript();
        var logger = Create(LogLevel.Trace, new Recorder("p", t)).CreateLogger("cache");
        logger.LogInformation("{A} {B}", 1, 2);
        logger.LogInformation("{A} {B}", "x", "y");
        logger.LogInformation("{A} {B}", 1, 2);
        return t.ToString();
    }

    static string FormatterGetsTheException()
    {
        var t = new Transcript();
        var logger = Create(LogLevel.Trace, new Recorder("p", t)).CreateLogger("exception");
        logger.LogError(new InvalidOperationException("boom"), "failed {Step}", "load");
        logger.LogError((Exception?)null, "failed {Step}", "save");
        return t.ToString();
    }

    sealed class Capture : ILogger
    {
        public string? Last;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Last = formatter(state, exception);
    }

    static readonly object?[][] ArgumentSets =
    {
        Array.Empty<object?>(),
        new object?[] { "A" },
        new object?[] { "A", "B" },
    };

    static string Render(Capture capture, string? template, object?[] args)
    {
        capture.Last = null;
        try
        {
            capture.LogInformation(template, args);
            return capture.Last ?? "<not logged>";
        }
        catch (Exception e)
        {
            return "threw " + Unwrap(e);
        }
    }

    // every template of up to six characters over braces, colon, comma, digit, letter and space
    static string ExhaustiveTemplates(char first)
    {
        var t = new Transcript();
        var capture = new Capture();
        for (var length = 1; length <= 6; length++)
        {
            var count = (int)Math.Pow(ExhaustiveAlphabet.Length, length - 1);
            for (var n = 0; n < count; n++)
            {
                var chars = new char[length];
                chars[0] = first;
                var rest = n;
                for (var i = 1; i < length; i++)
                {
                    chars[i] = ExhaustiveAlphabet[rest % ExhaustiveAlphabet.Length];
                    rest /= ExhaustiveAlphabet.Length;
                }
                var template = new string(chars);
                foreach (var args in ArgumentSets)
                    t.Line($"[{template}] {args.Length}: {Render(capture, template, args)}");
            }
        }
        return t.ToString();
    }

    static string RandomTemplates()
    {
        var pieces = new[] { "{Name}", "{0}", "{1:F2}", "{A,6}", "{B,-4:N0}", "{{", "}}", "{", "}", ":", ",", " text ", "x", "{{{Q}}}", "{}", "{ }", "%", "\u00e9" };
        var arguments = new object?[] { 1, 2.5, "s", null, new[] { 1, 2 }, new List<string?> { "a", null }, LogLevel.Error, 'c', 10m, new DateTime(2020, 2, 29, 1, 2, 3, DateTimeKind.Utc) };
        var random = new Random(20240607);
        var t = new Transcript();
        var capture = new Capture();
        for (var n = 0; n < 20000; n++)
        {
            var template = string.Concat(Enumerable.Range(0, random.Next(0, 8)).Select(_ => pieces[random.Next(pieces.Length)]));
            var args = Enumerable.Range(0, random.Next(0, 5)).Select(_ => arguments[random.Next(arguments.Length)]).ToArray();
            t.Line($"[{template}] {args.Length}: {Render(capture, template, args)}");
        }
        return t.ToString();
    }

    static string EveryOverload()
    {
        var t = new Transcript();
        var logger = Create(LogLevel.Trace, new Recorder("p", t)).CreateLogger("extensions");
        var error = new InvalidOperationException("boom");
        var id = new EventId(42, "answer");

        logger.LogTrace("m {A}", 1);
        logger.LogTrace(error, "m {A}", 1);
        logger.LogTrace(id, "m {A}", 1);
        logger.LogTrace(id, error, "m {A}", 1);

        logger.LogDebug("m {A}", 1);
        logger.LogDebug(error, "m {A}", 1);
        logger.LogDebug(id, "m {A}", 1);
        logger.LogDebug(id, error, "m {A}", 1);

        logger.LogInformation("m {A}", 1);
        logger.LogInformation(error, "m {A}", 1);
        logger.LogInformation(id, "m {A}", 1);
        logger.LogInformation(id, error, "m {A}", 1);

        logger.LogWarning("m {A}", 1);
        logger.LogWarning(error, "m {A}", 1);
        logger.LogWarning(id, "m {A}", 1);
        logger.LogWarning(id, error, "m {A}", 1);

        logger.LogError("m {A}", 1);
        logger.LogError(error, "m {A}", 1);
        logger.LogError(id, "m {A}", 1);
        logger.LogError(id, error, "m {A}", 1);

        logger.LogCritical("m {A}", 1);
        logger.LogCritical(error, "m {A}", 1);
        logger.LogCritical(id, "m {A}", 1);
        logger.LogCritical(id, error, "m {A}", 1);

        logger.Log(LogLevel.Warning, "m {A}", 1);
        logger.Log(LogLevel.Warning, id, "m {A}", 1);
        logger.Log(LogLevel.Warning, error, "m {A}", 1);
        logger.Log(LogLevel.Warning, id, error, "m {A}", 1);
        logger.Log(LogLevel.None, "none {A}", 1);

        logger.LogInformation("no arguments at all");
        logger.LogInformation("only message and null exception", (object?[]?)null!);
        logger.LogError((Exception?)null, null, 1, 2);
        logger.LogError(7, "int converts to EventId {A}", 3);
        logger.LogError(error, "{A} {B}", "first", "second");
        return t.ToString();
    }

    static string NamedArguments()
    {
        var t = new Transcript();
        var logger = Create(LogLevel.Trace, new Recorder("p", t)).CreateLogger("named");
        var error = new InvalidOperationException("fail");
        logger.LogError(eventId: default, message: error.Message, exception: error);
        logger.LogWarning(exception: error, message: "named {A}", args: new object?[] { 5 });
        logger.LogInformation(message: "only message");
        logger.Log(logLevel: LogLevel.Debug, eventId: 3, exception: null, message: "log {A}", args: new object?[] { 9 });
        return t.ToString();
    }

    static string NullLoggerArgument()
    {
        var t = new Transcript();
        ILogger? logger = null;
        t.Line(Outcome(() => logger!.LogInformation("x")));
        t.Line(Outcome(() => logger!.Log(LogLevel.Error, "x")));
        t.Line(Outcome(() => logger!.LogError(new Exception("e"), "x")));
        return t.ToString();
    }

    static string NullFactoryArguments()
    {
        var t = new Transcript();
        ILoggerFactory? factory = null;
        t.Line(Outcome(() => factory!.CreateLogger<Types.Plain>()));
        t.Line(Outcome(() => factory!.CreateLogger(typeof(Types.Plain))));
        t.Line(Outcome(() => Create(LogLevel.Trace).CreateLogger(null!)));
        t.Line(Outcome(() => Create(LogLevel.Trace).CreateLogger((Type)null!)));
        return t.ToString();
    }

    static void LogEveryLevel(Transcript t, ILogger logger)
    {
        foreach (var level in AllLevels)
        {
            t.Line($"IsEnabled({level}) = {logger.IsEnabled(level)}");
            logger.Log(level, "message at {Level}", level);
        }
    }

    static string MinimumLevel(LogLevel? level)
    {
        var t = new Transcript();
        var provider = new Recorder("p", t);
        var factory = LoggerFactory.Create(builder =>
        {
            if (level != null)
                builder.SetMinimumLevel(level.Value);
            builder.AddProvider(provider);
        });
        LogEveryLevel(t, factory.CreateLogger("levels"));
        return t.ToString();
    }

    static string LastMinimumWins()
    {
        var t = new Transcript();
        var provider = new Recorder("p", t);
        var factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Error);
            builder.AddProvider(provider);
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        LogEveryLevel(t, factory.CreateLogger("levels"));
        return t.ToString();
    }

    static string InstanceFactoryLevel()
    {
        var t = new Transcript();
        var factory = new LoggerFactory();
        factory.AddProvider(new Recorder("p", t));
        LogEveryLevel(t, factory.CreateLogger("levels"));
        return t.ToString();
    }

    static string SingleProvider()
    {
        var t = new Transcript();
        var logger = Create(LogLevel.Trace, new Recorder("p", t)).CreateLogger("single");
        logger.LogTrace("trace");
        logger.LogCritical(new InvalidOperationException("x"), "critical {A}", 1);
        return t.ToString();
    }

    static string TwoProviders()
    {
        var t = new Transcript();
        var logger = Create(LogLevel.Trace, new Recorder("p1", t), new Recorder("p2", t)).CreateLogger("two");
        logger.LogInformation("hello {A}", 1);
        t.Line("IsEnabled = " + logger.IsEnabled(LogLevel.Debug));
        return t.ToString();
    }

    static string TwoSameType()
    {
        var t = new Transcript();
        var factory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new Recorder("first", t));
            builder.AddProvider(new Recorder("second", t));
            builder.SetMinimumLevel(LogLevel.Trace);
        });
        factory.CreateLogger("same").LogInformation("hello");
        return t.ToString();
    }

    static string ClearProviders()
    {
        var t = new Transcript();
        var factory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new Recorder("dropped", t));
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Trace);
        });
        factory.CreateLogger("cleared").LogInformation("hello");
        t.Line("IsEnabled = " + factory.CreateLogger("cleared").IsEnabled(LogLevel.Error));
        return t.ToString();
    }

    static string ClearThenAdd()
    {
        var t = new Transcript();
        var factory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new Recorder("dropped", t));
            builder.ClearProviders();
            builder.AddProvider(new Recorder("kept", t));
            builder.SetMinimumLevel(LogLevel.Trace);
        });
        factory.CreateLogger("cleared").LogInformation("hello");
        return t.ToString();
    }

    static string CreatedOncePerCategory()
    {
        var t = new Transcript();
        var factory = Create(LogLevel.Trace, new Recorder("p", t));
        factory.CreateLogger("a").LogInformation("one");
        factory.CreateLogger("a").LogInformation("two");
        factory.CreateLogger("b").LogInformation("three");
        return t.ToString();
    }

    static string LateProvider()
    {
        var t = new Transcript();
        var factory = Create(LogLevel.Trace, new Recorder("early", t));
        var first = factory.CreateLogger("first");
        var second = factory.CreateLogger("second");
        first.LogInformation("before");
        factory.AddProvider(new Recorder("late", t));
        first.LogInformation("after");
        second.LogInformation("after");
        factory.CreateLogger("third").LogInformation("new category");
        return t.ToString();
    }

    static string EarlyProvider()
    {
        var t = new Transcript();
        var factory = new LoggerFactory();
        factory.AddProvider(new Recorder("p1", t));
        factory.AddProvider(new Recorder("p2", t));
        factory.CreateLogger("c").LogInformation("hello");
        return t.ToString();
    }

    static string NoProvider()
    {
        var t = new Transcript();
        var logger = Create(LogLevel.Trace).CreateLogger("none");
        logger.LogError("nobody listens");
        t.Line("IsEnabled = " + logger.IsEnabled(LogLevel.Critical));
        return t.ToString();
    }

    static string ProviderThrowsOnLog()
    {
        var t = new Transcript();
        var logger = Create(LogLevel.Trace, new Recorder("bad", t) { ThrowOnLog = true }, new Recorder("good", t)).CreateLogger("errors");
        t.Line(Outcome(() => logger.LogInformation("hello {A}", 1)));
        return t.ToString();
    }

    static string EveryProviderThrowsOnLog()
    {
        var t = new Transcript();
        var logger = Create(LogLevel.Trace, new Recorder("bad1", t) { ThrowOnLog = true }, new Recorder("good", t), new Recorder("bad2", t) { ThrowOnLog = true }).CreateLogger("errors");
        t.Line(Outcome(() => logger.LogInformation("hello")));
        return t.ToString();
    }

    static string ThrowsBeforeEnabled()
    {
        var t = new Transcript();
        var logger = Create(LogLevel.Trace, new Recorder("bad", t) { ThrowOnIsEnabled = true }, new Recorder("good", t)).CreateLogger("errors");
        t.Line(Outcome(() => t.Line("IsEnabled = " + logger.IsEnabled(LogLevel.Error))));
        return t.ToString();
    }

    static string ThrowsAfterEnabled()
    {
        var t = new Transcript();
        var logger = Create(LogLevel.Trace, new Recorder("good", t), new Recorder("bad", t) { ThrowOnIsEnabled = true }).CreateLogger("errors");
        t.Line(Outcome(() => t.Line("IsEnabled = " + logger.IsEnabled(LogLevel.Error))));
        return t.ToString();
    }

    static string IsEnabledNeedsOne()
    {
        var t = new Transcript();
        var off = new Recorder("off", t) { Enabled = _ => false };
        var warnings = new Recorder("warnings", t) { Enabled = level => level >= LogLevel.Warning };
        var logger = Create(LogLevel.Trace, off, warnings).CreateLogger("enabled");
        foreach (var level in new[] { LogLevel.Information, LogLevel.Warning })
            t.Line($"IsEnabled({level}) = {logger.IsEnabled(level)}");
        return t.ToString();
    }

    static string FormatterFailureIsWrapped()
    {
        var t = new Transcript();
        var logger = Create(LogLevel.Trace, new Recorder("p", t)).CreateLogger("errors");
        t.Line(Outcome(() => logger.LogInformation("{A} {B}", 1)));
        t.Line(Outcome(() => logger.LogInformation("fine {A}", 1)));
        return t.ToString();
    }

    static string ScopeWithoutProvider()
    {
        var t = new Transcript();
        var logger = Create(LogLevel.Trace).CreateLogger("scope");
        var scope = logger.BeginScope("s");
        t.Line("scope is null = " + (scope == null));
        t.Line(Outcome(() => scope?.Dispose()));
        return t.ToString();
    }

    static string ScopeOneProvider()
    {
        var t = new Transcript();
        var own = new Scope("own", t);
        var logger = Create(LogLevel.Trace, new Recorder("p", t) { Scope = () => own }).CreateLogger("scope");
        var scope = logger.BeginScope("s");
        t.Line("same instance = " + ReferenceEquals(scope, own));
        scope!.Dispose();
        return t.ToString();
    }

    static string ScopeNullFromProvider()
    {
        var t = new Transcript();
        var logger = Create(LogLevel.Trace, new Recorder("p", t)).CreateLogger("scope");
        t.Line("scope is null = " + (logger.BeginScope("s") == null));
        return t.ToString();
    }

    static string ScopeTwoProviders()
    {
        var t = new Transcript();
        var logger = Create(LogLevel.Trace,
            new Recorder("p1", t) { Scope = () => new Scope("scope1", t) },
            new Recorder("p2", t) { Scope = () => new Scope("scope2", t) },
            new Recorder("p3", t)).CreateLogger("scope");
        var scope = logger.BeginScope("s");
        t.Line("scope is null = " + (scope == null));
        scope!.Dispose();
        scope.Dispose();
        return t.ToString();
    }

    static string ScopeProviderThrows()
    {
        var t = new Transcript();
        var logger = Create(LogLevel.Trace, new Recorder("bad", t) { ThrowOnScope = true }, new Recorder("good", t) { Scope = () => new Scope("scope", t) }).CreateLogger("scope");
        t.Line(Outcome(() => logger.BeginScope("s")));
        return t.ToString();
    }

    static string DisposeCreated()
    {
        var t = new Transcript();
        var factory = Create(LogLevel.Trace, new Recorder("p", t));
        factory.CreateLogger("c");
        factory.Dispose();
        factory.Dispose();
        t.Line("disposed twice");
        return t.ToString();
    }

    static string DisposeInstance()
    {
        var t = new Transcript();
        var factory = new LoggerFactory();
        factory.AddProvider(new Recorder("p1", t));
        factory.AddProvider(new Recorder("p2", t));
        factory.Dispose();
        factory.Dispose();
        t.Line("disposed twice");
        return t.ToString();
    }

    static string DisposeFailingProvider()
    {
        var t = new Transcript();
        var factory = new LoggerFactory();
        factory.AddProvider(new Recorder("p1", t));
        factory.AddProvider(new Recorder("bad", t) { ThrowOnDispose = true });
        factory.AddProvider(new Recorder("p3", t));
        t.Line(Outcome(factory.Dispose));
        return t.ToString();
    }

    static string LoggersAfterDispose()
    {
        var t = new Transcript();
        var factory = Create(LogLevel.Trace, new Recorder("p", t));
        var logger = factory.CreateLogger("c");
        factory.Dispose();
        logger.LogInformation("still here");
        return t.ToString();
    }

    static string UseAfterDispose()
    {
        var t = new Transcript();
        var factory = Create(LogLevel.Trace, new Recorder("p", t));
        factory.CreateLogger("cached");
        factory.Dispose();
        t.Line(Outcome(() => factory.CreateLogger("cached")));
        t.Line(Outcome(() => factory.CreateLogger("fresh")));
        t.Line(Outcome(() => factory.AddProvider(new Recorder("q", t))));

        var instance = new LoggerFactory();
        instance.Dispose();
        t.Line(Outcome(() => instance.CreateLogger("fresh")));
        t.Line(Outcome(() => instance.AddProvider(new Recorder("q", t))));
        t.Line(Outcome(() => new LoggerFactory().AddProvider(null!)));
        return t.ToString();
    }

    static string Normalize(string text) => text.Replace(typeof(Scenarios).Namespace!, "NS");

    static string Categories()
    {
        var t = new Transcript();
        var factory = Create(LogLevel.Trace, new Recorder("p", t));
        factory.CreateLogger<Types.Plain>();
        factory.CreateLogger<Types.IThing>();
        factory.CreateLogger<Types.Outer>();
        factory.CreateLogger<Types.Outer.Inner>();
        factory.CreateLogger<Types.Outer.InnerGeneric<int>>();
        factory.CreateLogger<Types.Generic<string>>();
        factory.CreateLogger<Types.Generic<Types.Plain>>();
        factory.CreateLogger<Types.Generic2<int, Types.Plain>>();
        factory.CreateLogger<Types.GenericOuter<int>.Nested>();
        factory.CreateLogger<Recorder>();
        factory.CreateLogger<IDisposable>();
        factory.CreateLogger<Exception>();
        t.Line("-- by Type");
        factory = Create(LogLevel.Trace, new Recorder("p", t));
        factory.CreateLogger(typeof(Types.Plain));
        factory.CreateLogger(typeof(Types.Outer.Inner));
        factory.CreateLogger(typeof(Types.Generic<>));
        factory.CreateLogger(typeof(Types.Generic<int>));
        factory.CreateLogger(typeof(Types.GenericOuter<>.Nested));
        factory.CreateLogger(typeof(Types.GenericOuter<string>.Nested));
        factory.CreateLogger(typeof(IDisposable));
        t.Line("-- through the typed wrapper");
        new Logger<Types.Outer.Inner>(Create(LogLevel.Trace, new Recorder("p", t))).LogInformation("hello");
        return Normalize(t.ToString());
    }

    static string CachedByName()
    {
        var t = new Transcript();
        var factory = Create(LogLevel.Trace, new Recorder("p", t));
        t.Line("same name same logger = " + ReferenceEquals(factory.CreateLogger("a"), factory.CreateLogger("a")));
        t.Line("other name other logger = " + !ReferenceEquals(factory.CreateLogger("a"), factory.CreateLogger("b")));
        t.Line("names are case sensitive = " + !ReferenceEquals(factory.CreateLogger("a"), factory.CreateLogger("A")));
        t.Line("empty name is allowed = " + (factory.CreateLogger("") != null));
        return t.ToString();
    }

    static string TypedLoggerForwards()
    {
        var t = new Transcript();
        var scope = new Scope("scope", t);
        var provider = new Recorder("p", t) { Scope = () => scope, Enabled = level => level >= LogLevel.Warning };
        var typed = Create(LogLevel.Trace, provider).CreateLogger<Types.Plain>();
        t.Line("info enabled = " + typed.IsEnabled(LogLevel.Information));
        t.Line("error enabled = " + typed.IsEnabled(LogLevel.Error));
        typed.LogError(new InvalidOperationException("e"), "typed {A}", 1);
        typed.BeginScope("s")!.Dispose();
        return Normalize(t.ToString());
    }

    static string TypedLoggerNullFactory()
    {
        var t = new Transcript();
        t.Line(Outcome(() => new Logger<Types.Plain>(null!)));
        return t.ToString();
    }

    static string NullObjects()
    {
        var t = new Transcript();
        t.Line("NullLogger.IsEnabled: " + string.Join(",", AllLevels.Select(level => NullLogger.Instance.IsEnabled(level))));
        t.Line("NullLogger<T>.IsEnabled: " + string.Join(",", AllLevels.Select(level => NullLogger<Types.Plain>.Instance.IsEnabled(level))));
        NullLogger.Instance.LogError(new InvalidOperationException("ignored"), "ignored {A}", 1);
        NullLogger<Types.Plain>.Instance.LogCritical("ignored");
        t.Line("scope is null = " + (NullLogger.Instance.BeginScope("x") == null));
        t.Line("typed scope is null = " + (NullLogger<Types.Plain>.Instance.BeginScope("x") == null));
        t.Line(Outcome(() => NullLogger.Instance.BeginScope("x").Dispose()));
        t.Line(Outcome(() => NullLogger<Types.Plain>.Instance.BeginScope("x").Dispose()));
        t.Line("same scope instance = " + ReferenceEquals(NullLogger.Instance.BeginScope("x"), NullLogger.Instance.BeginScope("y")));
        t.Line("instances are shared = " + ReferenceEquals(NullLogger.Instance, NullLogger.Instance) + " " + ReferenceEquals(NullLogger<Types.Plain>.Instance, NullLogger<Types.Plain>.Instance));

        var factory = NullLoggerFactory.Instance;
        t.Line("factory returns the NullLogger = " + ReferenceEquals(factory.CreateLogger("a"), NullLogger.Instance));
        factory.AddProvider(new Recorder("ignored", t));
        factory.Dispose();
        t.Line("factory ignores providers = " + ReferenceEquals(factory.CreateLogger("b"), NullLogger.Instance));
        t.Line("own factory instance = " + (new NullLoggerFactory() != factory));
        t.Line("typed logger = " + (factory.CreateLogger<Types.Plain>() != null));

        ILogger<Types.Plain> typed = NullLogger<Types.Plain>.Instance;
        ILogger untyped = typed;
        t.Line("typed is a logger = " + (untyped != null));
        return Normalize(t.ToString());
    }

    static string EventIds()
    {
        var t = new Transcript();
        var one = new EventId(1, "one");
        var uno = new EventId(1, "uno");
        EventId two = 2;
        EventId nothing = default;
        t.Line($"{one == uno} {one != uno} {one.Equals(uno)} {one.Equals((object)uno)} {one.Equals((object?)null)} {one.Equals("x")} {one.Equals((object)1)}");
        t.Line($"{one == two} {one != two} {nothing == 0} {nothing.Equals(new EventId(0, "named"))}");
        t.Line($"{one.GetHashCode()} {two.GetHashCode()} {nothing.GetHashCode()} {new EventId(-5).GetHashCode()}");
        t.Line($"[{one}] [{two}] [{nothing}] [{new EventId(7, "")}] [{new EventId(8, null)}]");
        t.Line($"{one.Id}/{one.Name} {two.Id}/{two.Name ?? "null"} {nothing.Id}/{nothing.Name ?? "null"}");
        t.Line("dictionary key = " + new Dictionary<EventId, string> { [one] = "a" }.ContainsKey(uno));
        return t.ToString();
    }

    static string LogLevels()
    {
        var t = new Transcript();
        t.Line(string.Join(",", Enum.GetValues<LogLevel>().Select(level => $"{level}={(int)level}")));
        t.Line(string.Join(",", Enum.GetNames<LogLevel>()));
        t.Line("underlying = " + Enum.GetUnderlyingType(typeof(LogLevel)).Name);
        return t.ToString();
    }
}
