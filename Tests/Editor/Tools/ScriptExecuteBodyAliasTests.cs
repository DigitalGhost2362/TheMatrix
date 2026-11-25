#nullable enable

using System;
using Feeder.MCP.Editor.API;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class ScriptExecuteBodyAliasTests
    {
        static string? RunBody(string body)
        {
            try
            {
                return Tool_Script.Execute(body + " // " + Guid.NewGuid().ToString("N"), isMethodBody: true)?.valueJsonElement?.ToString();
            }
            catch (Exception e) when (e.Message.Contains("Compile service failed"))
            {
                Assert.Ignore("Compile service is not reachable: " + e.Message);
                return null;
            }
        }

        [Test]
        public void GeneratedHeader_AliasesObjectAndRandomToUnityEngine()
        {
            var code = Tool_Script.GenerateFullCode("return 1;", "Script", "Main", null, returnsValue: true);
            StringAssert.Contains("using Object = UnityEngine.Object;", code);
            StringAssert.Contains("using Random = UnityEngine.Random;", code);
        }

        [Test]
        public void BareObjectAndRandom_CompileInBodyMode()
        {
            var result = RunBody("var all = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None); return all.Length + Random.Range(0, 1);");
            Assert.That(int.TryParse(result, out var count) && count >= 0, $"expected a number, got '{result}'");
        }

        [Test]
        public void SystemRandom_StillCompilesInBodyMode()
        {
            Assert.AreEqual("3", RunBody("System.Random r = new System.Random(1); return r.Next(3, 4);"));
        }
    }
}
