using Feeder.Matrix.Tools;

namespace Feeder.Matrix.Unity;

/// <summary>Frees idle editor claims, notices lock files and keeps the editor's lock display current.</summary>
public sealed class EditorLockMonitor(EditorLock editorLock, TimeProvider time, ILogger<EditorLockMonitor> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await editorLock.TickAsync();
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Editor lock pass failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
