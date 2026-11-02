#nullable enable

using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Feeder.MCP.Runtime.Utils;
using Feeder.Reflect.Utils;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Feeder.MCP.Editor.Tests.Utils
{
    public sealed class MainThreadTests
    {
        sealed class Counter
        {
            public int Executed;
            public int OffMainThread;
        }

        [Test]
        public void Instance_IsTheEditorQueue()
        {
            Assert.IsInstanceOf<UnityMainThread>(MainThread.Instance);
        }

        [UnityTest]
        public IEnumerator RunAsync_FromManyThreads_RunsEveryCallbackOnTheMainThread()
        {
            const int threads = 16;
            const int callsPerThread = 125;
            var counter = new Counter();
            using var start = new ManualResetEventSlim(false);

            var workers = Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
            {
                start.Wait();
                var calls = new Task[callsPerThread];
                for (var i = 0; i < callsPerThread; i++)
                {
                    calls[i] = MainThread.Instance.RunAsync(() =>
                    {
                        if (!MainThreadDispatcher.IsMainThread)
                            Interlocked.Increment(ref counter.OffMainThread);
                        Interlocked.Increment(ref counter.Executed);
                    });
                }
                return Task.WhenAll(calls);
            })).ToArray();

            start.Set();
            var all = Task.WhenAll(workers);
            var elapsed = Stopwatch.StartNew();
            while (!all.IsCompleted && elapsed.Elapsed < TimeSpan.FromSeconds(10))
                yield return null;

            Assert.IsTrue(all.IsCompleted, $"{threads * callsPerThread - counter.Executed} callbacks never ran");
            Assert.AreEqual(threads * callsPerThread, counter.Executed);
            Assert.AreEqual(0, counter.OffMainThread);
        }

        static async Task<int> AwaitsDelayAndYield()
        {
            await Task.Delay(10);
            await Task.Yield();
            return 42;
        }

        [Test]
        public void Run_TaskOnTheMainThread_ThatAwaitsDelayAndYield_DoesNotDeadlock()
        {
            Assert.IsTrue(MainThreadDispatcher.IsMainThread);

            var elapsed = Stopwatch.StartNew();
            Assert.AreEqual(42, MainThread.Instance.Run(AwaitsDelayAndYield()));
            MainThread.Instance.Run((Task)AwaitsDelayAndYield());

            Assert.Less(elapsed.Elapsed.TotalSeconds, 10);
        }

        [Test]
        public void RunAsync_TaskOffTheMainThread_ReturnsWithoutTheMainThread()
        {
            var result = Task.Run(() => MainThread.Instance.RunAsync(Task.FromResult(7)));

            Assert.IsTrue(result.Wait(TimeSpan.FromSeconds(5)));
            Assert.AreEqual(7, result.Result);
        }
    }
}
