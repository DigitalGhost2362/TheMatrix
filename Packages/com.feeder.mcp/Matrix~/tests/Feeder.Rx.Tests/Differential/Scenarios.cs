#nullable enable
using System.Text;
using R3;

namespace Differential.Real;

// compiled twice, against the reference package and against Feeder.Rx; every scenario returns a transcript and the two must be identical
public static class Scenarios
{
    // left out on purpose: use after Dispose, completion and a zero throttle window behave differently in Feeder.Rx
    static readonly Dictionary<string, Func<string>> Table = new()
    {
        ["subject/delivers-to-every-subscriber-in-order"] = SubjectOrder,
        ["subject/late-subscriber-sees-nothing"] = SubjectLate,
        ["subject/equal-values-are-not-suppressed"] = SubjectNoDedup,
        ["subject/throwing-subscriber-is-isolated"] = SubjectIsolation,
        ["subject/subscriber-disposed-mid-dispatch"] = SubjectDisposeOther,
        ["subject/subscriber-disposing-itself"] = SubjectDisposeSelf,
        ["subject/subscriber-added-mid-dispatch"] = SubjectAddMidDispatch,
        ["subject/subject-disposed-mid-dispatch"] = SubjectDisposedMidDispatch,
        ["subject/publish-from-a-subscriber"] = SubjectReentrant,
        ["property/replays-the-current-value"] = PropertyReplay,
        ["property/suppresses-equal-consecutive-values"] = PropertyDedup,
        ["property/null-is-a-value"] = PropertyNulls,
        ["property/struct-and-enum-values"] = PropertyStructs,
        ["property/value-is-set-before-subscribers-run"] = PropertyValueFirst,
        ["property/throwing-replay-is-isolated"] = PropertyThrowingReplay,
        ["property/throwing-subscriber-is-isolated"] = PropertyThrowingSubscriber,
        ["property/subscription-disposed"] = PropertyDisposeSubscription,
        ["property/owned-by-a-composite"] = PropertyOwnedByComposite,
        ["derived/is-connected-pattern"] = DerivedIsConnected,
        ["derived/initial-value-until-the-source-emits"] = DerivedInitialValue,
        ["derived/dispose-detaches-from-the-source"] = DerivedDispose,
        ["derived/suppresses-repeated-results"] = DerivedDedup,
        ["operators/where"] = OperatorWhere,
        ["operators/where-chain"] = OperatorWhereChain,
        ["operators/select"] = OperatorSelect,
        ["operators/where-not-null"] = OperatorWhereNotNull,
        ["operators/where-not-null-on-a-property"] = OperatorWhereNotNullProperty,
        ["operators/as-observable"] = OperatorAsObservable,
        ["operators/as-observable-of-a-property-replays"] = OperatorAsObservableProperty,
        ["operators/selector-that-throws"] = OperatorSelectorThrows,
        ["operators/merge-subjects"] = MergeSubjects,
        ["operators/merge-properties-replay-in-argument-order"] = MergeProperties,
        ["operators/merge-dispose-detaches-everything"] = MergeDispose,
        ["operators/empty"] = EmptyObservable,
        ["operators/throttle-first-window"] = ThrottleWindow,
        ["operators/throttle-first-per-subscription"] = ThrottlePerSubscription,
        ["observe-on/posts-once-and-keeps-order"] = ObserveOnOrder,
        ["observe-on/posts-again-after-a-drain"] = ObserveOnAgain,
        ["observe-on/context-is-captured-when-applied"] = ObserveOnCapture,
        ["observe-on/replay-goes-through-the-context"] = ObserveOnReplay,
        ["observe-on/throwing-subscriber-keeps-draining"] = ObserveOnThrowing,
        ["observe-on/disposed-before-the-drain"] = ObserveOnDisposed,
        ["observe-on/subscriber-disposing-itself"] = ObserveOnSelfDispose,
        ["observe-on/chained-with-where"] = ObserveOnChain,
        ["disposables/create-runs-once"] = DisposableCreate,
        ["disposables/composite-add-clear-dispose"] = CompositeBasics,
        ["disposables/composite-add-after-dispose"] = CompositeAddAfterDispose,
        ["disposables/composite-remove"] = CompositeRemove,
        ["disposables/composite-as-a-collection"] = CompositeCollection,
        ["disposables/add-to"] = AddToCollection,
        ["disposables/serial"] = SerialBasics,
        ["disposables/serial-after-dispose"] = SerialAfterDispose,
        ["unit/equality"] = UnitEquality,
    };

    public static IEnumerable<string> Names => Table.Keys;

    public static string LibraryNamespace => typeof(Subject<int>).Namespace!;

    public static string Run(string name) => Table[name]();

    sealed class Transcript
    {
        readonly StringBuilder text = new();

        public void Line(string line) => text.Append(line).Append('\n');

        public void Values<T>(string label, IEnumerable<T> values)
            => Line(label + ": " + string.Join(",", values.Select(value => value == null ? "<null>" : value.ToString())));

        public override string ToString() => text.ToString();
    }

    sealed class Disposed : IDisposable
    {
        readonly Transcript transcript;
        readonly string name;

        public Disposed(string name, Transcript transcript)
        {
            this.name = name;
            this.transcript = transcript;
        }

        public void Dispose() => transcript.Line(name + ".Dispose");
    }

    // stands in for the UI thread: Post only queues, the scenario decides when it runs
    sealed class Pump : SynchronizationContext
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

        public T Apply<T>(Func<T> operatorCall)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                return operatorCall();
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }
    }

    enum State
    {
        Disconnected,
        Connecting,
        Connected,
    }

    static string Outcome(Action action)
    {
        try
        {
            action();
            return "ok";
        }
        catch (Exception e)
        {
            return "threw " + e.GetType().Name;
        }
    }

    static string SubjectOrder()
    {
        var t = new Transcript();
        var subject = new Subject<int>();
        subject.Subscribe(value => t.Line("a" + value));
        subject.Subscribe(value => t.Line("b" + value));
        subject.OnNext(1);
        subject.OnNext(2);
        return t.ToString();
    }

    static string SubjectLate()
    {
        var t = new Transcript();
        var subject = new Subject<int>();
        subject.OnNext(1);
        subject.Subscribe(value => t.Line("late " + value));
        subject.OnNext(2);
        return t.ToString();
    }

    static string SubjectNoDedup()
    {
        var t = new Transcript();
        var subject = new Subject<int>();
        subject.Subscribe(value => t.Line("got " + value));
        subject.OnNext(7);
        subject.OnNext(7);
        return t.ToString();
    }

    static string SubjectIsolation()
    {
        var t = new Transcript();
        var subject = new Subject<int>();
        subject.Subscribe(value =>
        {
            if (value == 2)
                throw new InvalidOperationException("boom");
            t.Line("first " + value);
        });
        subject.Subscribe(value => t.Line("second " + value));
        t.Line(Outcome(() => subject.OnNext(1)));
        t.Line(Outcome(() => subject.OnNext(2)));
        t.Line(Outcome(() => subject.OnNext(3)));
        return t.ToString();
    }

    static string SubjectDisposeOther()
    {
        var t = new Transcript();
        var subject = new Subject<int>();
        IDisposable? second = null;
        subject.Subscribe(value =>
        {
            t.Line("first " + value);
            second!.Dispose();
        });
        second = subject.Subscribe(value => t.Line("second " + value));
        subject.OnNext(1);
        subject.OnNext(2);
        return t.ToString();
    }

    static string SubjectDisposeSelf()
    {
        var t = new Transcript();
        var subject = new Subject<int>();
        IDisposable? self = null;
        self = subject.Subscribe(value =>
        {
            t.Line("self " + value);
            self!.Dispose();
        });
        subject.Subscribe(value => t.Line("other " + value));
        subject.OnNext(1);
        subject.OnNext(2);
        return t.ToString();
    }

    static string SubjectAddMidDispatch()
    {
        var t = new Transcript();
        var subject = new Subject<int>();
        var added = false;
        subject.Subscribe(value =>
        {
            t.Line("first " + value);
            if (added)
                return;
            added = true;
            subject.Subscribe(late => t.Line("added " + late));
        });
        subject.OnNext(1);
        subject.OnNext(2);
        return t.ToString();
    }

    static string SubjectDisposedMidDispatch()
    {
        var t = new Transcript();
        var subject = new Subject<int>();
        subject.Subscribe(value =>
        {
            t.Line("first " + value);
            subject.Dispose();
        });
        subject.Subscribe(value => t.Line("second " + value));
        subject.OnNext(1);
        return t.ToString();
    }

    static string SubjectReentrant()
    {
        var t = new Transcript();
        var subject = new Subject<int>();
        subject.Subscribe(value =>
        {
            if (value < 3)
                subject.OnNext(value + 1);
        });
        subject.Subscribe(value => t.Line("saw " + value));
        subject.OnNext(1);
        return t.ToString();
    }

    static string PropertyReplay()
    {
        var t = new Transcript();
        var property = new ReactiveProperty<int>(1);
        t.Line("before subscribe");
        property.Subscribe(value => t.Line("first " + value));
        t.Line("after subscribe");
        property.Value = 2;
        property.Value = 3;
        property.Subscribe(value => t.Line("late " + value));
        property.Value = 4;
        t.Line("current " + property.CurrentValue + " value " + property.Value);
        return t.ToString();
    }

    static string PropertyDedup()
    {
        var t = new Transcript();
        var property = new ReactiveProperty<int>(0);
        var received = new List<int>();
        property.Subscribe(received.Add);
        foreach (var value in new[] { 1, 1, 2, 1, 1, 0, 0 })
            property.Value = value;
        t.Values("received", received);
        return t.ToString();
    }

    static string PropertyNulls()
    {
        var t = new Transcript();
        var property = new ReactiveProperty<string?>(null);
        var received = new List<string?>();
        property.Subscribe(received.Add);
        foreach (var value in new[] { null, "a", null, null, "a", "a" })
            property.Value = value;
        t.Values("received", received);
        return t.ToString();
    }

    static string PropertyStructs()
    {
        var t = new Transcript();
        var states = new ReactiveProperty<State>(State.Disconnected);
        var pairs = new ReactiveProperty<(int, string)>((1, "a"));
        var units = new ReactiveProperty<Unit>(default);
        var received = new List<string>();
        states.Subscribe(state => received.Add("state " + state));
        pairs.Subscribe(pair => received.Add("pair " + pair));
        units.Subscribe(unit => received.Add("unit"));
        states.Value = State.Connected;
        states.Value = State.Connected;
        pairs.Value = (1, "a");
        pairs.Value = (2, "a");
        units.Value = Unit.Default;
        t.Values("received", received);
        return t.ToString();
    }

    static string PropertyValueFirst()
    {
        var t = new Transcript();
        var property = new ReactiveProperty<int>(0);
        property.Subscribe(value => t.Line("sees " + value + " current " + property.CurrentValue));
        property.Value = 5;
        return t.ToString();
    }

    static string PropertyThrowingReplay()
    {
        var t = new Transcript();
        var property = new ReactiveProperty<int>(0);
        var calls = 0;
        t.Line(Outcome(() => property.Subscribe(value =>
        {
            calls++;
            throw new InvalidOperationException("replay failed");
        })));
        property.Value = 1;
        t.Line("calls " + calls);
        return t.ToString();
    }

    static string PropertyThrowingSubscriber()
    {
        var t = new Transcript();
        var property = new ReactiveProperty<int>(0);
        var armed = false;
        property.Subscribe(value =>
        {
            if (armed)
                throw new InvalidOperationException("boom");
        });
        property.Subscribe(value => t.Line("other " + value));
        armed = true;
        t.Line(Outcome(() => property.Value = 1));
        t.Line("value " + property.Value);
        return t.ToString();
    }

    static string PropertyDisposeSubscription()
    {
        var t = new Transcript();
        var property = new ReactiveProperty<int>(0);
        var subscription = property.Subscribe(value => t.Line("got " + value));
        property.Value = 1;
        subscription.Dispose();
        subscription.Dispose();
        property.Value = 2;
        return t.ToString();
    }

    static string PropertyOwnedByComposite()
    {
        var t = new Transcript();
        var owner = new CompositeDisposable();
        var property = new ReactiveProperty<int>(1);
        owner.Add(property);
        property.Subscribe(value => t.Line("got " + value));
        owner.Dispose();
        t.Line("disposed " + property.IsDisposed);
        return t.ToString();
    }

    static string DerivedIsConnected()
    {
        var t = new Transcript();
        var state = new ReactiveProperty<State>(State.Connected);
        var isConnected = state.Select(x => x == State.Connected).ToReadOnlyReactiveProperty(false);
        t.Line("initial " + isConnected.CurrentValue);
        var received = new List<bool>();
        isConnected.Subscribe(received.Add);
        state.Value = State.Connecting;
        state.Value = State.Connected;
        state.Value = State.Disconnected;
        t.Values("received", received);
        t.Line("final " + isConnected.CurrentValue);
        return t.ToString();
    }

    static string DerivedInitialValue()
    {
        var t = new Transcript();
        var source = new Subject<int>();
        var property = source.ToReadOnlyReactiveProperty(-1);
        t.Line("before " + property.CurrentValue);
        source.OnNext(4);
        t.Line("after " + property.CurrentValue);
        return t.ToString();
    }

    static string DerivedDispose()
    {
        var t = new Transcript();
        var source = new Subject<int>();
        var property = source.ToReadOnlyReactiveProperty(0);
        source.OnNext(1);
        property.Dispose();
        source.OnNext(2);
        t.Line("current " + property.CurrentValue);
        return t.ToString();
    }

    static string DerivedDedup()
    {
        var t = new Transcript();
        var source = new Subject<int>();
        var parity = source.Select(value => value % 2).ToReadOnlyReactiveProperty(-1);
        var received = new List<int>();
        parity.Subscribe(received.Add);
        foreach (var value in new[] { 2, 4, 5, 7, 8 })
            source.OnNext(value);
        t.Values("received", received);
        return t.ToString();
    }

    static string OperatorWhere()
    {
        var t = new Transcript();
        var subject = new Subject<int>();
        var received = new List<int>();
        subject.Where(value => value % 2 == 0).Subscribe(received.Add);
        for (var i = 1; i <= 6; i++)
            subject.OnNext(i);
        t.Values("received", received);
        return t.ToString();
    }

    static string OperatorWhereChain()
    {
        var t = new Transcript();
        var subject = new Subject<int>();
        var received = new List<int>();
        subject.Where(value => value > 1).Where(value => value < 4).Subscribe(received.Add);
        for (var i = 0; i <= 5; i++)
            subject.OnNext(i);
        t.Values("received", received);
        return t.ToString();
    }

    static string OperatorSelect()
    {
        var t = new Transcript();
        var subject = new Subject<int>();
        var received = new List<string>();
        subject.Where(value => value > 0).Select(value => "v" + value).Subscribe(received.Add);
        subject.OnNext(0);
        subject.OnNext(1);
        subject.OnNext(2);
        t.Values("received", received);
        return t.ToString();
    }

    static string OperatorWhereNotNull()
    {
        var t = new Transcript();
        var subject = new Subject<string?>();
        var received = new List<string>();
        subject.WhereNotNull().Subscribe(received.Add);
        subject.OnNext("a");
        subject.OnNext(null);
        subject.OnNext("b");
        t.Values("received", received);
        return t.ToString();
    }

    static string OperatorWhereNotNullProperty()
    {
        var t = new Transcript();
        var present = new ReactiveProperty<string?>("x");
        var absent = new ReactiveProperty<string?>(null);
        var received = new List<string>();
        present.WhereNotNull().Subscribe(received.Add);
        absent.WhereNotNull().Subscribe(received.Add);
        absent.Value = "y";
        t.Values("received", received);
        return t.ToString();
    }

    static string OperatorAsObservable()
    {
        var t = new Transcript();
        var subject = new Subject<int>();
        var exposed = subject.AsObservable();
        var subscription = exposed.Subscribe(value => t.Line("got " + value));
        subject.OnNext(1);
        subscription.Dispose();
        subject.OnNext(2);
        t.Line("still a subject = " + (exposed is Subject<int>));
        return t.ToString();
    }

    static string OperatorAsObservableProperty()
    {
        var t = new Transcript();
        var property = new ReactiveProperty<int>(8);
        property.AsObservable().Subscribe(value => t.Line("got " + value));
        property.Value = 9;
        return t.ToString();
    }

    static string OperatorSelectorThrows()
    {
        var t = new Transcript();
        var subject = new Subject<int>();
        subject.Select(value => value == 2 ? throw new InvalidOperationException("selector failed") : value * 10)
            .Subscribe(value => t.Line("mapped " + value));
        subject.Subscribe(value => t.Line("plain " + value));
        t.Line(Outcome(() => subject.OnNext(1)));
        t.Line(Outcome(() => subject.OnNext(2)));
        t.Line(Outcome(() => subject.OnNext(3)));
        return t.ToString();
    }

    static string MergeSubjects()
    {
        var t = new Transcript();
        var a = new Subject<int>();
        var b = new Subject<int>();
        var received = new List<int>();
        Observable.Merge(a, b).Subscribe(received.Add);
        a.OnNext(1);
        b.OnNext(2);
        a.OnNext(3);
        t.Values("received", received);
        return t.ToString();
    }

    static string MergeProperties()
    {
        var t = new Transcript();
        var pending = new ReactiveProperty<string?>("pending");
        var fault = new ReactiveProperty<string?>(null);
        var moved = new ReactiveProperty<string?>("moved");
        var received = new List<string?>();
        Observable.Merge(pending, fault, moved).Subscribe(received.Add);
        fault.Value = "fault";
        t.Values("received", received);
        return t.ToString();
    }

    static string MergeDispose()
    {
        var t = new Transcript();
        var a = new Subject<int>();
        var b = new Subject<int>();
        var received = new List<int>();
        var subscription = Observable.Merge(a, b).Subscribe(received.Add);
        a.OnNext(1);
        subscription.Dispose();
        a.OnNext(2);
        b.OnNext(3);
        t.Values("received", received);
        return t.ToString();
    }

    static string EmptyObservable()
    {
        var t = new Transcript();
        var received = new List<Unit>();
        var subscription = Observable.Empty<Unit>().Subscribe(received.Add);
        subscription.Dispose();
        t.Line("received " + received.Count);
        return t.ToString();
    }

    static string ThrottleWindow()
    {
        var t = new Transcript();
        var subject = new Subject<int>();
        var received = new List<int>();
        subject.ThrottleFirst(TimeSpan.FromMilliseconds(150)).Subscribe(received.Add);
        subject.OnNext(1);
        subject.OnNext(2);
        subject.OnNext(3);
        t.Values("inside the window", received);
        Thread.Sleep(500);
        subject.OnNext(4);
        subject.OnNext(5);
        t.Values("after the window", received);
        Thread.Sleep(500);
        t.Values("no trailing value", received);
        return t.ToString();
    }

    static string ThrottlePerSubscription()
    {
        var t = new Transcript();
        var subject = new Subject<int>();
        var throttled = subject.ThrottleFirst(TimeSpan.FromMilliseconds(500));
        var first = new List<int>();
        var second = new List<int>();
        throttled.Subscribe(first.Add);
        subject.OnNext(1);
        throttled.Subscribe(second.Add);
        subject.OnNext(2);
        t.Values("first", first);
        t.Values("second", second);
        return t.ToString();
    }

    static string ObserveOnOrder()
    {
        var t = new Transcript();
        var pump = new Pump();
        var subject = new Subject<int>();
        var received = new List<int>();
        pump.Apply(() => subject.ObserveOnCurrentSynchronizationContext()).Subscribe(received.Add);
        subject.OnNext(1);
        subject.OnNext(2);
        subject.OnNext(3);
        t.Values("before the pump", received);
        t.Line("posts " + pump.Pending);
        pump.RunAll();
        t.Values("after the pump", received);
        t.Line("posts " + pump.Pending);
        return t.ToString();
    }

    static string ObserveOnAgain()
    {
        var t = new Transcript();
        var pump = new Pump();
        var subject = new Subject<int>();
        var received = new List<int>();
        pump.Apply(() => subject.ObserveOnCurrentSynchronizationContext()).Subscribe(received.Add);
        subject.OnNext(1);
        pump.RunAll();
        subject.OnNext(2);
        t.Line("posts " + pump.Pending);
        pump.RunAll();
        t.Values("received", received);
        return t.ToString();
    }

    static string ObserveOnCapture()
    {
        var t = new Transcript();
        var applied = new Pump();
        var subscribed = new Pump();
        var subject = new Subject<int>();
        var chain = applied.Apply(() => subject.ObserveOnCurrentSynchronizationContext());
        subscribed.Apply(() => chain.Subscribe(value => t.Line("got " + value)));
        subject.OnNext(1);
        t.Line("applied " + applied.Pending + " subscribed " + subscribed.Pending);
        return t.ToString();
    }

    static string ObserveOnReplay()
    {
        var t = new Transcript();
        var pump = new Pump();
        var property = new ReactiveProperty<int>(5);
        var received = new List<int>();
        pump.Apply(() => property.ObserveOnCurrentSynchronizationContext()).Subscribe(received.Add);
        t.Values("before the pump", received);
        pump.RunAll();
        t.Values("after the pump", received);
        return t.ToString();
    }

    static string ObserveOnThrowing()
    {
        var t = new Transcript();
        var pump = new Pump();
        var subject = new Subject<int>();
        var received = new List<int>();
        pump.Apply(() => subject.ObserveOnCurrentSynchronizationContext()).Subscribe(value =>
        {
            if (value == 2)
                throw new InvalidOperationException("ui failed");
            received.Add(value);
        });
        subject.OnNext(1);
        subject.OnNext(2);
        subject.OnNext(3);
        t.Line(Outcome(pump.RunAll));
        subject.OnNext(4);
        t.Line(Outcome(pump.RunAll));
        t.Values("received", received);
        return t.ToString();
    }

    static string ObserveOnDisposed()
    {
        var t = new Transcript();
        var pump = new Pump();
        var subject = new Subject<int>();
        var received = new List<int>();
        var subscription = pump.Apply(() => subject.ObserveOnCurrentSynchronizationContext()).Subscribe(received.Add);
        subject.OnNext(1);
        subscription.Dispose();
        pump.RunAll();
        subject.OnNext(2);
        pump.RunAll();
        t.Values("received", received);
        return t.ToString();
    }

    static string ObserveOnSelfDispose()
    {
        var t = new Transcript();
        var pump = new Pump();
        var subject = new Subject<int>();
        var received = new List<int>();
        IDisposable? subscription = null;
        subscription = pump.Apply(() => subject.ObserveOnCurrentSynchronizationContext()).Subscribe(value =>
        {
            received.Add(value);
            subscription!.Dispose();
        });
        subject.OnNext(1);
        subject.OnNext(2);
        pump.RunAll();
        t.Values("received", received);
        return t.ToString();
    }

    static string ObserveOnChain()
    {
        var t = new Transcript();
        var pump = new Pump();
        var property = new ReactiveProperty<string?>(null);
        var received = new List<string>();
        pump.Apply(() => property.ObserveOnCurrentSynchronizationContext()).Where(value => value != null).Select(value => value!.ToUpperInvariant()).Subscribe(received.Add);
        property.Value = "a";
        property.Value = "b";
        pump.RunAll();
        t.Values("received", received);
        return t.ToString();
    }

    static string DisposableCreate()
    {
        var t = new Transcript();
        var disposable = Disposable.Create(() => t.Line("disposed"));
        disposable.Dispose();
        disposable.Dispose();
        return t.ToString();
    }

    static string CompositeBasics()
    {
        var t = new Transcript();
        var composite = new CompositeDisposable();
        composite.Add(new Disposed("a", t));
        composite.Add(new Disposed("b", t));
        t.Line("count " + composite.Count);
        composite.Clear();
        t.Line("count after clear " + composite.Count);
        composite.Add(new Disposed("c", t));
        composite.Dispose();
        composite.Dispose();
        t.Line("count after dispose " + composite.Count);
        return t.ToString();
    }

    static string CompositeAddAfterDispose()
    {
        var t = new Transcript();
        var composite = new CompositeDisposable();
        composite.Dispose();
        composite.Add(new Disposed("late", t));
        t.Line("count " + composite.Count);
        return t.ToString();
    }

    static string CompositeRemove()
    {
        var t = new Transcript();
        var composite = new CompositeDisposable();
        var item = new Disposed("item", t);
        composite.Add(item);
        t.Line("first remove " + composite.Remove(item));
        t.Line("second remove " + composite.Remove(item));
        composite.Dispose();
        return t.ToString();
    }

    static string CompositeCollection()
    {
        var t = new Transcript();
        var composite = new CompositeDisposable();
        var a = new Disposed("a", t);
        var b = new Disposed("b", t);
        ICollection<IDisposable> collection = composite;
        collection.Add(a);
        collection.Add(b);
        var copy = new IDisposable[2];
        collection.CopyTo(copy, 0);
        t.Line("read only " + collection.IsReadOnly);
        t.Line("contains a " + collection.Contains(a));
        t.Line("count " + collection.Count);
        t.Line("copy " + (copy[0] == a) + " " + (copy[1] == b));
        var enumerated = 0;
        foreach (var item in collection)
            enumerated++;
        t.Line("enumerated " + enumerated);
        return t.ToString();
    }

    static string AddToCollection()
    {
        var t = new Transcript();
        var composite = new CompositeDisposable();
        var item = new Disposed("item", t);
        var returned = item.AddTo(composite);
        t.Line("returned the same = " + ReferenceEquals(returned, item));
        var subject = new Subject<int>();
        subject.Subscribe(value => t.Line("got " + value)).AddTo(composite);
        subject.OnNext(1);
        composite.Dispose();
        subject.OnNext(2);
        return t.ToString();
    }

    static string SerialBasics()
    {
        var t = new Transcript();
        var serial = new SerialDisposable();
        serial.Disposable = new Disposed("first", t);
        serial.Disposable = new Disposed("second", t);
        t.Line("current is second = " + ((serial.Disposable as Disposed) != null));
        serial.Disposable = null;
        serial.Disposable = new Disposed("third", t);
        serial.Dispose();
        serial.Dispose();
        return t.ToString();
    }

    static string SerialAfterDispose()
    {
        var t = new Transcript();
        var serial = new SerialDisposable();
        serial.Dispose();
        serial.Disposable = new Disposed("late", t);
        return t.ToString();
    }

    static string UnitEquality()
    {
        var t = new Transcript();
        t.Line($"{Unit.Default.Equals(new Unit())} {Unit.Default.Equals((object)new Unit())} {Unit.Default.Equals("x")} {Unit.Default.GetHashCode()}");
        t.Line("equals the default comparer = " + EqualityComparer<Unit>.Default.Equals(Unit.Default, default));
        return t.ToString();
    }
}
