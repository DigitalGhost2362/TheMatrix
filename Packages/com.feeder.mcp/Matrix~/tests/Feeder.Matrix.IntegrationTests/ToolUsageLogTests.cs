using System.Text.Json;
using Feeder.Matrix.Usage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Feeder.Matrix.IntegrationTests;

public class ToolUsageLogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mx-usage-" + Guid.NewGuid().ToString("N"));
    private string? _linked;

    public ToolUsageLogTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private ToolUsageLog NewLog(bool enabled = true)
        => new(() => _linked, enabled, TimeSpan.FromHours(1), NullLogger<ToolUsageLog>.Instance);

    private static void Call(ToolUsageLog log, string tool, string status = "ok", string? via = null)
    {
        var scope = log.Begin(tool);
        scope.Via = via;
        log.Record(scope, "tests", "main", TimeSpan.FromMilliseconds(12), status, 40, 900);
    }

    private string[] Lines() => Directory.GetFiles(Path.Combine(_root, ToolUsageLog.Folder), "usage-*.jsonl").SelectMany(File.ReadAllLines).ToArray();

    [Fact]
    public void Lines_wait_in_memory_until_a_project_is_linked_then_are_written_in_one_batch()
    {
        var log = NewLog();
        Call(log, "rql-query");
        Call(log, "script-execute", "error");

        Assert.Equal(0, log.Flush());
        Assert.False(Directory.Exists(Path.Combine(_root, ToolUsageLog.Folder)));

        _linked = _root;
        Assert.Equal(2, log.Flush());
        var lines = Lines();
        Assert.Equal(2, lines.Length);
        using var first = JsonDocument.Parse(lines[0]);
        Assert.Equal("rql-query", first.RootElement.GetProperty("tool").GetString());
        Assert.Equal("ok", first.RootElement.GetProperty("status").GetString());
        Assert.Equal(40, first.RootElement.GetProperty("argBytes").GetInt64());
        Assert.Equal(900, first.RootElement.GetProperty("resultBytes").GetInt64());
    }

    [Fact]
    public void A_scope_status_and_a_tool_call_target_win()
    {
        _linked = _root;
        var log = NewLog();
        var scope = log.Begin("tool-call");
        scope.Tool = "scene-create";
        scope.Via = "tool-call";
        scope.Status = "refused";
        log.Record(scope, "tests", "main", TimeSpan.Zero, "ok", 0, 0);
        log.Flush();

        using var doc = JsonDocument.Parse(Lines().Single());
        Assert.Equal("scene-create", doc.RootElement.GetProperty("tool").GetString());
        Assert.Equal("tool-call", doc.RootElement.GetProperty("via").GetString());
        Assert.Equal("refused", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void Files_are_one_per_month_and_only_three_months_are_kept()
    {
        _linked = _root;
        var folder = Path.Combine(_root, ToolUsageLog.Folder);
        Directory.CreateDirectory(folder);
        var now = DateTimeOffset.UtcNow;
        var old = Path.GetFileName(ToolUsageLog.FileFor(_root, now.AddMonths(-3)));
        var kept = Path.GetFileName(ToolUsageLog.FileFor(_root, now.AddMonths(-2)));
        File.WriteAllText(Path.Combine(folder, old), "{}\n");
        File.WriteAllText(Path.Combine(folder, kept), "{}\n");

        var log = NewLog();
        Call(log, "console-get-logs");
        log.Flush();

        Assert.False(File.Exists(Path.Combine(folder, old)));
        Assert.True(File.Exists(Path.Combine(folder, kept)));
        Assert.True(File.Exists(ToolUsageLog.FileFor(_root, now)));
        Assert.EndsWith($"usage-{now.UtcDateTime:yyyy-MM}.jsonl", ToolUsageLog.FileFor(_root, now));
    }

    [Fact]
    public void Turned_off_it_writes_nothing()
    {
        _linked = _root;
        var log = NewLog(enabled: false);
        Call(log, "rql-query");
        Assert.Equal(0, log.Flush());
        Assert.False(Directory.Exists(Path.Combine(_root, ToolUsageLog.Folder)));
    }

    [Fact]
    public void Sizes_never_carry_the_argument_text()
    {
        var args = new Dictionary<string, JsonElement> { ["csharpCode"] = JsonSerializer.SerializeToElement("return \"secret-token\";") };
        Assert.True(ToolUsageLog.SizeOf(args) > 0);
        _linked = _root;
        var log = NewLog();
        var scope = log.Begin("script-execute");
        log.Record(scope, "tests", "main", TimeSpan.Zero, "ok", ToolUsageLog.SizeOf(args), 0);
        log.Flush();
        Assert.DoesNotContain("secret-token", File.ReadAllText(ToolUsageLog.FileFor(_root, DateTimeOffset.UtcNow)));
    }
}
