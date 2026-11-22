#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Feeder.Json;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;
using Feeder.ToolHost.Utils;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Utils
{
    public sealed class ParameterNameUtilsTests
    {
        static int _maxEntries;
        static string? _gameObject;

        public static string Capture(int maxEntries = 100, string? gameObject = null)
        {
            _maxEntries = maxEntries;
            _gameObject = gameObject;
            return "ok";
        }

        public static void Twins(int ab, int a_b) { }

        static Dictionary<string, string>? Lookup(string method)
            => ParameterNameUtils.BuildParameterNameLookup(typeof(ParameterNameUtilsTests).GetMethod(method)!.GetParameters());

        [TestCase("maxEntries")]
        [TestCase("MAXENTRIES")]
        [TestCase("max_entries")]
        [TestCase("Max_Entries")]
        [TestCase("max-entries")]
        public void Normalize_AnyCasingOrSeparator_FindsParameter(string incoming)
        {
            Assert.AreEqual("maxEntries", ParameterNameUtils.NormalizeParameterName(incoming, Lookup(nameof(Capture))));
        }

        [Test]
        public void Normalize_UnknownName_IsUnchanged()
        {
            Assert.AreEqual("other_name", ParameterNameUtils.NormalizeParameterName("other_name", Lookup(nameof(Capture))));
        }

        [Test]
        public void Normalize_NamesThatDifferOnlyBySeparator_KeepExactMatchAndSkipSqueeze()
        {
            var lookup = Lookup(nameof(Twins));
            Assert.AreEqual("ab", ParameterNameUtils.NormalizeParameterName("ab", lookup));
            Assert.AreEqual("a_b", ParameterNameUtils.NormalizeParameterName("A_B", lookup));
            Assert.AreEqual("a-b", ParameterNameUtils.NormalizeParameterName("a-b", lookup));
        }

        [Test]
        public void Run_SnakeCaseNames_BindToCamelCaseParameters()
        {
            _maxEntries = 0;
            _gameObject = null;
            var reflector = MatrixPluginEditor.Instance.CreateDefaultReflector();
            var tool = RunTool.CreateFromStaticMethod(reflector, null, "mx-test-snake",
                typeof(ParameterNameUtilsTests).GetMethod(nameof(Capture))!);

            var arguments = new Dictionary<string, JsonElement>
            {
                ["max_entries"] = JsonDocument.Parse("1").RootElement.Clone(),
                ["game_object"] = JsonDocument.Parse("\"Cube\"").RootElement.Clone(),
            };
            var response = tool.Run("snake-1", arguments, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ResponseStatus.Success, response.Status, response.GetMessage());
            Assert.AreEqual(1, _maxEntries, "max_entries was ignored and the default applied");
            Assert.AreEqual("Cube", _gameObject);
        }

        [Test]
        public void Run_SameParameterTwiceAfterNormalizing_IsRejected()
        {
            var reflector = MatrixPluginEditor.Instance.CreateDefaultReflector();
            var tool = RunTool.CreateFromStaticMethod(reflector, null, "mx-test-snake-dup",
                typeof(ParameterNameUtilsTests).GetMethod(nameof(Capture))!);

            var arguments = new Dictionary<string, JsonElement>
            {
                ["max_entries"] = JsonDocument.Parse("1").RootElement.Clone(),
                ["maxEntries"] = JsonDocument.Parse("2").RootElement.Clone(),
            };
            var response = tool.Run("snake-2", arguments, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ResponseStatus.Error, response.Status);
        }
    }
}
