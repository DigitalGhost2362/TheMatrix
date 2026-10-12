namespace Feeder.Rx.Tests;

public class OperatorTests
{
    [Fact]
    public void Where_passes_only_matching_values()
    {
        var subject = new Subject<int>();
        var received = new List<int>();
        subject.Where(value => value % 2 == 0).Subscribe(received.Add);

        for (var i = 1; i <= 6; i++)
            subject.OnNext(i);

        Assert.Equal(new[] { 2, 4, 6 }, received);
    }

    [Fact]
    public void Where_chains_apply_every_predicate()
    {
        var subject = new Subject<int>();
        var received = new List<int>();
        subject.Where(value => value > 1).Where(value => value < 4).Subscribe(received.Add);

        for (var i = 0; i <= 5; i++)
            subject.OnNext(i);

        Assert.Equal(new[] { 2, 3 }, received);
    }

    [Fact]
    public void Select_maps_every_value()
    {
        var subject = new Subject<int>();
        var received = new List<string>();
        subject.Select(value => "v" + value).Subscribe(received.Add);

        subject.OnNext(1);
        subject.OnNext(2);

        Assert.Equal(new[] { "v1", "v2" }, received);
    }

    [Fact]
    public void An_operator_function_that_throws_is_isolated_like_a_subscriber()
    {
        using var errors = new ErrorCapture();
        var subject = new Subject<int>();
        var received = new List<int>();
        subject.Where(value => value != 2 ? true : throw new InvalidOperationException("predicate failed")).Subscribe(received.Add);
        var other = new List<int>();
        subject.Subscribe(other.Add);

        subject.OnNext(1);
        subject.OnNext(2);
        subject.OnNext(3);

        Assert.Equal(new[] { 1, 3 }, received);
        Assert.Equal(new[] { 1, 2, 3 }, other);
        Assert.Contains("predicate failed", errors.Text);
    }

    [Fact]
    public void WhereNotNull_drops_nulls_and_narrows_the_type()
    {
        var property = new ReactiveProperty<string?>(null);
        var received = new List<string>();
        property.WhereNotNull().Subscribe(received.Add);

        property.Value = "a";
        property.Value = null;
        property.Value = "b";

        Assert.Equal(new[] { "a", "b" }, received);
    }

    [Fact]
    public void WhereNotNull_on_a_reactive_property_skips_a_null_replay_and_delivers_a_present_one()
    {
        var present = new ReactiveProperty<string?>("x");
        var absent = new ReactiveProperty<string?>(null);
        var received = new List<string>();

        present.WhereNotNull().Subscribe(received.Add);
        absent.WhereNotNull().Subscribe(received.Add);

        Assert.Equal(new[] { "x" }, received);
    }

    [Fact]
    public void AsObservable_hides_the_source_type_and_keeps_delivering()
    {
        var subject = new Subject<int>();
        var exposed = subject.AsObservable();
        var received = new List<int>();

        var subscription = exposed.Subscribe(received.Add);
        subject.OnNext(1);
        subscription.Dispose();
        subject.OnNext(2);

        Assert.IsNotType<Subject<int>>(exposed);
        Assert.Equal(new[] { 1 }, received);
    }

    [Fact]
    public void AsObservable_over_a_reactive_property_still_replays()
    {
        var property = new ReactiveProperty<int>(8);
        var received = new List<int>();

        property.AsObservable().Subscribe(received.Add);

        Assert.Equal(new[] { 8 }, received);
    }

    [Fact]
    public void Operators_attach_per_subscription_and_detach_with_it()
    {
        var subject = new Subject<int>();
        var evaluations = 0;
        var chain = subject.Select(value =>
        {
            evaluations++;
            return value;
        });

        subject.OnNext(1);
        var first = chain.Subscribe(_ => { });
        var second = chain.Subscribe(_ => { });
        subject.OnNext(2);
        first.Dispose();
        subject.OnNext(3);
        second.Dispose();
        subject.OnNext(4);

        Assert.Equal(3, evaluations);
    }

    [Fact]
    public void Merge_forwards_values_from_every_source()
    {
        var a = new Subject<int>();
        var b = new Subject<int>();
        var received = new List<int>();
        Observable.Merge(a, b).Subscribe(received.Add);

        a.OnNext(1);
        b.OnNext(2);
        a.OnNext(3);

        Assert.Equal(new[] { 1, 2, 3 }, received);
    }

    [Fact]
    public void Merge_of_reactive_properties_replays_each_current_value_in_argument_order()
    {
        var pending = new ReactiveProperty<string?>("pending");
        var fault = new ReactiveProperty<string?>(null);
        var moved = new ReactiveProperty<string?>("moved");
        var received = new List<string?>();

        Observable.Merge(pending, fault, moved).Subscribe(received.Add);
        fault.Value = "fault";

        Assert.Equal(new[] { "pending", null, "moved", "fault" }, received);
    }

    [Fact]
    public void Disposing_a_merged_subscription_detaches_from_every_source()
    {
        var a = new Subject<int>();
        var b = new Subject<int>();
        var received = new List<int>();
        var subscription = Observable.Merge(a, b).Subscribe(received.Add);

        subscription.Dispose();
        a.OnNext(1);
        b.OnNext(2);

        Assert.Empty(received);
    }

    [Fact]
    public void Merge_without_sources_is_silent()
    {
        var received = new List<int>();

        var subscription = Observable.Merge<int>().Subscribe(received.Add);
        subscription.Dispose();

        Assert.Empty(received);
    }

    [Fact]
    public void Empty_emits_nothing_and_can_be_disposed()
    {
        var received = new List<Unit>();

        var subscription = Observable.Empty<Unit>().Subscribe(received.Add);
        subscription.Dispose();

        Assert.Empty(received);
    }

    [Fact]
    public void ThrottleFirst_passes_the_first_value_and_drops_the_rest_of_the_window()
    {
        var subject = new Subject<int>();
        var received = new List<int>();
        subject.ThrottleFirst(TimeSpan.FromMilliseconds(400)).Subscribe(received.Add);

        subject.OnNext(1);
        subject.OnNext(2);
        subject.OnNext(3);

        Assert.Equal(new[] { 1 }, received);
    }

    [Fact]
    public void ThrottleFirst_opens_again_after_the_window_and_never_emits_a_trailing_value()
    {
        var subject = new Subject<int>();
        var received = new List<int>();
        subject.ThrottleFirst(TimeSpan.FromMilliseconds(150)).Subscribe(received.Add);

        subject.OnNext(1);
        subject.OnNext(2);
        Thread.Sleep(450);
        Assert.Equal(new[] { 1 }, received);
        subject.OnNext(3);
        subject.OnNext(4);

        Assert.Equal(new[] { 1, 3 }, received);
    }

    [Fact]
    public void ThrottleFirst_keeps_a_separate_window_per_subscription()
    {
        var subject = new Subject<int>();
        var throttled = subject.ThrottleFirst(TimeSpan.FromMilliseconds(400));
        var first = new List<int>();
        var second = new List<int>();

        throttled.Subscribe(first.Add);
        subject.OnNext(1);
        throttled.Subscribe(second.Add);
        subject.OnNext(2);

        Assert.Equal(new[] { 1 }, first);
        Assert.Equal(new[] { 2 }, second);
    }

    [Fact]
    public void ThrottleFirst_with_a_zero_window_passes_everything()
    {
        var subject = new Subject<int>();
        var received = new List<int>();
        subject.ThrottleFirst(TimeSpan.Zero).Subscribe(received.Add);

        subject.OnNext(1);
        subject.OnNext(2);

        Assert.Equal(new[] { 1, 2 }, received);
    }

    [Fact]
    public async Task ThrottleFirst_lets_exactly_one_of_many_simultaneous_values_through()
    {
        var subject = new Subject<int>();
        var passed = 0;
        subject.ThrottleFirst(TimeSpan.FromSeconds(30)).Subscribe(_ => Interlocked.Increment(ref passed));

        var work = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 1000; i++)
                    subject.OnNext(i);
            }))
            .ToArray();
        await Task.WhenAll(work).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(1, passed);
    }

    [Fact]
    public void ObserveOn_posts_to_the_captured_context_and_keeps_order()
    {
        var context = new QueueContext();
        var subject = new Subject<int>();
        var received = new List<int>();
        using (context.Install())
            subject.ObserveOnCurrentSynchronizationContext().Subscribe(received.Add);

        subject.OnNext(1);
        subject.OnNext(2);
        subject.OnNext(3);

        Assert.Empty(received);
        Assert.Equal(1, context.Pending);
        context.RunAll();
        Assert.Equal(new[] { 1, 2, 3 }, received);
    }

    [Fact]
    public void ObserveOn_posts_again_after_a_drain()
    {
        var context = new QueueContext();
        var subject = new Subject<int>();
        var received = new List<int>();
        using (context.Install())
            subject.ObserveOnCurrentSynchronizationContext().Subscribe(received.Add);

        subject.OnNext(1);
        context.RunAll();
        subject.OnNext(2);
        context.RunAll();

        Assert.Equal(new[] { 1, 2 }, received);
    }

    [Fact]
    public void ObserveOn_captures_the_context_when_applied_not_when_subscribed()
    {
        var applied = new QueueContext();
        var subscribed = new QueueContext();
        var subject = new Subject<int>();
        var received = new List<int>();
        Observable<int> chain;
        using (applied.Install())
            chain = subject.ObserveOnCurrentSynchronizationContext();
        using (subscribed.Install())
            chain.Subscribe(received.Add);

        subject.OnNext(1);

        Assert.Equal(1, applied.Pending);
        Assert.Equal(0, subscribed.Pending);
    }

    [Fact]
    public void ObserveOn_delivers_the_replayed_value_through_the_context_too()
    {
        var context = new QueueContext();
        var property = new ReactiveProperty<int>(5);
        var received = new List<int>();
        using (context.Install())
            property.ObserveOnCurrentSynchronizationContext().Subscribe(received.Add);

        Assert.Empty(received);
        context.RunAll();

        Assert.Equal(new[] { 5 }, received);
    }

    [Fact]
    public void ObserveOn_survives_a_throwing_subscriber()
    {
        using var errors = new ErrorCapture();
        var context = new QueueContext();
        var subject = new Subject<int>();
        var received = new List<int>();
        using (context.Install())
        {
            subject.ObserveOnCurrentSynchronizationContext().Subscribe(value =>
            {
                if (value == 2)
                    throw new InvalidOperationException("ui failed");
                received.Add(value);
            });
        }

        subject.OnNext(1);
        subject.OnNext(2);
        subject.OnNext(3);
        context.RunAll();
        subject.OnNext(4);
        context.RunAll();

        Assert.Equal(new[] { 1, 3, 4 }, received);
        Assert.Contains("ui failed", errors.Text);
    }

    [Fact]
    public void ObserveOn_discards_queued_values_once_the_subscription_is_disposed()
    {
        var context = new QueueContext();
        var subject = new Subject<int>();
        var received = new List<int>();
        IDisposable subscription;
        using (context.Install())
            subscription = subject.ObserveOnCurrentSynchronizationContext().Subscribe(received.Add);

        subject.OnNext(1);
        subscription.Dispose();
        context.RunAll();
        subject.OnNext(2);
        context.RunAll();

        Assert.Empty(received);
    }

    [Fact]
    public void ObserveOn_a_subscriber_may_dispose_itself_from_the_drain()
    {
        var context = new QueueContext();
        var subject = new Subject<int>();
        var received = new List<int>();
        IDisposable? subscription = null;
        using (context.Install())
        {
            subscription = subject.ObserveOnCurrentSynchronizationContext().Subscribe(value =>
            {
                received.Add(value);
                subscription!.Dispose();
            });
        }

        subject.OnNext(1);
        subject.OnNext(2);
        context.RunAll();

        Assert.Equal(new[] { 1 }, received);
    }

    [Fact]
    public async Task ObserveOn_without_a_context_delivers_in_order_on_the_thread_pool()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        var subject = new Subject<int>();
        var received = new List<int>();
        var done = new TaskCompletionSource();
        try
        {
            subject.ObserveOnCurrentSynchronizationContext().Subscribe(value =>
            {
                lock (received)
                {
                    received.Add(value);
                    if (received.Count == 200)
                        done.SetResult();
                }
            });
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        for (var i = 0; i < 200; i++)
            subject.OnNext(i);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(Enumerable.Range(0, 200), received);
    }

    [Fact]
    public async Task ObserveOn_keeps_per_producer_order_when_fed_from_many_threads()
    {
        var context = new QueueContext();
        var subject = new Subject<(int Producer, int Index)>();
        var received = new List<(int Producer, int Index)>();
        using (context.Install())
            subject.ObserveOnCurrentSynchronizationContext().Subscribe(received.Add);

        var work = Enumerable.Range(0, 4)
            .Select(producer => Task.Run(() =>
            {
                for (var i = 0; i < 2000; i++)
                    subject.OnNext((producer, i));
            }))
            .ToArray();
        await Task.WhenAll(work).WaitAsync(TimeSpan.FromSeconds(60));
        context.RunAll();

        Assert.Equal(8000, received.Count);
        for (var producer = 0; producer < 4; producer++)
            Assert.Equal(Enumerable.Range(0, 2000), received.Where(item => item.Producer == producer).Select(item => item.Index));
    }
}
