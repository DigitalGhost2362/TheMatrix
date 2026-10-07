#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace Feeder.Rx
{
    public static class Disposable
    {
        public static readonly IDisposable Empty = new Callback(() => { });

        public static IDisposable Create(Action onDisposed) => new Callback(onDisposed);

        public static T AddTo<T>(this T disposable, ICollection<IDisposable> disposables) where T : IDisposable
        {
            disposables.Add(disposable);
            return disposable;
        }

        sealed class Callback : IDisposable
        {
            Action? onDisposed;

            public Callback(Action onDisposed) => this.onDisposed = onDisposed;

            public void Dispose() => Interlocked.Exchange(ref onDisposed, null)?.Invoke();
        }
    }

    public sealed class CompositeDisposable : ICollection<IDisposable>, IDisposable
    {
        readonly List<IDisposable> items = new List<IDisposable>();
        bool disposed;

        public int Count
        {
            get
            {
                lock (items)
                    return items.Count;
            }
        }

        public bool IsReadOnly => false;

        // an item added after Dispose is disposed at once
        public void Add(IDisposable item)
        {
            lock (items)
            {
                if (!disposed)
                {
                    items.Add(item);
                    return;
                }
            }
            item.Dispose();
        }

        public bool Remove(IDisposable item)
        {
            lock (items)
            {
                if (!items.Remove(item))
                    return false;
            }
            item.Dispose();
            return true;
        }

        public bool Contains(IDisposable item)
        {
            lock (items)
                return items.Contains(item);
        }

        public void CopyTo(IDisposable[] array, int arrayIndex)
        {
            lock (items)
                items.CopyTo(array, arrayIndex);
        }

        // the composite stays usable after Clear
        public void Clear()
        {
            IDisposable[] cleared;
            lock (items)
            {
                cleared = items.ToArray();
                items.Clear();
            }
            foreach (var item in cleared)
                item.Dispose();
        }

        public void Dispose()
        {
            IDisposable[] cleared;
            lock (items)
            {
                if (disposed)
                    return;
                disposed = true;
                cleared = items.ToArray();
                items.Clear();
            }
            foreach (var item in cleared)
                item.Dispose();
        }

        public IEnumerator<IDisposable> GetEnumerator()
        {
            lock (items)
                return ((IEnumerable<IDisposable>)items.ToArray()).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class SerialDisposable : IDisposable
    {
        readonly object gate = new object();
        IDisposable? current;
        bool disposed;

        // replacing the value disposes the previous one
        public IDisposable? Disposable
        {
            get
            {
                lock (gate)
                    return current;
            }
            set
            {
                IDisposable? replaced;
                lock (gate)
                {
                    replaced = disposed ? value : current;
                    if (!disposed)
                        current = value;
                }
                replaced?.Dispose();
            }
        }

        public void Dispose()
        {
            IDisposable? previous;
            lock (gate)
            {
                if (disposed)
                    return;
                disposed = true;
                previous = current;
                current = null;
            }
            previous?.Dispose();
        }
    }
}
