#nullable enable
#pragma warning disable CS0618

using Feeder.MCP.Data;
using Feeder.MCP.Runtime.Extensions;
using NUnit.Framework;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests.RefTypes
{
    public sealed class ComponentRefTests
    {
        GameObject _go = null!;
        BoxCollider _box = null!;

        [SetUp]
        public void CreateObject()
        {
            _go = new GameObject("MxTest_ComponentRef");
            _box = _go.AddComponent<BoxCollider>();
        }

        [TearDown]
        public void DestroyObject()
        {
            if (_go != null)
                UnityEngine.Object.DestroyImmediate(_go);
        }

        [Test]
        public void FromComponent_CapturesIdIndexAndType()
        {
            var reference = new ComponentRef(_box);

            Assert.AreEqual(_box.GetInstanceID(), reference.InstanceID);
            Assert.AreEqual(1, reference.Index);
            Assert.AreEqual(nameof(BoxCollider), reference.TypeName);
        }

        [Test]
        public void FromTransform_HasIndexZero()
        {
            Assert.AreEqual(0, new ComponentRef(_go.transform).Index);
        }

        [Test]
        public void Default_IsInvalidAndExplainsWhy()
        {
            var reference = new ComponentRef();

            Assert.AreEqual(-1, reference.Index);
            Assert.IsFalse(reference.IsValid(out var error));
            StringAssert.Contains("index", error);
        }

        [Test]
        public void IsValid_AcceptsAnySingleKey()
        {
            Assert.IsTrue(new ComponentRef { Index = 0 }.IsValid());
            Assert.IsTrue(new ComponentRef { TypeName = "UnityEngine.Transform" }.IsValid());
            Assert.IsTrue(new ComponentRef(77).IsValid());
        }

        [Test]
        public void Matches_ByInstanceId_IgnoresIndexAndType()
        {
            var reference = new ComponentRef(_box.GetInstanceID()) { Index = 0, TypeName = "UnityEngine.Transform" };

            Assert.IsTrue(reference.Matches(_box, 1));
            Assert.IsFalse(reference.Matches(_go.transform, 0));
        }

        [Test]
        public void Matches_ByIndex_NeedsTheIndex()
        {
            var reference = new ComponentRef { Index = 1 };

            Assert.IsTrue(reference.Matches(_box, 1));
            Assert.IsFalse(reference.Matches(_go.transform, 0));
            Assert.IsFalse(reference.Matches(_box));
        }

        [Test]
        public void Matches_ByFullTypeName()
        {
            var reference = new ComponentRef { TypeName = "UnityEngine.BoxCollider" };

            Assert.IsTrue(reference.Matches(_box));
            Assert.IsFalse(reference.Matches(_go.transform));
        }

        [Test]
        public void Matches_DefaultReferenceMatchesOnlyNull()
        {
            Assert.IsTrue(new ComponentRef().Matches(null!));
            Assert.IsFalse(new ComponentRef().Matches(_box));
        }

        [Test]
        public void ReferenceBuiltFromComponent_MatchesByTypeNameAlone()
        {
            var reference = new ComponentRef(_box) { InstanceID = 0, Index = -1 };

            Assert.IsTrue(reference.Matches(_box));
        }

        [Test]
        public void ToString_DescribesTheStrongestKey()
        {
            StringAssert.Contains("instanceID='9'", new ComponentRef(9) { Index = 2 }.ToString());
            StringAssert.Contains("index='2'", new ComponentRef { Index = 2, TypeName = "T" }.ToString());
            StringAssert.Contains("typeName='T'", new ComponentRef { TypeName = "T" }.ToString());
            StringAssert.Contains("unknown", new ComponentRef().ToString());
        }

        [Test]
        public void Json_RoundTrip_KeepsEveryKey()
        {
            var reflector = MatrixPluginEditor.Instance.CreateDefaultReflector();
            var json = reflector.JsonSerializer.Serialize(new ComponentRef(12) { Index = 3, TypeName = "UnityEngine.Light" }, typeof(ComponentRef));

            var back = reflector.JsonSerializer.Deserialize<ComponentRef>(json)!;

            Assert.AreEqual(12, back.InstanceID);
            Assert.AreEqual(3, back.Index);
            Assert.AreEqual("UnityEngine.Light", back.TypeName);
        }
    }
}
