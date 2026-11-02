#if UNITY_EDITOR
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Feeder.Reflect.Utils;
using UnityEditor;

namespace Feeder.MCP.Runtime.Utils
{
    public static class MainThreadInstaller
    {
        [InitializeOnLoadMethod]
        public static void Init()
        {
            EditorApplication.update -= UnityMainThread.Drain;
            EditorApplication.update += UnityMainThread.Drain;
            MainThread.Instance = new UnityMainThread();
        }
    }
    public class UnityMainThread : MainThread
    {
        public const int MainThreadWaitTimeoutMs = 25000;

        static readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
        static readonly Action? pumpSynchronizationContext = CreateSynchronizationContextPump();

        public override bool IsMainThread => MainThreadDispatcher.IsMainThread;

        public override void Run(Task task)
        {
            if (MainThreadDispatcher.IsMainThread)
                WaitPumping(task);
            task.Wait();
        }

        public override T Run<T>(Task<T> task)
        {
            if (MainThreadDispatcher.IsMainThread)
                WaitPumping(task);
            return task.Result;
        }

        public override Task<T> RunAsync<T>(Func<T> func)
            => MainThreadDispatcher.IsMainThread ? Task.FromResult(func()) : Dispatch(func);

        public override Task RunAsync(Action action)
        {
            if (MainThreadDispatcher.IsMainThread)
            {
                action();
                return Task.CompletedTask;
            }
            return Dispatch(() => { action(); return true; });
        }

        public static void Drain()
        {
            while (queue.TryDequeue(out var action))
                action();
        }

        static Task<T> Dispatch<T>(Func<T> body)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Enqueue(() =>
            {
                try { tcs.SetResult(body()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }

        // blocking the main thread must keep running its continuations, or an awaited Task.Delay/Yield never resumes
        static void WaitPumping(Task task)
        {
            var elapsed = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                Drain();
                pumpSynchronizationContext?.Invoke();
                if (((IAsyncResult)task).AsyncWaitHandle.WaitOne(5))
                    return;
                if (elapsed.ElapsedMilliseconds > MainThreadWaitTimeoutMs)
                    throw new TimeoutException($"The task did not complete within {MainThreadWaitTimeoutMs / 1000} s while the Unity main thread was waiting for it. " +
                        "It probably waits for a later Editor frame; run it off the main thread instead.");
            }
        }

        static Action? CreateSynchronizationContextPump()
        {
            var type = typeof(UnityEngine.Object).Assembly.GetType("UnityEngine.UnitySynchronizationContext");
            var execute = type?.GetMethod("ExecuteTasks", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (execute != null && execute.GetParameters().Length == 0)
                return (Action)Delegate.CreateDelegate(typeof(Action), execute);

            var exec = type?.GetMethod("Exec", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null);
            if (exec == null)
                return null;
            return () =>
            {
                var context = SynchronizationContext.Current;
                if (context != null && context.GetType() == type)
                    exec.Invoke(context, null);
            };
        }
    }
}
#endif
