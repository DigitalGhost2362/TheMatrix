[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Feeder.Rx.Tests;

// subscriber failures are reported on the error stream; tests that look at it must not overlap
internal sealed class ErrorCapture : IDisposable
{
    readonly TextWriter original = Console.Error;
    readonly StringWriter buffer = new();

    public ErrorCapture() => Console.SetError(buffer);

    public string Text => buffer.ToString();

    public void Dispose() => Console.SetError(original);
}

// stands in for the editor UI context: Post only queues, the test decides when the "UI thread" runs
internal sealed class QueueContext : SynchronizationContext
{
    readonly Queue<(SendOrPostCallback Callback, object? State)> queue = new();

    public int Pending
    {
        get
        {
            lock (queue)
                return queue.Count;
        }
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (queue)
            queue.Enqueue((d, state));
    }

    public void RunAll()
    {
        while (true)
        {
            (SendOrPostCallback Callback, object? State) item;
            lock (queue)
            {
                if (queue.Count == 0)
                    return;
                item = queue.Dequeue();
            }
            item.Callback(item.State);
        }
    }

    public IDisposable Install()
    {
        var previous = Current;
        SetSynchronizationContext(this);
        return Disposable.Create(() => SetSynchronizationContext(previous));
    }
}
