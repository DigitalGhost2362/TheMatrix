#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Feeder.ToolHost;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Utils
{
    public sealed class RunToolRequestIdTests
    {
        static int _mismatches;

        public static string? Echo(string expected, [RequestID] string? requestId = null)
        {
            if (expected != requestId)
                Interlocked.Increment(ref _mismatches);
            return requestId;
        }

        [Test]
        public void Run_InParallel_InjectsEachCallsOwnRequestId()
        {
            const int workers = 8;
            const int rounds = 200;
            _mismatches = 0;

            var reflector = MatrixPluginEditor.Instance.CreateDefaultReflector();
            var tool = RunTool.CreateFromStaticMethod(reflector, null, "mx-test-echo",
                typeof(RunToolRequestIdTests).GetMethod(nameof(Echo))!);

            var runs = Enumerable.Range(0, workers).Select(worker => Task.Run(async () =>
            {
                var wrong = 0;
                for (var round = 0; round < rounds; round++)
                {
                    var requestId = $"req-{worker}-{round}";
                    var response = await tool.Run(requestId, CancellationToken.None, requestId);
                    if (response.Status != Feeder.ToolHost.Common.Model.ResponseStatus.Success)
                        wrong++;
                }
                return wrong;
            })).ToArray();

            Assert.IsTrue(Task.WaitAll(runs, TimeSpan.FromSeconds(30)), "parallel runs did not finish");
            Assert.AreEqual(0, runs.Sum(r => r.Result), "some runs failed");
            Assert.AreEqual(0, _mismatches, "some runs received another call's RequestID");
        }
    }
}
