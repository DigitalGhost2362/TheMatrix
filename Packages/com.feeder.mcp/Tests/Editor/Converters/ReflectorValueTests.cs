#nullable enable

using System;
using System.Linq;
using Feeder.Json;
using Feeder.Reflect;
using Feeder.Reflect.Model;
using NUnit.Framework;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests.Converters
{
    public enum Rarity
    {
        Common,
        Rare,
        Legendary
    }

    [Flags]
    public enum Facing
    {
        None = 0,
        North = 1,
        East = 2,
        South = 4
    }

    public sealed class Loadout
    {
        public string title = string.Empty;
        public int level;
        public Rarity rarity;
        public Vector3 offset;
        public Quaternion spin = Quaternion.identity;
        public Color tint = Color.white;
        public Rect area;
    }

    public sealed class Bag
    {
        public int[] slots = Array.Empty<int>();
    }

    public sealed class ReflectorValueTests
    {
        Reflector _reflector = null!;

        [OneTimeSetUp]
        public void CreateReflector() => _reflector = MatrixPluginEditor.Instance.CreateDefaultReflector();

        [Test]
        public void Serialize_Vector3_IsAnAtomicJsonValue()
        {
            var member = _reflector.Serialize(new Vector3(1, 2, 3), name: "pos");

            Assert.AreEqual("pos", member.name);
            Assert.AreEqual("UnityEngine.Vector3", member.typeName);
            Assert.IsNull(member.fields);
            Assert.IsNull(member.props);
            Assert.AreEqual(new Vector3(1, 2, 3), member.GetValue<Vector3>(_reflector));
        }

        [Test]
        public void Deserialize_SerializedQuaternion_ReturnsTheSameRotation()
        {
            var value = Quaternion.Euler(0, 90, 45);
            var member = _reflector.Serialize(value);

            var back = (Quaternion)_reflector.Deserialize(member)!;

            Assert.AreEqual(value.x, back.x);
            Assert.AreEqual(value.w, back.w);
        }

        [Test]
        public void Deserialize_UsesTypeNameOverFallback()
        {
            var member = SerializedMember.FromValue(_reflector, new Vector2Int(3, -4));

            Assert.AreEqual(new Vector2Int(3, -4), _reflector.Deserialize(member, typeof(object)));
        }

        [Test]
        public void Serialize_PlainObject_ListsPublicFieldsByName()
        {
            var member = _reflector.Serialize(new Loadout { title = "scout", level = 3 });
            var names = member.fields!.Select(f => f.name).ToArray();

            CollectionAssert.IsSupersetOf(names, new[] { "title", "level", "rarity", "offset", "spin", "tint", "area" });
            Assert.AreEqual(3, member.GetField("level")!.GetValue<int>(_reflector));
            Assert.AreEqual("scout", member.GetField("title")!.GetValue<string>(_reflector));
        }

        [Test]
        public void SerializeThenDeserialize_PlainObjectWithUnityStructs_RestoresEveryField()
        {
            var source = new Loadout
            {
                title = "raider",
                level = 12,
                rarity = Rarity.Legendary,
                offset = new Vector3(0.5f, -1, 8),
                spin = Quaternion.Euler(30, 60, 90),
                tint = new Color(0.2f, 0.4f, 0.6f, 0.8f),
                area = new Rect(1, 2, 3, 4)
            };

            var back = _reflector.Deserialize<Loadout>(_reflector.Serialize(source))!;

            Assert.AreEqual(source.title, back.title);
            Assert.AreEqual(source.level, back.level);
            Assert.AreEqual(source.rarity, back.rarity);
            Assert.AreEqual(source.offset, back.offset);
            Assert.AreEqual(source.spin, back.spin);
            Assert.AreEqual(source.tint, back.tint);
            Assert.AreEqual(source.area, back.area);
        }

        [Test]
        public void SerializeThenDeserialize_IntArray_KeepsOrder()
        {
            var back = _reflector.Deserialize<Bag>(_reflector.Serialize(new Bag { slots = new[] { 5, 3, 9 } }))!;

            CollectionAssert.AreEqual(new[] { 5, 3, 9 }, back.slots);
        }

        [Test]
        public void Enum_IsWrittenAsItsName()
        {
            Assert.AreEqual("\"Rare\"", _reflector.JsonSerializer.Serialize(Rarity.Rare, typeof(Rarity)));
        }

        [TestCase("\"Legendary\"", Rarity.Legendary)]
        [TestCase("\"legendary\"", Rarity.Legendary)]
        [TestCase("1", Rarity.Rare)]
        public void Enum_ReadsNamesIgnoringCaseAndDefinedNumbers(string json, Rarity expected)
        {
            Assert.AreEqual(expected, _reflector.JsonSerializer.Deserialize<Rarity>(json));
        }

        [TestCase("\"Mythic\"")]
        [TestCase("7")]
        public void Enum_UndefinedValue_Throws(string json)
        {
            Assert.Catch<JsonException>(() => _reflector.JsonSerializer.Deserialize<Rarity>(json));
        }

        [Test]
        public void FlagsEnum_Combination_RoundTrips()
        {
            var value = Facing.North | Facing.South;
            var json = _reflector.JsonSerializer.Serialize(value, typeof(Facing));

            Assert.AreEqual(value, _reflector.JsonSerializer.Deserialize<Facing>(json));
        }

        [Test]
        public void CreateDefaultReflector_KnowsEveryUnityStruct()
        {
            var types = new[]
            {
                typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Vector2Int), typeof(Vector3Int),
                typeof(Quaternion), typeof(Color), typeof(Color32), typeof(Rect), typeof(RectInt),
                typeof(Bounds), typeof(BoundsInt), typeof(Matrix4x4), typeof(Gradient)
            };

            foreach (var type in types)
                Assert.IsNotNull(_reflector.JsonSerializer.GetJsonConverter(type), type.Name);
        }
    }
}
