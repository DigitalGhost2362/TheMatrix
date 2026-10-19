#nullable enable

using System.Linq;
using Feeder.Reflect;
using Feeder.Reflect.Model;
using NUnit.Framework;
using UnityEngine;
using ReflectLogType = Feeder.Reflect.Model.LogType;

namespace Feeder.MCP.Editor.Tests.Reflect
{
    public sealed class TryModifyAtTests
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

        bool Modify<T>(object target, string path, T value, Logs? logs = null)
        {
            object? obj = target;
            return _reflector.TryModifyAt(ref obj, path, value, logs: logs);
        }

        [Test]
        public void TopLevelField_IsReplaced()
        {
            var ship = new Ship();

            Assert.IsTrue(Modify(ship, "crew", 12));
            Assert.AreEqual(12, ship.crew);
        }

        [Test]
        public void StringField_IsReplaced()
        {
            var ship = new Ship();

            Assert.IsTrue(Modify(ship, "name", "Vega"));
            Assert.AreEqual("Vega", ship.name);
        }

        [Test]
        public void EnumField_IsReplaced()
        {
            var ship = new Ship();

            Assert.IsTrue(Modify(ship, "hull", Hull.Titanium));
            Assert.AreEqual(Hull.Titanium, ship.hull);
        }

        [Test]
        public void Property_IsReplaced()
        {
            var ship = new Ship();

            Assert.IsTrue(Modify(ship, "Speed", 9.5f));
            Assert.AreEqual(9.5f, ship.Speed);
        }

        [Test]
        public void UnityStructAxis_WritesThroughAndKeepsOtherAxes()
        {
            var ship = new Ship();

            Assert.IsTrue(Modify(ship, "position/y", 20f));
            Assert.AreEqual(new Vector3(1, 20, 3), ship.position);
        }

        [Test]
        public void StructInsideStruct_WritesThroughBothLevels()
        {
            var ship = new Ship();

            Assert.IsTrue(Modify(ship, "engine/fuel/liters", 75));
            Assert.AreEqual(75, ship.engine.fuel.liters);
            Assert.AreEqual(10f, ship.engine.thrust);
        }

        [Test]
        public void WholeUnityStruct_IsReplaced()
        {
            var ship = new Ship();

            Assert.IsTrue(Modify(ship, "position", new Vector3(-4, 5, -6)));
            Assert.AreEqual(new Vector3(-4, 5, -6), ship.position);
        }

        [Test]
        public void ListElementField_ChangesOnlyThatElement()
        {
            var ship = new Ship();

            Assert.IsTrue(Modify(ship, "holds/[0]/mass", 6.5f));
            Assert.AreEqual(6.5f, ship.holds[0].mass);
            Assert.AreEqual(4f, ship.holds[1].mass);
        }

        [Test]
        public void ArrayElement_IsReplaced()
        {
            var ship = new Ship();

            Assert.IsTrue(Modify(ship, "codes/[1]", 80));
            CollectionAssert.AreEqual(new[] { 7, 80, 9 }, ship.codes);
        }

        [Test]
        public void DictionaryExistingKey_IsReplaced()
        {
            var ship = new Ship();

            Assert.IsTrue(Modify(ship, "stock/[iron]", 40));
            Assert.AreEqual(40, ship.stock["iron"]);
        }

        [Test]
        public void DictionaryNewKey_IsAdded()
        {
            var ship = new Ship();

            Assert.IsTrue(Modify(ship, "stock/[gold]", 9));
            Assert.AreEqual(9, ship.stock["gold"]);
            Assert.AreEqual(4, ship.stock["iron"]);
        }

        [Test]
        public void ExistingObject_IsReplacedFromValue()
        {
            var ship = new Ship();

            Assert.IsTrue(Modify(ship, "cargo", new Cargo { label = "salt", mass = 0.5f }));
            Assert.AreEqual("salt", ship.cargo!.label);
            Assert.AreEqual(0.5f, ship.cargo.mass);
        }

        [Test]
        public void NullObjectField_IsFilledFromSerializedMember()
        {
            var ship = new Ship { cargo = null };
            object? obj = ship;

            Assert.IsTrue(_reflector.TryModifyAt(ref obj, "cargo", _reflector.Serialize(new Cargo { label = "tea", mass = 1.25f })));
            Assert.AreEqual("tea", ship.cargo!.label);
            Assert.AreEqual(1.25f, ship.cargo.mass);
        }

        [Test]
        public void NullObjectField_IsFilledFromTypedValue()
        {
            var ship = new Ship { cargo = null };

            Assert.IsTrue(Modify(ship, "cargo", new Cargo { label = "tea", mass = 1.25f }));
            Assert.AreEqual("tea", ship.cargo?.label);
        }

        [Test]
        public void IndexOutOfRange_FailsAndLeavesListUntouched()
        {
            var ship = new Ship();
            var logs = new Logs();

            Assert.IsFalse(Modify(ship, "holds/[5]/mass", 1f, logs));
            Assert.AreEqual(2, ship.holds.Count);
            StringAssert.Contains("out of range", logs.First(l => l.Type == ReflectLogType.Error).Message);
        }

        [Test]
        public void MissingMember_FailsAndListsAvailableFields()
        {
            var ship = new Ship();
            var logs = new Logs();

            Assert.IsFalse(Modify(ship, "captain", "Ripley", logs));
            var error = logs.First(l => l.Type == ReflectLogType.Error).Message;
            StringAssert.Contains("'captain'", error);
            StringAssert.Contains("crew", error);
        }

        [Test]
        public void ReadOnlyProperty_Fails()
        {
            var logs = new Logs();

            Assert.IsFalse(Modify(new Ship(), "Decks", 5, logs));
            StringAssert.Contains("read-only", logs.First().Message);
        }

        [Test]
        public void NullRootWithPath_Fails()
        {
            object? obj = null;

            Assert.IsFalse(_reflector.TryModifyAt(ref obj, "crew", 1));
        }

        [Test]
        public void TransformLocalPositionAxis_WritesThroughTheProperty()
        {
            _go = new GameObject("MxTest_ModifyAt");
            _go.transform.localPosition = new Vector3(1, 1, 1);

            Assert.IsTrue(Modify(_go.transform, "localPosition/x", 8f));
            Assert.AreEqual(new Vector3(8, 1, 1), _go.transform.localPosition);
        }

        [Test]
        public void ColliderCenter_IsReplaced()
        {
            _go = new GameObject("MxTest_ModifyAt");
            var box = _go.AddComponent<BoxCollider>();

            Assert.IsTrue(Modify(box, "center", new Vector3(0, 2, 0)));
            Assert.AreEqual(new Vector3(0, 2, 0), box.center);
        }
    }
}
