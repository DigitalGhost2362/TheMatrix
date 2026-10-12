using System;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;

namespace Feeder.Fmp.Tests
{
    internal static class Json
    {
        // keeps non-ASCII and '+' as they are, so the size of a generated payload stays predictable
        public static readonly JsonSerializerOptions Relaxed = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        public static readonly JsonSerializerOptions Web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        public static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutMs, string what)
        {
            var watch = Stopwatch.StartNew();
            while (!await condition().ConfigureAwait(false))
            {
                if (watch.ElapsedMilliseconds > timeoutMs)
                    throw new TimeoutException("Timed out after " + timeoutMs + " ms waiting for " + what + ".");
                await Task.Delay(20).ConfigureAwait(false);
            }
        }

        public static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 10000, string what = "the condition")
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.ElapsedMilliseconds > timeoutMs)
                    throw new TimeoutException("Timed out after " + timeoutMs + " ms waiting for " + what + ".");
                await Task.Delay(20).ConfigureAwait(false);
            }
        }
    }
}
