namespace Feeder.Rx.Tests;

public class DisposableTests
{
    sealed class Counter : IDisposable
    {
        public int Disposed;

        public void Dispose() => Interlocked.Increment(ref Disposed);
    }

    [Fact]
    public void Create_runs_the_action_once()
    {
        var calls = 0;
        var disposable = Disposable.Create(() => calls++);

        disposable.Dispose();
        disposable.Dispose();

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Empty_can_be_disposed_any_number_of_times()
    {
        Disposable.Empty.Dispose();
        Disposable.Empty.Dispose();
    }

    [Fact]
    public void Composite_disposes_everything_it_holds_once()
    {
        var composite = new CompositeDisposable();
        var a = new Counter();
        var b = new Counter();
        composite.Add(a);
        composite.Add(b);

        composite.Dispose();
        composite.Dispose();

        Assert.Equal(1, a.Disposed);
        Assert.Equal(1, b.Disposed);
        Assert.Empty(composite);
    }

    [Fact]
    public void Composite_disposes_an_item_added_after_it_was_disposed()
    {
        var composite = new CompositeDisposable();
        composite.Dispose();
        var late = new Counter();

        composite.Add(late);

        Assert.Equal(1, late.Disposed);
        Assert.Empty(composite);
    }

    [Fact]
    public void Clear_disposes_the_items_and_leaves_the_composite_usable()
    {
        var composite = new CompositeDisposable();
        var first = new Counter();
        composite.Add(first);

        composite.Clear();
        var second = new Counter();
        composite.Add(second);

        Assert.Equal(1, first.Disposed);
        Assert.Equal(0, second.Disposed);
        Assert.Single(composite);
        composite.Dispose();
        Assert.Equal(1, second.Disposed);
    }

    [Fact]
    public void Remove_takes_the_item_out_and_disposes_it()
    {
        var composite = new CompositeDisposable();
        var item = new Counter();
        composite.Add(item);

        Assert.True(composite.Remove(item));
        Assert.False(composite.Remove(item));
        composite.Dispose();

        Assert.Equal(1, item.Disposed);
    }

    [Fact]
    public void Composite_behaves_as_a_collection_of_disposables()
    {
        var composite = new CompositeDisposable();
        var a = new Counter();
        var b = new Counter();
        ICollection<IDisposable> collection = composite;
        collection.Add(a);
        collection.Add(b);

        var copy = new IDisposable[2];
        collection.CopyTo(copy, 0);

        Assert.False(collection.IsReadOnly);
        Assert.True(collection.Contains(a));
        Assert.Equal(2, collection.Count);
        Assert.Equal(new IDisposable[] { a, b }, copy);
        Assert.Equal(new IDisposable[] { a, b }, collection.ToArray());
    }

    [Fact]
    public void AddTo_registers_the_disposable_and_returns_it()
    {
        var composite = new CompositeDisposable();
        var item = new Counter();

        var returned = item.AddTo(composite);
        composite.Dispose();

        Assert.Same(item, returned);
        Assert.Equal(1, item.Disposed);
    }

    [Fact]
    public void A_subscription_added_to_a_composite_ends_with_it()
    {
        var subject = new Subject<int>();
        var composite = new CompositeDisposable();
        var received = new List<int>();
        subject.Subscribe(received.Add).AddTo(composite);

        subject.OnNext(1);
        composite.Dispose();
        subject.OnNext(2);

        Assert.Equal(new[] { 1 }, received);
    }

    [Fact]
    public void Serial_disposes_the_previous_value_when_replaced()
    {
        var serial = new SerialDisposable();
        var first = new Counter();
        var second = new Counter();

        serial.Disposable = first;
        serial.Disposable = second;

        Assert.Equal(1, first.Disposed);
        Assert.Equal(0, second.Disposed);
        Assert.Same(second, serial.Disposable);
    }

    [Fact]
    public void Serial_disposes_the_current_value_once_and_refuses_later_ones()
    {
        var serial = new SerialDisposable();
        var current = new Counter();
        serial.Disposable = current;

        serial.Dispose();
        serial.Dispose();
        var late = new Counter();
        serial.Disposable = late;

        Assert.Equal(1, current.Disposed);
        Assert.Equal(1, late.Disposed);
        Assert.Null(serial.Disposable);
    }

    [Fact]
    public void Serial_accepts_null_to_clear_the_value()
    {
        var serial = new SerialDisposable();
        var current = new Counter();
        serial.Disposable = current;

        serial.Disposable = null;

        Assert.Equal(1, current.Disposed);
        Assert.Null(serial.Disposable);
    }

    [Fact]
    public async Task Composite_tolerates_adds_and_clears_from_many_threads()
    {
        var composite = new CompositeDisposable();
        var added = new Counter();
        var work = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 5000; i++)
                {
                    composite.Add(added);
                    if (i % 7 == 0)
                        composite.Clear();
                }
            }))
            .ToArray();
        await Task.WhenAll(work).WaitAsync(TimeSpan.FromSeconds(60));
        composite.Dispose();

        Assert.Equal(4 * 5000, added.Disposed);
    }
}
