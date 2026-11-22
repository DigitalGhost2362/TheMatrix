#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Feeder.Rx
{
    public static class ObservableExtensions
    {
        public static Observable<T> Where<T>(this Observable<T> source, Func<T, bool> predicate)
            => new Anonymous<T>(onNext => source.Attach(value =>
            {
                if (predicate(value))
                    onNext(value);
            }));

        public static Observable<TResult> Select<T, TResult>(this Observable<T> source, Func<T, TResult> selector)
            => new Anonymous<TResult>(onNext => source.Attach(value => onNext(selector(value))));

        public static Observable<T> WhereNotNull<T>(this Observable<T?> source) where T : class
            => new Anonymous<T>(onNext => source.Attach(value =>
            {
                if (value != null)
                    onNext(value);
            }));

        public static Observable<T> AsObservable<T>(this Observable<T> source)
            => new Anonymous<T>(source.Attach);

        // passes the first value, drops the rest until the window has elapsed, never emits a trailing value
        public static Observable<T> ThrottleFirst<T>(this Observable<T> source, TimeSpan window)
        {
            var windowTicks = (long)(window.TotalSeconds * Stopwatch.Frequency);
            return new Anonymous<T>(onNext =>
            {
                var gate = new object();
                var reopensAt = long.MinValue;
                return source.Attach(value =>
                {
                    lock (gate)
                    {
                        var now = Stopwatch.GetTimestamp();
                        if (now < reopensAt)
                            return;
                        reopensAt = now + windowTicks;
                    }
                    onNext(value);
                });
            });
        }

        // the context is captured when the operator is applied; without one the values go through the thread pool
        public static Observable<T> ObserveOnCurrentSynchronizationContext<T>(this Observable<T> source)
        {
            var context = SynchronizationContext.Current ?? new SynchronizationContext();
            return new Anonymous<T>(onNext =>
            {
                var queue = new Queue<T>();
                var draining = false;
                var disposed = false;

                void Drain(object? _)
                {
                    while (true)
                    {
                        T value;
                        lock (queue)
                        {
                            if (disposed || queue.Count == 0)
                            {
                                queue.Clear();
                                draining = false;
                                return;
                            }
                            value = queue.Dequeue();
                        }
                        Safe.Invoke(onNext, value);
                    }
                }

                var subscription = source.Attach(value =>
                {
                    lock (queue)
                    {
                        if (disposed)
                            return;
                        queue.Enqueue(value);
                        if (draining)
                            return;
                        draining = true;
                    }
                    context.Post(Drain, null);
                });

                return Disposable.Create(() =>
                {
                    lock (queue)
                        disposed = true;
                    subscription.Dispose();
                });
            });
        }
    }
}
