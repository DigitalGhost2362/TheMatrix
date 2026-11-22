#nullable enable

using System.Linq;
using Feeder.Reflect;
using Feeder.Reflect.Model;
using NUnit.Framework;
using UnityEngine;
using ReflectLogType = Feeder.Reflect.Model.LogType;

namespace Feeder.MCP.Editor.Tests.Reflect
{
    public sealed class TryReadAtTests
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

        T ReadValue<T>(object obj, string path)
        {
            Assert.IsTrue(_reflector.TryReadAt(obj, path, out var member), path);
            return member!.GetValue<T>(_reflector)!;
        }

        [Test]
        public void TopLevelField_ReturnsItsValue()
        {
            Assert.AreEqual(3, ReadValue<int>(new Ship(), "crew"));
        }

        [Test]
        public void Property_ReturnsItsValue()
        {
            Assert.AreEqual(4f, ReadValue<float>(new Ship(), "Speed"));
        }

        [Test]
        public void NestedStructField_ReturnsLeafValue()
        {
            Assert.AreEqual(50, ReadValue<int>(new Ship(), "engine/fuel/liters"));
        }

        [Test]
        public void ListElementField_ReturnsThatElementsValue()
        {
            Assert.AreEqual("gas", ReadValue<string>(new Ship(), "holds/[1]/label"));
        }

        [Test]
        public void ArrayIndex_ReturnsElement()
        {
            Assert.AreEqual(9, ReadValue<int>(new Ship(), "codes/[2]"));
        }

        [Test]
        public void DictionaryKeys_ResolveStringAndIntKeys()
        {
            var ship = new Ship();

            Assert.AreEqual(4, ReadValue<int>(ship, "stock/[iron]"));
            Assert.AreEqual("laser", ReadValue<string>(ship, "slots/[1]"));
        }

        [TestCase("#/crew")]
        [TestCase("#crew")]
        [TestCase("/crew/")]
        public void PathPrefixesAndStraySlashes_AreIgnored(string path)
        {
            Assert.AreEqual(3, ReadValue<int>(new Ship(), path));
        }

        [Test]
        public void EmptyPath_SerializesTheRoot()
        {
            Assert.IsTrue(_reflector.TryReadAt(new Ship(), string.Empty, out var member));

            Assert.AreEqual(typeof(Ship).GetTypeId(), member!.typeName);
            Assert.IsNotNull(member.GetField("crew"));
        }

        [Test]
        public void StructLeaf_IsReturnedAsTypedValue()
        {
            Assert.AreEqual(new Vector3(1, 2, 3), ReadValue<Vector3>(new Ship(), "position"));
        }

        [Test]
        public void MissingMember_FailsAndNamesAvailableMembers()
        {
            var logs = new Logs();

            Assert.IsFalse(_reflector.TryReadAt(new Ship(), "engine/warp", out var member, logs: logs));

            Assert.IsNull(member);
            var error = logs.Single(l => l.Type == ReflectLogType.Error).Message;
            StringAssert.Contains("'warp'", error);
            StringAssert.Contains("thrust", error);
        }

        [Test]
        public void IndexOutOfRange_Fails()
        {
            var logs = new Logs();

            Assert.IsFalse(_reflector.TryReadAt(new Ship(), "codes/[3]", out _, logs: logs));
            StringAssert.Contains("out of range", logs.First().Message);
        }

        [Test]
        public void NonNumericListIndex_Fails()
        {
            Assert.IsFalse(_reflector.TryReadAt(new Ship(), "holds/[first]", out _));
        }

        [Test]
        public void BracketOnScalar_Fails()
        {
            var logs = new Logs();

            Assert.IsFalse(_reflector.TryReadAt(new Ship(), "crew/[0]", out _, logs: logs));
            StringAssert.Contains("not an array, list, or dictionary", logs.First().Message);
        }

        [Test]
        public void MissingDictionaryKey_Fails()
        {
            Assert.IsFalse(_reflector.TryReadAt(new Ship(), "stock/[gold]", out _));
        }

        [Test]
        public void NullIntermediate_FailsInsteadOfThrowing()
        {
            var logs = new Logs();

            Assert.IsFalse(_reflector.TryReadAt(new Ship { cargo = null }, "cargo/label", out _, logs: logs));
            StringAssert.Contains("null", logs.First().Message);
        }

        [Test]
        public void UnityComponentProperty_ReturnsCurrentValue()
        {
            _go = new GameObject("MxTest_ReadAt");
            _go.transform.localPosition = new Vector3(5, 6, 7);

            Assert.AreEqual(new Vector3(5, 6, 7), ReadValue<Vector3>(_go.transform, "localPosition"));
        }

        [Test]
        public void UnityColliderNestedStructField_ReturnsLeaf()
        {
            _go = new GameObject("MxTest_ReadAt");
            var box = _go.AddComponent<BoxCollider>();
            box.size = new Vector3(2, 3, 4);

            Assert.AreEqual(3f, ReadValue<float>(box, "size/y"));
        }
    }
}
