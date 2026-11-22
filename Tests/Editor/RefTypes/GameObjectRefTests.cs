#nullable enable
#pragma warning disable CS0618

using System;
using Feeder.Json;
using Feeder.MCP.Data;
using Feeder.MCP.Runtime.Extensions;
using Feeder.MCP.Runtime.Utils;
using Feeder.Reflect;
using NUnit.Framework;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests.RefTypes
{
    public sealed class GameObjectRefTests
    {
        GameObject _root = null!;
        GameObject _arm = null!;
        GameObject _hand = null!;
        string _rootName = null!;

        [SetUp]
        public void CreateHierarchy()
        {
            _rootName = "MxTest_Root_" + Guid.NewGuid().ToString("N");
            _root = new GameObject(_rootName);
            _arm = new GameObject("Arm");
            _arm.transform.SetParent(_root.transform, false);
            _hand = new GameObject("MxTest_Hand_" + Guid.NewGuid().ToString("N"));
            _hand.transform.SetParent(_arm.transform, false);
        }

        [TearDown]
        public void DestroyHierarchy()
        {
            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);
        }

        [Test]
        public void FromGameObject_CapturesIdNameAndHierarchyPath()
        {
            var reference = new GameObjectRef(_hand);

            Assert.AreEqual(_hand.GetInstanceID(), reference.InstanceID);
            Assert.AreEqual(_hand.name, reference.Name);
            Assert.AreEqual($"{_rootName}/Arm/{_hand.name}", reference.Path);
            Assert.IsNull(reference.AssetPath);
        }

        [Test]
        public void FromNull_IsEmptyAndInvalid()
        {
            var reference = new GameObjectRef((GameObject?)null);

            Assert.AreEqual(0, reference.InstanceID);
            Assert.IsFalse(reference.IsValid(out var error));
            StringAssert.Contains("'path'", error);
            StringAssert.Contains("'name'", error);
        }

        [Test]
        public void IsValid_AcceptsPathOrNameAlone()
        {
            Assert.IsTrue(new GameObjectRef { Path = "a/b" }.IsValid());
            Assert.IsTrue(new GameObjectRef { Name = "b" }.IsValid());
            Assert.IsTrue(new GameObjectRef(123).IsValid());
        }

        [Test]
        public void Find_ByInstanceId_ReturnsThatObject()
        {
            var found = new GameObjectRef(_hand.GetInstanceID()).FindGameObject(out var error);

            Assert.AreSame(_hand, found);
            Assert.IsNull(error);
        }

        [Test]
        public void Find_ByPath_WalksTheHierarchy()
        {
            Assert.AreSame(_hand, new GameObjectRef { Path = $"{_rootName}/Arm/{_hand.name}" }.FindGameObject());
        }

        [Test]
        public void Find_ByPath_IgnoresSurroundingSlashes()
        {
            Assert.AreSame(_arm, new GameObjectRef { Path = $"/{_rootName}/Arm/" }.FindGameObject());
        }

        [Test]
        public void Find_ByName_SearchesNestedChildren()
        {
            Assert.AreSame(_hand, new GameObjectRef { Name = _hand.name }.FindGameObject());
        }

        [Test]
        public void Find_InstanceIdWinsOverPathAndName()
        {
            var reference = new GameObjectRef(_arm.GetInstanceID()) { Name = _hand.name, Path = _rootName };

            Assert.AreSame(_arm, reference.FindGameObject());
        }

        [Test]
        public void Find_PathWinsOverName()
        {
            var reference = new GameObjectRef { Path = _rootName, Name = _hand.name };

            Assert.AreSame(_root, reference.FindGameObject());
        }

        [Test]
        public void Find_UnknownName_ReturnsNullWithError()
        {
            var missing = "MxTest_Missing_" + Guid.NewGuid().ToString("N");

            Assert.IsNull(new GameObjectRef { Name = missing }.FindGameObject(out var error));
            StringAssert.Contains(missing, error);
        }

        [Test]
        public void Find_BrokenPath_ReturnsNullWithError()
        {
            Assert.IsNull(new GameObjectRef { Path = $"{_rootName}/Leg" }.FindGameObject(out var error));
            StringAssert.Contains("Leg", error);
        }

        [Test]
        public void Find_InvalidReference_ReportsWhatIsMissing()
        {
            Assert.IsNull(new GameObjectRef().FindGameObject(out var error));
            StringAssert.Contains("instanceID", error);
        }

        [Test]
        public void Find_NullReference_ReturnsNullWithoutError()
        {
            Assert.IsNull(((GameObjectRef?)null).FindGameObject(out var error));
            Assert.IsNull(error);
        }

        [Test]
        public void FindBy_DestroyedInstanceId_ReportsNotFound()
        {
            var doomed = new GameObject("MxTest_Doomed");
            var id = doomed.GetInstanceID();
            UnityEngine.Object.DestroyImmediate(doomed);

            Assert.IsNull(GameObjectUtils.FindBy(id, null, null, out var error));
            StringAssert.Contains(id.ToString(), error);
        }

        [Test]
        public void ToString_DescribesTheStrongestKey()
        {
            StringAssert.Contains("instanceID='5'", new GameObjectRef(5) { Name = "x" }.ToString());
            StringAssert.Contains("path='a/b'", new GameObjectRef { Path = "a/b", Name = "b" }.ToString());
            StringAssert.Contains("name='b'", new GameObjectRef { Name = "b" }.ToString());
        }

        [Test]
        public void Json_RoundTrip_KeepsEveryKey()
        {
            var reflector = MatrixPluginEditor.Instance.CreateDefaultReflector();
            var source = new GameObjectRef(42) { Path = "a/b", Name = "b" };

            var json = reflector.JsonSerializer.Serialize(source, typeof(GameObjectRef));
            var back = reflector.JsonSerializer.Deserialize<GameObjectRef>(json)!;

            Assert.AreEqual(42, back.InstanceID);
            Assert.AreEqual("a/b", back.Path);
            Assert.AreEqual("b", back.Name);
        }

        [Test]
        public void Json_OmitsEmptyKeys()
        {
            var reflector = MatrixPluginEditor.Instance.CreateDefaultReflector();
            var json = reflector.JsonSerializer.Serialize(new GameObjectRef { Name = "solo" }, typeof(GameObjectRef));

            StringAssert.DoesNotContain("\"path\"", json);
            StringAssert.Contains("\"name\"", json);
            StringAssert.Contains("\"instanceID\"", json);
        }

        [Test]
        public void Json_UnknownKey_Throws()
        {
            var reflector = MatrixPluginEditor.Instance.CreateDefaultReflector();

            Assert.Catch<JsonException>(() => reflector.JsonSerializer.Deserialize<GameObjectRef>("{\"guid\":\"x\"}"));
        }
    }
}
