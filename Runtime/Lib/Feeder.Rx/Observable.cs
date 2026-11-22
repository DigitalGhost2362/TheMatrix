#nullable enable
using System;

namespace Feeder.Rx
{
    public abstract class Observable<T>
    {
        // sealed hierarchy: every source is a Subject, a ReactiveProperty or an operator over one of them
        internal abstract IDisposable Attach(Action<T> onNext);
    }

    public static class Observable
    {
        public static Observable<T> Empty<T>() => new Anonymous<T>(_ => Disposable.Empty);

        public static Observable<T> Merge<T>(params Observable<T>[] sources)
            => new Anonymous<T>(onNext =>
            {
                var subscriptions = new CompositeDisposable();
                foreach (var source in sources)
                    subscriptions.Add(source.Attach(onNext));
                return subscriptions;
            });
    }

    public static class ObservableSubscribeExtensions
    {
        public static IDisposable Subscribe<T>(this Observable<T> source, Action<T> onNext)
        {
            if (onNext == null)
                throw new ArgumentNullException(nameof(onNext));
            return source.Attach(onNext);
        }
    }

    internal sealed class Anonymous<T> : Observable<T>
    {
        readonly Func<Action<T>, IDisposable> attach;

        public Anonymous(Func<Action<T>, IDisposable> attach) => this.attach = attach;

        internal override IDisposable Attach(Action<T> onNext) => attach(onNext);
    }

    internal static class Safe
    {
        // a throwing subscriber must not reach the producer or the other subscribers
        internal static void Invoke<T>(Action<T> onNext, T value)
        {
            try
            {
                onNext(value);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Feeder.Rx: subscriber threw: " + e);
            }
        }
    }
}
