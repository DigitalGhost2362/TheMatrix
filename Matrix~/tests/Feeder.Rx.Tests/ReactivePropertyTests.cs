namespace Feeder.Rx.Tests;

public class ReactivePropertyTests
{
    enum State
    {
        Disconnected,
        Connecting,
        Connected,
    }

    [Fact]
    public void Replays_the_current_value_before_Subscribe_returns()
    {
        var property = new ReactiveProperty<int>(5);
        var received = new List<int>();

        property.Subscribe(received.Add);

        Assert.Equal(new[] { 5 }, received);
    }

    [Fact]
    public void Replays_the_latest_value_to_a_late_subscriber()
    {
        var property = new ReactiveProperty<int>(1);
        property.Value = 2;
        property.Value = 3;
        var received = new List<int>();

        property.Subscribe(received.Add);

        Assert.Equal(new[] { 3 }, received);
    }

    [Fact]
    public void Delivers_changes_and_suppresses_equal_consecutive_values()
    {
        var property = new ReactiveProperty<int>(0);
        var received = new List<int>();
        property.Subscribe(received.Add);

        property.Value = 1;
        property.Value = 1;
        property.Value = 2;
        property.Value = 1;
        property.Value = 1;

        Assert.Equal(new[] { 0, 1, 2, 1 }, received);
    }

    [Fact]
    public void Starting_value_is_not_delivered_again_when_set_to_the_same_value()
    {
        var property = new ReactiveProperty<State>(State.Connected);
        var received = new List<State>();
        property.Subscribe(received.Add);

        property.Value = State.Connected;

        Assert.Equal(new[] { State.Connected }, received);
    }

    [Fact]
    public void Null_is_a_value_like_any_other()
    {
        var property = new ReactiveProperty<string?>(null);
        var received = new List<string?>();
        property.Subscribe(received.Add);

        property.Value = null;
        property.Value = "a";
        property.Value = null;
        property.Value = null;

        Assert.Equal(new string?[] { null, "a", null }, received);
    }

    [Fact]
    public void The_default_constructor_starts_from_default()
    {
        var property = new ReactiveProperty<int>();

        Assert.Equal(0, property.Value);
        Assert.Equal(0, property.CurrentValue);
    }

    [Fact]
    public void CurrentValue_follows_Value_and_is_visible_through_the_read_only_type()
    {
        var property = new ReactiveProperty<string>("a");
        ReadOnlyReactiveProperty<string> view = property;

        property.Value = "b";

        Assert.Equal("b", property.Value);
        Assert.Equal("b", view.CurrentValue);
    }

    [Fact]
    public void Value_is_already_updated_when_subscribers_are_called()
    {
        var property = new ReactiveProperty<int>(0);
        var seen = new List<int>();
        property.Subscribe(_ => seen.Add(property.CurrentValue));

        property.Value = 9;

        Assert.Equal(new[] { 0, 9 }, seen);
    }

    [Fact]
    public void Disposing_a_subscription_stops_its_deliveries()
    {
        var property = new ReactiveProperty<int>(0);
        var received = new List<int>();
        var subscription = property.Subscribe(received.Add);

        subscription.Dispose();
        property.Value = 1;

        Assert.Equal(new[] { 0 }, received);
    }

    [Fact]
    public void A_throwing_replay_does_not_escape_Subscribe_and_the_subscription_stays_live()
    {
        using var errors = new ErrorCapture();
        var property = new ReactiveProperty<int>(0);
        var calls = 0;

        property.Subscribe(_ =>
        {
            calls++;
            throw new InvalidOperationException("replay failed");
        });
        property.Value = 1;

        Assert.Equal(2, calls);
        Assert.Contains("replay failed", errors.Text);
    }

    [Fact]
    public void A_throwing_subscriber_stops_neither_the_others_nor_the_setter()
    {
        using var errors = new ErrorCapture();
        var property = new ReactiveProperty<int>(0);
        var received = new List<int>();
        var armed = false;
        property.Subscribe(_ =>
        {
            if (armed)
                throw new InvalidOperationException("boom");
        });
        property.Subscribe(received.Add);
        armed = true;

        property.Value = 1;

        Assert.Equal(new[] { 0, 1 }, received);
        Assert.Equal(1, property.Value);
        Assert.Contains("boom", errors.Text);
    }

    [Fact]
    public void A_disposed_property_keeps_its_last_value_and_ignores_writes_and_subscribers()
    {
        var property = new ReactiveProperty<int>(1);
        var received = new List<int>();
        property.Subscribe(received.Add);

        property.Dispose();
        property.Dispose();
        property.Value = 2;
        var late = property.Subscribe(received.Add);
        late.Dispose();

        Assert.True(property.IsDisposed);
        Assert.Equal(1, property.CurrentValue);
        Assert.Equal(new[] { 1 }, received);
    }

    [Fact]
    public void IsDisposed_is_false_until_disposed()
    {
        var property = new ReactiveProperty<int>(1);

        Assert.False(property.IsDisposed);
    }

    [Fact]
    public void Can_be_owned_by_a_composite_disposable()
    {
        var owner = new CompositeDisposable();
        var property = new ReactiveProperty<int>(1);
        owner.Add(property);

        owner.Dispose();

        Assert.True(property.IsDisposed);
    }

    [Fact]
    public void ToReadOnlyReactiveProperty_starts_from_the_source_current_value()
    {
        var source = new ReactiveProperty<State>(State.Connected);

        var isConnected = source
            .Select(state => state == State.Connected)
            .ToReadOnlyReactiveProperty(false);

        Assert.True(isConnected.CurrentValue);
    }

    [Fact]
    public void ToReadOnlyReactiveProperty_tracks_the_source_and_suppresses_repeats()
    {
        var source = new ReactiveProperty<State>(State.Disconnected);
        var isConnected = source.Select(state => state == State.Connected).ToReadOnlyReactiveProperty(false);
        var received = new List<bool>();
        isConnected.Subscribe(received.Add);

        source.Value = State.Connecting;
        source.Value = State.Connected;
        source.Value = State.Disconnected;

        Assert.Equal(new[] { false, true, false }, received);
        Assert.False(isConnected.CurrentValue);
    }

    [Fact]
    public void ToReadOnlyReactiveProperty_uses_the_initial_value_until_the_source_emits()
    {
        var source = new Subject<int>();

        var property = source.ToReadOnlyReactiveProperty(-1);
        Assert.Equal(-1, property.CurrentValue);
        source.OnNext(4);

        Assert.Equal(4, property.CurrentValue);
    }

    [Fact]
    public void Disposing_a_derived_property_detaches_it_from_the_source()
    {
        var source = new Subject<int>();
        var property = source.ToReadOnlyReactiveProperty(0);
        source.OnNext(1);

        property.Dispose();
        source.OnNext(2);

        Assert.Equal(1, property.CurrentValue);
        Assert.True(property.IsDisposed);
    }

    [Fact]
    public async Task Writers_on_many_threads_deliver_every_distinct_write_to_a_stable_subscriber()
    {
        var property = new ReactiveProperty<int>(0);
        var deliveries = 0L;
        property.Subscribe(_ => Interlocked.Increment(ref deliveries));
        const int writers = 4;
        const int perWriter = 5000;

        // all written values differ from each other and from 0, so no write may be suppressed
        var work = Enumerable.Range(0, writers)
            .Select(writer => Task.Run(() =>
            {
                for (var i = 1; i <= perWriter; i++)
                    property.Value = writer * perWriter + i;
            }))
            .ToArray();
        await Task.WhenAll(work).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(1 + writers * perWriter, Interlocked.Read(ref deliveries));
        Assert.Contains(property.CurrentValue, Enumerable.Range(0, writers).Select(writer => writer * perWriter + perWriter));
    }
}
