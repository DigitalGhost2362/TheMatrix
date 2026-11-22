#nullable enable
using System;
using System.Collections.Generic;

namespace Feeder.Rx
{
    public class ReadOnlyReactiveProperty<T> : Observable<T>, IDisposable
    {
        readonly Subscribers<T> subscribers = new Subscribers<T>();
        readonly object gate = new object();
        T current;
        bool disposed;
        IDisposable? followed;

        internal ReadOnlyReactiveProperty(T value) => current = value;

        public T CurrentValue
        {
            get
            {
                lock (gate)
                    return current;
            }
        }

        public bool IsDisposed
        {
            get
            {
                lock (gate)
                    return disposed;
            }
        }

        internal void Set(T value)
        {
            lock (gate)
            {
                if (disposed || EqualityComparer<T>.Default.Equals(current, value))
                    return;
                current = value;
            }
            subscribers.Publish(value);
        }

        internal void Follow(IDisposable source) => followed = source;

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                    return;
                disposed = true;
            }
            subscribers.Close();
            followed?.Dispose();
        }

        // a value set between the replay and the registration is not delivered to this subscriber
        internal override IDisposable Attach(Action<T> onNext)
        {
            T snapshot;
            lock (gate)
            {
                if (disposed)
                    return Disposable.Empty;
                snapshot = current;
            }
            Safe.Invoke(onNext, snapshot);
            return subscribers.Add(onNext);
        }
    }

    public sealed class ReactiveProperty<T> : ReadOnlyReactiveProperty<T>
    {
        public ReactiveProperty() : base(default!)
        {
        }

        public ReactiveProperty(T value) : base(value)
        {
        }

        public T Value
        {
            get => CurrentValue;
            set => Set(value);
        }
    }

    public static class ReactivePropertyExtensions
    {
        public static ReadOnlyReactiveProperty<T> ToReadOnlyReactiveProperty<T>(this Observable<T> source, T initialValue = default!)
        {
            var property = new ReactiveProperty<T>(initialValue);
            property.Follow(source.Subscribe(value => property.Value = value));
            return property;
        }
    }
}
