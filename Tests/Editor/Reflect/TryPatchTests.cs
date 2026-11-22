#nullable enable

using System.Linq;
using Feeder.Json;
using Feeder.Reflect;
using Feeder.Reflect.Model;
using NUnit.Framework;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests.Reflect
{
    public sealed class TryPatchTests
    {
        Reflector _reflector = null!;
        GameObject? _go;

        [OneTimeSetUp]
        public void CreateReflector() => _reflector = MatrixPluginEditor.Instance.CreateDefaultReflector();

        [TearDown]
        public void DestroyObjects()
        {
            if (_go != null)
                UnityEngine.Object.DestroyImmediate(_go);
        }

        bool Patch(object target, string json, Logs? logs = null)
        {
            object? obj = target;
            return _reflector.TryPatch(ref obj, json, logs: logs);
        }

        [Test]
        public void SeveralTopLevelFields_AreAllApplied()
        {
            var ship = new Ship();

            Assert.IsTrue(Patch(ship, "{\"crew\":9,\"name\":\"Vega\",\"hull\":\"Wood\"}"));
            Assert.AreEqual(9, ship.crew);
            Assert.AreEqual("Vega", ship.name);
            Assert.AreEqual(Hull.Wood, ship.hull);
        }

        [Test]
        public void PartialUnityStruct_KeepsUnmentionedAxes()
        {
            var ship = new Ship();

            Assert.IsTrue(Patch(ship, "{\"position\":{\"y\":20}}"));
            Assert.AreEqual(new Vector3(1, 20, 3), ship.position);
        }

        [Test]
        public void PartialNestedStruct_KeepsSiblingFields()
        {
            var ship = new Ship();

            Assert.IsTrue(Patch(ship, "{\"engine\":{\"fuel\":{\"liters\":5}}}"));
            Assert.AreEqual(5, ship.engine.fuel.liters);
            Assert.AreEqual(10f, ship.engine.thrust);
        }

        [Test]
        public void ListElement_IsPatchedInPlace()
        {
            var ship = new Ship();
            var second = ship.holds[1];

            Assert.IsTrue(Patch(ship, "{\"holds\":{\"[1]\":{\"mass\":9.5}}}"));
            Assert.AreSame(second, ship.holds[1]);
            Assert.AreEqual(9.5f, second.mass);
            Assert.AreEqual("gas", second.label);
        }

        [Test]
        public void ArrayElementAndDictionaryEntry_ArePatchedTogether()
        {
            var ship = new Ship();

            Assert.IsTrue(Patch(ship, "{\"codes\":{\"[0]\":70},\"stock\":{\"[iron]\":11,\"[gold]\":2}}"));
            CollectionAssert.AreEqual(new[] { 70, 8, 9 }, ship.codes);
            Assert.AreEqual(11, ship.stock["iron"]);
            Assert.AreEqual(2, ship.stock["gold"]);
        }

        [Test]
        public void NullValue_ClearsReferenceField()
        {
            var ship = new Ship();

            Assert.IsTrue(Patch(ship, "{\"cargo\":null}"));
            Assert.IsNull(ship.cargo);
        }

        [Test]
        public void NullValue_OnValueType_Fails()
        {
            var ship = new Ship();

            Assert.IsFalse(Patch(ship, "{\"crew\":null}"));
            Assert.AreEqual(3, ship.crew);
        }

        [Test]
        public void TypeHint_ReplacesWithDerivedType()
        {
            var ship = new Ship();
            var json = "{\"cargo\":{\"$type\":\"" + typeof(HeavyCargo).FullName + "\",\"containers\":3,\"label\":\"steel\"}}";

            Assert.IsTrue(Patch(ship, json));
            var heavy = ship.cargo as HeavyCargo;
            Assert.IsNotNull(heavy);
            Assert.AreEqual(3, heavy!.containers);
            Assert.AreEqual("steel", heavy.label);
        }

        [Test]
        public void TypeHint_NotAssignable_Fails()
        {
            var ship = new Ship();
            var logs = new Logs();
            var json = "{\"cargo\":{\"$type\":\"" + typeof(Module).FullName + "\",\"label\":\"x\"}}";

            Patch(ship, json, logs);

            Assert.IsNotInstanceOf<Module>((object?)ship.cargo);
            Assert.IsTrue(logs.Any(l => l.Message.Contains("not assignable")));
        }

        [Test]
        public void NullReferenceField_IsCreatedFromObjectPatch()
        {
            var ship = new Ship { cargo = null };

            Assert.IsTrue(Patch(ship, "{\"cargo\":{\"label\":\"wool\",\"mass\":3}}"));
            Assert.AreEqual("wool", ship.cargo!.label);
            Assert.AreEqual(3f, ship.cargo.mass);
        }

        [Test]
        public void InvalidJson_FailsWithParseError()
        {
            var ship = new Ship();
            var logs = new Logs();

            Assert.IsFalse(Patch(ship, "{\"crew\":", logs));
            StringAssert.Contains("Failed to parse JSON patch", logs.First().Message);
            Assert.AreEqual(3, ship.crew);
        }

        [Test]
        public void UnknownMember_FailsButStillAppliesTheRest()
        {
            var ship = new Ship();
            var logs = new Logs();

            Assert.IsFalse(Patch(ship, "{\"crew\":5,\"warp\":1}", logs));
            Assert.AreEqual(5, ship.crew);
            Assert.IsTrue(logs.Any(l => l.Message.Contains("'warp'")));
        }

        [Test]
        public void JsonElementOverload_BehavesLikeString()
        {
            var ship = new Ship();
            object? obj = ship;
            using var doc = JsonDocument.Parse("{\"Speed\":12.5}");

            Assert.IsTrue(_reflector.TryPatch(ref obj, doc.RootElement));
            Assert.AreEqual(12.5f, ship.Speed);
        }

        [Test]
        public void TransformProperty_PartialPatch_KeepsOtherAxes()
        {
            _go = new GameObject("MxTest_Patch");
            _go.transform.localScale = new Vector3(2, 2, 2);

            Assert.IsTrue(Patch(_go.transform, "{\"localScale\":{\"z\":5}}"));
            Assert.AreEqual(new Vector3(2, 2, 5), _go.transform.localScale);
        }
    }
}
