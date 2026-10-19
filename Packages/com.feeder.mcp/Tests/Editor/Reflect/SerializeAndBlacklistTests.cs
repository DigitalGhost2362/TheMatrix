#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Feeder.Reflect;
using Feeder.Reflect.Model;
using NUnit.Framework;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests.Reflect
{
    public sealed class Link
    {
        public string tag = string.Empty;
        public Link? first;
        public Link? second;
    }

    public sealed class SerializeAndBlacklistTests
    {
        Reflector _reflector = null!;

        [OneTimeSetUp]
        public void CreateReflector() => _reflector = MatrixPluginEditor.Instance.CreateDefaultReflector();

        static string?[] FieldNames(SerializedMember member) => member.fields?.Select(f => f.name).ToArray() ?? new string?[0];

        [TestCase(typeof(Action))]
        [TestCase(typeof(Func<int>))]
        [TestCase(typeof(EventHandler<EventArgs>))]
        [TestCase(typeof(CancellationToken))]
        [TestCase(typeof(IntPtr))]
        [TestCase(typeof(Action[]))]
        [TestCase(typeof(List<Action>))]
        public void DefaultReflector_BlacklistsDelegatesPointersAndTheirContainers(Type type)
        {
            Assert.IsTrue(_reflector.Converters.IsTypeBlacklisted(type));
        }

        [TestCase(typeof(int))]
        [TestCase(typeof(string))]
        [TestCase(typeof(Vector3))]
        [TestCase(typeof(List<int>))]
        [TestCase(typeof(Ship))]
        public void DefaultReflector_DoesNotBlacklistDataTypes(Type type)
        {
            Assert.IsFalse(_reflector.Converters.IsTypeBlacklisted(type));
        }

        [Test]
        public void Serialize_SkipsBlacklistedFieldsButKeepsTheRest()
        {
            var names = FieldNames(_reflector.Serialize(new Ship { onDock = () => { } }));

            CollectionAssert.DoesNotContain(names, "onDock");
            CollectionAssert.Contains(names, "crew");
        }

        [Test]
        public void BlacklistType_CoversDerivedTypesAndGenericArguments()
        {
            var reflector = MatrixPluginEditor.Instance.CreateDefaultReflector();

            Assert.IsTrue(reflector.Converters.BlacklistType(typeof(Cargo)));
            Assert.IsFalse(reflector.Converters.BlacklistType(typeof(Cargo)));
            Assert.IsTrue(reflector.Converters.IsTypeBlacklisted(typeof(HeavyCargo)));
            Assert.IsTrue(reflector.Converters.IsTypeBlacklisted(typeof(List<Cargo>)));

            var names = FieldNames(reflector.Serialize(new Ship()));
            CollectionAssert.DoesNotContain(names, "cargo");
            CollectionAssert.DoesNotContain(names, "holds");
            CollectionAssert.Contains(names, "engine");
        }

        [Test]
        public void RemoveBlacklistedType_RestoresSerialization()
        {
            var reflector = MatrixPluginEditor.Instance.CreateDefaultReflector();
            reflector.Converters.BlacklistType(typeof(Cargo));

            Assert.IsTrue(reflector.Converters.RemoveBlacklistedType(typeof(Cargo)));
            Assert.IsFalse(reflector.Converters.IsTypeBlacklisted(typeof(HeavyCargo)));
            CollectionAssert.Contains(FieldNames(reflector.Serialize(new Ship())), "cargo");
        }

        [Test]
        public void BlacklistType_ByUnknownName_ReturnsFalse()
        {
            var reflector = MatrixPluginEditor.Instance.CreateDefaultReflector();

            Assert.IsFalse(reflector.Converters.BlacklistType("No.Such.TypeAnywhere"));
        }

        [Test]
        public void Serialize_BlacklistedRoot_IsANullMember()
        {
            var member = _reflector.Serialize(new Action(() => { }), name: "callback");

            Assert.AreEqual("callback", member.name);
            Assert.IsTrue(member.IsNull());
        }

        [Test]
        public void TryModify_NestedStructMember_PreservesUnmentionedFields()
        {
            var ship = new Ship();
            object? obj = ship;
            var patch = new SerializedMember { typeName = typeof(Ship).GetTypeId() }
                .AddField(new SerializedMember { name = "position", typeName = typeof(Vector3).GetTypeId() }
                    .AddField(SerializedMember.FromValue(_reflector, 9f, "y")))
                .AddField(new SerializedMember { name = "engine", typeName = typeof(Engine).GetTypeId() }
                    .AddField(SerializedMember.FromValue(_reflector, 99f, "thrust")));

            Assert.IsTrue(_reflector.TryModify(ref obj, patch));
            Assert.AreEqual(new Vector3(1, 9, 3), ship.position);
            Assert.AreEqual(99f, ship.engine.thrust);
            Assert.AreEqual(50, ship.engine.fuel.liters);
        }

        [Test]
        public void TryModify_StructMemberWithValue_ReplacesItWhole()
        {
            var ship = new Ship();
            object? obj = ship;
            var patch = new SerializedMember { typeName = typeof(Ship).GetTypeId() }
                .AddField(SerializedMember.FromValue(_reflector, new Vector3(7, 8, 9), "position"));

            Assert.IsTrue(_reflector.TryModify(ref obj, patch));
            Assert.AreEqual(new Vector3(7, 8, 9), ship.position);
        }

        [Test]
        public void Serialize_ReferenceAfterACycle_PointsAtTheTargetsOwnPath()
        {
            var root = new Link { tag = "root" };
            var child = new Link { tag = "child" };
            root.first = root;
            root.second = child;
            child.first = child;

            var member = _reflector.Serialize(root);
            var back = member.GetField("second")!.GetField("first")!;

            Assert.AreEqual("Reference", back.typeName);
            Assert.AreEqual("#/second", back.valueJsonElement!.Value.GetProperty("$ref").GetString());
        }
    }
}
