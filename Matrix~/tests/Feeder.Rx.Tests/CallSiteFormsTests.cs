namespace Feeder.Rx.Tests;

// each test repeats a call form that the package uses today, so a rename or a signature drift fails here
public class CallSiteFormsTests
{
    enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
    }

    interface IPlugin
    {
        string Name { get; }
    }

    sealed class Plugin : IPlugin
    {
        public string Name => "plugin";
    }

    sealed class Manager
    {
        readonly Subject<Unit> forceDisconnect = new Subject<Unit>();
        readonly Subject<IReadOnlyList<string>> clientsChanged = new Subject<IReadOnlyList<string>>();

        // explicit form: static holder, explicit type argument and a cast through object
        public Observable<Unit> OnForceDisconnect => ObservableExtensions.AsObservable<Unit>((Observable<Unit>)(object)forceDisconnect);

        public Observable<IReadOnlyList<string>> OnClientsChanged => ObservableExtensions.AsObservable<IReadOnlyList<string>>((Observable<IReadOnlyList<string>>)(object)clientsChanged);

        public void ForceDisconnect() => forceDisconnect.OnNext(Unit.Default);

        public void ClientsChanged(IReadOnlyList<string> clients) => clientsChanged.OnNext(clients);
    }

    [Fact]
    public void Static_form_subscription_chain_with_AddTo_into_a_collection()
    {
        var disposables = new CompositeDisposable();
        var state = new ReactiveProperty<ConnectionState>(ConnectionState.Disconnected);
        var cancelled = false;
        var seen = new List<ConnectionState>();

        Disposable.AddTo<IDisposable>(ObservableSubscribeExtensions.Subscribe<ConnectionState>(ObservableExtensions.Where<ConnectionState>(ObservableExtensions.Where<ConnectionState>((Observable<ConnectionState>)(object)state, (Func<ConnectionState, bool>)delegate(ConnectionState current)
        {
            return (int)current == 2;
        }), (Func<ConnectionState, bool>)((ConnectionState current) => !cancelled)), (Action<ConnectionState>)delegate(ConnectionState current)
        {
            seen.Add(current);
        }), (ICollection<IDisposable>)disposables);

        state.Value = ConnectionState.Connecting;
        state.Value = ConnectionState.Connected;
        cancelled = true;
        state.Value = ConnectionState.Disconnected;
        state.Value = ConnectionState.Connected;
        disposables.Dispose();

        Assert.Equal(new[] { ConnectionState.Connected }, seen);
    }

    [Fact]
    public async Task Static_form_throttle_with_an_async_void_handler()
    {
        var updates = new Subject<Unit>();
        var disposables = new CompositeDisposable();
        var handled = new TaskCompletionSource();

        Disposable.AddTo<IDisposable>(ObservableSubscribeExtensions.Subscribe<Unit>(ObservableExtensions.ThrottleFirst<Unit>(updates, TimeSpan.FromMilliseconds(100.0)), (Action<Unit>)async delegate
        {
            await Task.Yield();
            handled.TrySetResult();
        }), (ICollection<IDisposable>)disposables);

        updates.OnNext(Unit.Default);

        await handled.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Property_exposed_through_a_cast_from_object_or_a_fresh_default()
    {
        ReadOnlyReactiveProperty<ConnectionState>? hubState = null;

        var exposed = (ReadOnlyReactiveProperty<ConnectionState>)(((object?)hubState) ?? ((object)new ReactiveProperty<ConnectionState>((ConnectionState)0)));
        Observable<Unit> rejected = Observable.Empty<Unit>();

        Assert.Equal(ConnectionState.Disconnected, exposed.CurrentValue);
        Assert.NotNull(rejected);
    }

    [Fact]
    public void AsObservable_exposes_a_subject_without_exposing_its_type()
    {
        var manager = new Manager();
        var seen = 0;
        manager.OnForceDisconnect.Subscribe(_ => seen++);

        manager.ForceDisconnect();

        Assert.Equal(1, seen);
        Assert.IsNotType<Subject<Unit>>(manager.OnForceDisconnect);
    }

    [Fact]
    public void Window_style_bindings_with_method_groups_and_AddTo()
    {
        var subscriptions = new CompositeDisposable();
        var title = new ReactiveProperty<ConnectionState>(ConnectionState.Disconnected);
        var note = new ReactiveProperty<string?>(null);
        var plugin = new ReactiveProperty<IPlugin?>(null);
        var calls = new List<string>();
        var context = new QueueContext();

        void OnTitleState(ConnectionState state) => calls.Add("title:" + state);
        void OnNote(string? text) => calls.Add("note:" + text);
        void OnPlugin(IPlugin value) => calls.Add("plugin:" + value.Name);

        using (context.Install())
        {
            title
                .ObserveOnCurrentSynchronizationContext()
                .Subscribe(OnTitleState)
                .AddTo(subscriptions);
            Observable.Merge(note, note)
                .ObserveOnCurrentSynchronizationContext()
                .Subscribe(OnNote)
                .AddTo(subscriptions);
        }
        plugin
            .WhereNotNull()
            .Subscribe(OnPlugin)
            .AddTo(subscriptions);

        plugin.Value = new Plugin();
        context.RunAll();
        subscriptions.Clear();
        title.Value = ConnectionState.Connected;
        context.RunAll();

        Assert.Equal(new[] { "plugin:plugin", "title:Disconnected", "note:", "note:" }, calls);
    }

    [Fact]
    public void Config_changed_style_subscribe_with_an_action_variable_and_try_catch_publish()
    {
        var onChanged = new Subject<string>();
        var received = new List<string>();
        Action<string> action = received.Add;

        var subscription = onChanged.Subscribe(action);
        try
        {
            onChanged.OnNext("config");
        }
        catch (Exception)
        {
            Assert.Fail("publishing must not throw");
        }
        subscription.Dispose();

        Assert.Equal(new[] { "config" }, received);
    }

    [Fact]
    public void Connection_state_mirroring_between_two_properties_with_a_lambda_assignment()
    {
        var pluginState = new ReactiveProperty<ConnectionState>(ConnectionState.Disconnected);
        var mirrored = new ReactiveProperty<ConnectionState>(ConnectionState.Disconnected);
        var owner = new CompositeDisposable { mirrored };

        var subscription = pluginState.Subscribe(state => mirrored.Value = state);
        pluginState.Value = ConnectionState.Connected;
        subscription.Dispose();
        pluginState.Value = ConnectionState.Disconnected;

        Assert.Equal(ConnectionState.Connected, mirrored.Value);
        owner.Dispose();
        Assert.True(mirrored.IsDisposed);
    }

    [Fact]
    public void IsConnected_derivation_through_Select_and_ToReadOnlyReactiveProperty()
    {
        var state = new ReactiveProperty<ConnectionState>(ConnectionState.Connected);

        ReadOnlyReactiveProperty<bool> isConnected = state
            .Select(x => x == ConnectionState.Connected)
            .ToReadOnlyReactiveProperty(false);

        Assert.True(isConnected.CurrentValue);
    }

    [Fact]
    public void Target_typed_construction_of_reactive_properties()
    {
        ReactiveProperty<string?> note = new(null);
        ReactiveProperty<ConnectionState> state = new(ConnectionState.Disconnected);
        ReadOnlyReactiveProperty<string?> exposed = note;

        note.Value = "x";

        Assert.Equal("x", exposed.CurrentValue);
        Assert.Equal(ConnectionState.Disconnected, state.CurrentValue);
    }

    [Fact]
    public void Clients_changed_subject_carries_a_read_only_list()
    {
        var manager = new Manager();
        IReadOnlyList<string>? last = null;
        manager.OnClientsChanged.Subscribe(clients => last = clients);

        manager.ClientsChanged(new List<string> { "a", "b" });

        Assert.Equal(new[] { "a", "b" }, last);
    }

    [Fact]
    public void Cancellation_token_helper_form()
    {
        var disposables = new CompositeDisposable();
        var source = new CancellationTokenSource();
        disposables.Add(Disposable.Create(() => source.Cancel()));

        disposables.Dispose();

        Assert.True(source.IsCancellationRequested);
    }

    [Fact]
    public void Cancellation_source_registered_as_a_plain_disposable()
    {
        var disposables = new CompositeDisposable();
        var source = new CancellationTokenSource();
        disposables.Add((IDisposable)source);

        disposables.Dispose();

        Assert.Throws<ObjectDisposedException>(() => source.Token.WaitHandle);
    }

    [Fact]
    public void Unit_is_equal_to_itself()
    {
        Assert.Equal(Unit.Default, new Unit());
        Assert.True(Unit.Default.Equals((object)new Unit()));
        Assert.Equal(0, Unit.Default.GetHashCode());
        Assert.False(Unit.Default.Equals("()"));
    }
}
