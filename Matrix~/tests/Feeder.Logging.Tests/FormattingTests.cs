using System.Globalization;

namespace Feeder.Logging.Tests;

// absolute expectations, so a mistake shared by both sides of a differential comparison cannot hide
public class FormattingTests
{
    sealed class Capture : ILogger
    {
        public readonly List<string> Messages = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    static string Format(string? template, params object?[] args)
    {
        var capture = new Capture();
        capture.LogInformation(template, args);
        return Assert.Single(capture.Messages);
    }

    [Fact]
    public void Named_holes_take_the_arguments_in_order()
    {
        Assert.Equal("bob logged in from 10.0.0.1", Format("{User} logged in from {Address}", "bob", "10.0.0.1"));
    }

    [Fact]
    public void The_same_name_twice_still_consumes_two_arguments()
    {
        Assert.Equal("1 2", Format("{Same} {Same}", 1, 2));
    }

    [Fact]
    public void Format_specifiers_and_alignment_apply()
    {
        Assert.Equal("pi=3.1 [   42] [42   ] 00FF", Format("pi={Pi:F1} [{Right,5}] [{Left,-5}] {Hex:X4}", 3.14159, 42, 42, 255));
    }

    [Fact]
    public void Escaped_braces_become_single_braces_when_there_are_arguments()
    {
        Assert.Equal("{x} {literal}", Format("{{{Name}}} {{literal}}", "x"));
    }

    [Fact]
    public void Without_arguments_the_message_is_kept_exactly_as_written()
    {
        var json = "{ \"a\": {{1}} } {Name}";

        Assert.Equal(json, Format(json));
    }

    [Fact]
    public void Extra_arguments_are_ignored()
    {
        Assert.Equal("1", Format("{Only}", 1, 2, 3));
    }

    [Fact]
    public void Missing_arguments_are_a_format_error()
    {
        Assert.Throws<FormatException>(() => Format("{First} {Second}", 1));
    }

    [Fact]
    public void Null_and_collections_have_fixed_spellings()
    {
        Assert.Equal("(null) | 1, 2, 3 | 1, (null), a | abc", Format("{A} | {B} | {C} | {D}", null, new[] { 1, 2, 3 }, new object?[] { 1, null, "a" }, "abc"));
    }

    [Fact]
    public void A_null_message_prints_as_null_in_brackets()
    {
        Assert.Equal("[null]", Format(null, 1));
        Assert.Equal("[null]", Format(null));
    }

    [Fact]
    public void Numbers_do_not_follow_the_current_culture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            Assert.Equal("1234.5", Format("{V:F1}", 1234.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void The_caller_argument_array_is_not_modified()
    {
        var args = new object?[] { null, new[] { 1, 2 } };
        var capture = new Capture();

        capture.LogInformation("{A} {B}", args);

        Assert.Null(args[0]);
        Assert.IsType<int[]>(args[1]);
    }

    [Fact]
    public async Task Formatting_the_same_templates_from_many_threads_is_stable()
    {
        var work = Enumerable.Range(0, 8)
            .Select(worker => Task.Run(() =>
            {
                for (var i = 0; i < 3000; i++)
                    Assert.Equal($"w{worker} i{i}", Format("w{Worker} i{Index}", worker, i));
            }))
            .ToArray();

        await Task.WhenAll(work).WaitAsync(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void Thousands_of_distinct_templates_still_format_correctly_past_the_cache_limit()
    {
        for (var i = 0; i < 3000; i++)
            Assert.Equal("v=" + i, Format("v={V" + i + "}", i));
    }
}
