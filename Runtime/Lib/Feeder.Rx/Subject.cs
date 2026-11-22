#nullable enable
using System;
using System.Threading;

namespace Feeder.Rx
{
    public class Subject<T> : Observable<T>, IDisposable
    {
        readonly Subscribers<T> subscribers = new Subscribers<T>();

        public void OnNext(T value) => subscribers.Publish(value);

        public void Dispose() => subscribers.Close();

        internal override IDisposable Attach(Action<T> onNext) => subscribers.Add(onNext);
    }

    // copy-on-write list: Publish never blocks Add or Remove and skips a subscriber disposed mid-dispatch
    internal sealed class Subscribers<T>
    {
        sealed class Entry : IDisposable
        {
            readonly Subscribers<T> owner;
            public readonly Action<T> OnNext;
            public volatile bool IsDisposed;

            public Entry(Subscribers<T> owner, Action<T> onNext)
            {
                this.owner = owner;
                OnNext = onNext;
            }

            public void Dispose()
            {
                IsDisposed = true;
                owner.Remove(this);
            }
        }

        readonly object gate = new object();
        Entry[] entries = Array.Empty<Entry>();
        bool closed;

        public IDisposable Add(Action<T> onNext)
        {
            var entry = new Entry(this, onNext);
            lock (gate)
            {
                if (closed)
                    return Disposable.Empty;
                var grown = new Entry[entries.Length + 1];
                entries.CopyTo(grown, 0);
                grown[entries.Length] = entry;
                Volatile.Write(ref entries, grown);
            }
            return entry;
        }

        void Remove(Entry entry)
        {
            lock (gate)
            {
                var index = Array.IndexOf(entries, entry);
                if (index < 0)
                    return;
                var shrunk = new Entry[entries.Length - 1];
                Array.Copy(entries, 0, shrunk, 0, index);
                Array.Copy(entries, index + 1, shrunk, index, shrunk.Length - index);
                Volatile.Write(ref entries, shrunk);
            }
        }

        public void Publish(T value)
        {
            foreach (var entry in Volatile.Read(ref entries))
            {
                if (!entry.IsDisposed)
                    Safe.Invoke(entry.OnNext, value);
            }
        }

        public void Close()
        {
            Entry[] closing;
            lock (gate)
            {
                closed = true;
                closing = entries;
                Volatile.Write(ref entries, Array.Empty<Entry>());
            }
            foreach (var entry in closing)
                entry.IsDisposed = true;
        }
    }
}
