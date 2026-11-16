#nullable enable

using System;
using Feeder.MCP.Editor.API;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class ScriptExecuteCacheTests
    {
        // a unique comment gives every test its own compile-cache entry, so statics never leak between tests
        static string Counter() => "public class Script { static int c; public static int Main() { return ++c; } } // " + Guid.NewGuid().ToString("N");

        static string? Run(string code, bool fresh = false)
        {
            try
            {
                return Tool_Script.Execute(code, freshAssembly: fresh)?.valueJsonElement?.ToString();
            }
            catch (Exception e) when (e.Message.Contains("Compile service failed"))
            {
                Assert.Ignore("Compile service is not reachable: " + e.Message);
                return null;
            }
        }

        [Test]
        public void SameCode_ReusesTheLoadedAssembly_SoStaticsPersist()
        {
            var code = Counter();
            Assert.AreEqual("1", Run(code));
            Assert.AreEqual("2", Run(code));
        }

        [Test]
        public void FreshAssembly_StartsWithEmptyStatics()
        {
            var code = Counter();
            Assert.AreEqual("1", Run(code, fresh: true));
            Assert.AreEqual("1", Run(code, fresh: true));
        }
    }
}
