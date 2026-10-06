namespace Feeder.Matrix.Unity;

/// <summary>Re-broadcasts MCP clients when sessions go idle (or revive) without any request to trigger it.</summary>
public sealed class McpClientActivityMonitor(McpClientTracker tracker, TimeProvider time) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await tracker.BroadcastIfActiveCountChangedAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }
}
