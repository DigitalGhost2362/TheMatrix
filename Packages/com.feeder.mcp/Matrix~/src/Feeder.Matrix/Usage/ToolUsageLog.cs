using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Feeder.Matrix.Unity;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace Feeder.Matrix.Usage;

/// <summary>One tool call as the usage file stores it: who, which tool, how long, how it ended, how big. Never the arguments or the result.</summary>
public sealed record ToolUsageEntry(DateTimeOffset At, string Tool, string? Via, string Client, string Agent, long Ms, string Status, long ArgBytes, long ResultBytes);

/// <summary>Filled while one call runs; the editor lock gate and tool-call update it, the call handler records it at the end.</summary>
public sealed class ToolUsageScope(string tool)
{
    public string Tool { get; set; } = tool;
    public string? Via { get; set; }
    public string? Status { get; set; }
}

/// <summary>
/// Appends one JSON line per tool call to <c>&lt;project&gt;/Library/MatrixUsage/usage-YYYY-MM.jsonl</c>, read by the Skills
/// window. Recording only queues the line; a background loop writes the queue once a second, so a tool call never waits on
/// the disk. Lines queue in memory until a Unity project is linked. Off with <c>FeederMatrix:UsageLog=false</c>.
/// </summary>
public sealed class ToolUsageLog : BackgroundService
{
    public const string Folder = "Library/MatrixUsage";
    public const int KeepMonths = 3;
    const int MaxPending = 10_000;

    static readonly AsyncLocal<ToolUsageScope?> current = new();
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    readonly Channel<ToolUsageEntry> _queue = Channel.CreateUnbounded<ToolUsageEntry>(new UnboundedChannelOptions { SingleReader = true });
    readonly List<ToolUsageEntry> _pending = [];
    readonly Func<string?> _projectRoot;
    readonly bool _enabled;
    readonly TimeSpan _interval;
    readonly ILogger<ToolUsageLog> _logger;
    readonly object _gate = new();
    string? _prunedMonth;

    public ToolUsageLog(UnityLinkRegistry registry, IOptions<MatrixOptions> options, ILogger<ToolUsageLog> logger)
        : this(() => registry.GetLatestLink()?.ProjectRoot, options.Value.UsageLog, TimeSpan.FromSeconds(1), logger)
    {
    }

    public ToolUsageLog(Func<string?> projectRoot, bool enabled, TimeSpan interval, ILogger<ToolUsageLog> logger)
    {
        _projectRoot = projectRoot;
        _enabled = enabled;
        _interval = interval;
        _logger = logger;
    }

    /// <summary>The call in flight on this async flow, or null outside a recorded call.</summary>
    public static ToolUsageScope? Current => current.Value;

    public ToolUsageScope Begin(string tool) => current.Value = new ToolUsageScope(tool);

    public void Record(ToolUsageScope scope, string client, string agent, TimeSpan elapsed, string status, long argBytes, long resultBytes)
    {
        if (!_enabled)
            return;
        _queue.Writer.TryWrite(new ToolUsageEntry(DateTimeOffset.UtcNow, scope.Tool, scope.Via, client, agent,
            (long)elapsed.TotalMilliseconds, scope.Status ?? status, argBytes, resultBytes));
    }

    public static long SizeOf(IDictionary<string, JsonElement>? arguments)
        => arguments?.Sum(pair => (long)Encoding.UTF8.GetByteCount(pair.Key) + Encoding.UTF8.GetByteCount(pair.Value.GetRawText())) ?? 0;

    public static long SizeOf(CallToolResult? result)
    {
        if (result is null)
            return 0;
        long size = result.StructuredContent is { } structured ? Encoding.UTF8.GetByteCount(structured.GetRawText()) : 0;
        foreach (var block in result.Content ?? [])
            size += block switch
            {
                TextContentBlock text => Encoding.UTF8.GetByteCount(text.Text ?? string.Empty),
                ImageContentBlock image => image.Data.Length,
                AudioContentBlock audio => audio.Data.Length,
                _ => 0,
            };
        return size;
    }

    public static string FileFor(string projectRoot, DateTimeOffset at)
        => Path.Combine(projectRoot, Folder, $"usage-{at.UtcDateTime:yyyy-MM}.jsonl");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
            return;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(_interval, stoppingToken);
                Flush();
            }
        }
        catch (OperationCanceledException)
        {
        }
        Flush();
    }

    /// <summary>Writes every queued line now. Returns how many reached the file.</summary>
    public int Flush()
    {
        lock (_gate)
            return FlushLocked();
    }

    int FlushLocked()
    {
        while (_queue.Reader.TryRead(out var entry))
            _pending.Add(entry);
        if (_pending.Count == 0)
            return 0;

        var root = _projectRoot();
        if (string.IsNullOrEmpty(root))
        {
            if (_pending.Count > MaxPending)
                _pending.RemoveRange(0, _pending.Count - MaxPending);
            return 0;
        }

        var written = 0;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Folder));
            foreach (var month in _pending.GroupBy(entry => FileFor(root, entry.At)))
            {
                var lines = new StringBuilder();
                foreach (var entry in month)
                    lines.Append(JsonSerializer.Serialize(entry, Json)).Append('\n');
                File.AppendAllText(month.Key, lines.ToString());
                written += month.Count();
            }
            _pending.Clear();
            Prune(root, DateTimeOffset.UtcNow);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Tool usage log not written ({Error}); keeping {Count} line(s) for the next try.", e.Message, _pending.Count);
        }
        return written;
    }

    // keeps the current month and the two before it
    void Prune(string root, DateTimeOffset now)
    {
        var month = now.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        if (_prunedMonth == month)
            return;
        _prunedMonth = month;
        var oldest = new DateTime(now.UtcDateTime.Year, now.UtcDateTime.Month, 1).AddMonths(-(KeepMonths - 1));
        foreach (var file in Directory.GetFiles(Path.Combine(root, Folder), "usage-*.jsonl"))
        {
            var stamp = Path.GetFileNameWithoutExtension(file)["usage-".Length..];
            if (DateTime.TryParseExact(stamp, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fileMonth) && fileMonth < oldest)
                File.Delete(file);
        }
    }
}
