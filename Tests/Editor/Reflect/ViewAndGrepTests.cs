#nullable enable

using System.Linq;
using Feeder.Reflect;
using Feeder.Reflect.Model;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Reflect
{
    public sealed class ViewAndGrepTests
    {
        Reflector _reflector = null!;

        [OneTimeSetUp]
        public void CreateReflector() => _reflector = MatrixPluginEditor.Instance.CreateDefaultReflector();

        static string?[] Names(SerializedMemberList? list) => list?.Select(m => m.name).ToArray() ?? new string?[0];

        [Test]
        public void View_NoQuery_SerializesEverything()
        {
            var view = _reflector.View(new Station())!;

            CollectionAssert.IsSupersetOf(Names(view.fields), new[] { "title", "orbit", "modules", "sister" });
        }

        [Test]
        public void View_MaxDepthZero_KeepsOnlyTheRootEnvelope()
        {
            var view = _reflector.View(new Station(), new ViewQuery { MaxDepth = 0 })!;

            Assert.AreEqual(typeof(Station).GetTypeId(), view.typeName);
            Assert.IsNull(view.fields);
            Assert.IsNull(view.props);
        }

        [Test]
        public void View_MaxDepthOne_StripsGrandchildren()
        {
            var view = _reflector.View(new Station(), new ViewQuery { MaxDepth = 1 })!;
            var orbit = view.GetField("orbit")!;

            Assert.IsNotNull(orbit);
            Assert.IsNull(orbit.fields);
        }

        [Test]
        public void View_Path_SerializesOnlyThatSubtree()
        {
            var view = _reflector.View(new Station(), new ViewQuery { Path = "modules/[1]" })!;

            Assert.AreEqual(typeof(Module).GetTypeId(), view.typeName);
            Assert.AreEqual("lab", view.GetField("label")!.GetValue<string>(_reflector));
        }

        [Test]
        public void View_UnresolvablePath_ReturnsNull()
        {
            Assert.IsNull(_reflector.View(new Station(), new ViewQuery { Path = "orbit/apogee" }));
        }

        [Test]
        public void View_NamePattern_KeepsOnlyBranchesLeadingToAMatch()
        {
            var view = _reflector.View(new Station(), new ViewQuery { NamePattern = "^period$" })!;

            CollectionAssert.AreEqual(new[] { "orbit" }, Names(view.fields));
            CollectionAssert.AreEqual(new[] { "period" }, Names(view.GetField("orbit")!.fields));
        }

        [Test]
        public void View_NamePatternWithoutMatch_ReturnsEmptyEnvelope()
        {
            var view = _reflector.View(new Station(), new ViewQuery { NamePattern = "nothing-here" })!;

            Assert.AreEqual(typeof(Station).GetTypeId(), view.typeName);
            Assert.IsNull(view.fields);
        }

        [Test]
        public void View_TypeFilter_KeepsFloatBranches()
        {
            var view = _reflector.View(new Station(), new ViewQuery { TypeFilter = typeof(float) })!;

            CollectionAssert.AreEqual(new[] { "orbit" }, Names(view.fields));
            CollectionAssert.AreEquivalent(new[] { "radius", "period" }, Names(view.GetField("orbit")!.fields));
        }

        [Test]
        public void View_SelfReference_TerminatesWithAReference()
        {
            var station = new Station();
            station.sister = station;

            var view = _reflector.View(station)!;

            Assert.AreEqual("Reference", view.GetField("sister")!.typeName);
        }

        [Test]
        public void Grep_FindsEveryMatchingMemberWithItsPath()
        {
            var matches = _reflector.Grep(new Station(), "radius");

            CollectionAssert.AreEquivalent(
                new[] { "orbit/radius", "modules/[0]/radius", "modules/[1]/radius" },
                matches.Select(m => m.Path).ToArray());
            Assert.AreEqual(120f, matches.Single(m => m.Path == "orbit/radius").Value.GetValue<float>(_reflector));
        }

        [Test]
        public void Grep_IsCaseInsensitive()
        {
            Assert.AreEqual(3, _reflector.Grep(new Station(), "RADIUS").Count);
        }

        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(2, 3)]
        public void Grep_MaxDepth_LimitsHowDeepItLooks(int maxDepth, int expected)
        {
            Assert.AreEqual(expected, _reflector.Grep(new Station(), "radius", maxDepth).Count);
        }

        [Test]
        public void Grep_SelfReference_VisitsEachObjectOnce()
        {
            var station = new Station();
            station.sister = station;

            var matches = _reflector.Grep(station, "^title$");

            Assert.AreEqual(1, matches.Count);
            Assert.AreEqual("title", matches[0].Path);
        }

        [Test]
        public void Grep_NullTarget_IsEmpty()
        {
            Assert.IsEmpty(_reflector.Grep(null, "radius"));
        }

        [Test]
        public void Grep_InvalidPattern_IsEmptyAndLogsWhy()
        {
            var logs = new Logs();

            Assert.IsEmpty(_reflector.Grep(new Station(), "([", logs: logs));
            StringAssert.Contains("Invalid regex pattern", logs.First().Message);
        }
    }
}
