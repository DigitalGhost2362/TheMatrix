namespace Feeder.Rx.Tests;

public class SubjectTests
{
    [Fact]
    public void Delivers_to_every_subscriber_in_subscription_order()
    {
        var subject = new Subject<int>();
        var log = new List<string>();
        subject.Subscribe(value => log.Add("a" + value));
        subject.Subscribe(value => log.Add("b" + value));

        subject.OnNext(1);
        subject.OnNext(2);

        Assert.Equal(new[] { "a1", "b1", "a2", "b2" }, log);
    }

    [Fact]
    public void Does_not_replay_to_late_subscribers()
    {
        var subject = new Subject<int>();
        subject.OnNext(1);
        var received = new List<int>();

        subject.Subscribe(received.Add);

        Assert.Empty(received);
    }

    [Fact]
    public void Does_not_suppress_equal_consecutive_values()
    {
        var subject = new Subject<int>();
        var received = new List<int>();
        subject.Subscribe(received.Add);

        subject.OnNext(7);
        subject.OnNext(7);

        Assert.Equal(new[] { 7, 7 }, received);
    }

    [Fact]
    public void A_throwing_subscriber_stops_neither_the_others_nor_the_producer()
    {
        using var errors = new ErrorCapture();
        var subject = new Subject<int>();
        var received = new List<int>();
        subject.Subscribe(_ => throw new InvalidOperationException("boom"));
        subject.Subscribe(received.Add);

        subject.OnNext(1);
        subject.OnNext(2);

        Assert.Equal(new[] { 1, 2 }, received);
        Assert.Contains("boom", errors.Text);
    }

    [Fact]
    public void Disposing_a_subscription_stops_its_deliveries_only()
    {
        var subject = new Subject<int>();
        var kept = new List<int>();
        var dropped = new List<int>();
        subject.Subscribe(kept.Add);
        var subscription = subject.Subscribe(dropped.Add);

        subject.OnNext(1);
        subscription.Dispose();
        subscription.Dispose();
        subject.OnNext(2);

        Assert.Equal(new[] { 1, 2 }, kept);
        Assert.Equal(new[] { 1 }, dropped);
    }

    [Fact]
    public void A_subscriber_can_dispose_itself_while_it_is_called()
    {
        var subject = new Subject<int>();
        var calls = 0;
        IDisposable? self = null;
        self = subject.Subscribe(_ =>
        {
            calls++;
            self!.Dispose();
        });

        subject.OnNext(1);
        subject.OnNext(2);

        Assert.Equal(1, calls);
    }

    [Fact]
    public void A_subscriber_disposed_mid_dispatch_is_skipped_for_the_value_in_flight()
    {
        var subject = new Subject<int>();
        var later = new List<int>();
        IDisposable? laterSubscription = null;
        subject.Subscribe(_ => laterSubscription!.Dispose());
        laterSubscription = subject.Subscribe(later.Add);

        subject.OnNext(1);
        subject.OnNext(2);

        Assert.Empty(later);
    }

    [Fact]
    public void A_subscriber_added_mid_dispatch_misses_the_value_in_flight()
    {
        var subject = new Subject<int>();
        var added = new List<int>();
        var done = false;
        subject.Subscribe(_ =>
        {
            if (done)
                return;
            done = true;
            subject.Subscribe(added.Add);
        });

        subject.OnNext(1);
        subject.OnNext(2);

        Assert.Equal(new[] { 2 }, added);
    }

    [Fact]
    public void Disposing_the_subject_mid_dispatch_skips_the_remaining_subscribers()
    {
        var subject = new Subject<int>();
        var later = new List<int>();
        subject.Subscribe(_ => subject.Dispose());
        subject.Subscribe(later.Add);

        subject.OnNext(1);

        Assert.Empty(later);
    }

    [Fact]
    public void A_disposed_subject_ignores_values_and_hands_out_inert_subscriptions()
    {
        var subject = new Subject<int>();
        var received = new List<int>();
        subject.Subscribe(received.Add);
        subject.Dispose();
        subject.Dispose();

        subject.OnNext(1);
        var late = subject.Subscribe(received.Add);
        subject.OnNext(2);
        late.Dispose();

        Assert.Empty(received);
    }

    [Fact]
    public void A_subscriber_can_publish_back_into_the_subject()
    {
        var subject = new Subject<int>();
        var received = new List<int>();
        subject.Subscribe(value =>
        {
            if (value < 3)
                subject.OnNext(value + 1);
        });
        subject.Subscribe(received.Add);

        subject.OnNext(1);

        Assert.Equal(new[] { 3, 2, 1 }, received);
    }

    [Fact]
    public void Subscribing_with_a_null_callback_fails_at_once()
    {
        var subject = new Subject<int>();

        Assert.Throws<ArgumentNullException>(() => subject.Subscribe(null!));
    }

    [Fact]
    public async Task Producers_on_many_threads_and_subscription_churn_lose_nothing_for_a_stable_subscriber()
    {
        var subject = new Subject<int>();
        var total = 0L;
        subject.Subscribe(_ => Interlocked.Increment(ref total));
        const int producers = 4;
        const int perProducer = 20000;
        using var stop = new CancellationTokenSource();
        var churn = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
                subject.Subscribe(_ => { }).Dispose();
        });

        var work = Enumerable.Range(0, producers)
            .Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < perProducer; i++)
                    subject.OnNext(i);
            }))
            .ToArray();
        await Task.WhenAll(work).WaitAsync(TimeSpan.FromSeconds(60));
        stop.Cancel();
        await churn.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(producers * perProducer, Interlocked.Read(ref total));
    }
}
