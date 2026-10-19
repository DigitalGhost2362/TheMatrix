#nullable enable

using Feeder.Json;
using Feeder.Reflect;
using NUnit.Framework;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests.Converters
{
    public sealed class UnityStructJsonTests
    {
        Reflector _reflector = null!;

        [OneTimeSetUp]
        public void CreateReflector() => _reflector = MatrixPluginEditor.Instance.CreateDefaultReflector();

        string ToJson<T>(T value) => _reflector.JsonSerializer.Serialize(value, typeof(T));

        T FromJson<T>(string json) => _reflector.JsonSerializer.Deserialize<T>(json)!;

        T RoundTrip<T>(T value) => FromJson<T>(ToJson(value));

        [TestCase(0f, 0f)]
        [TestCase(1.5f, -2.25f)]
        [TestCase(123456.79f, -0.0001f)]
        public void Vector2_RoundTrip_KeepsExactComponents(float x, float y)
        {
            var back = RoundTrip(new Vector2(x, y));

            Assert.AreEqual(x, back.x);
            Assert.AreEqual(y, back.y);
        }

        [TestCase(0f, 0f, 0f)]
        [TestCase(1.5f, -2.25f, 1e6f)]
        [TestCase(-0.333333f, 7.125f, float.MaxValue)]
        public void Vector3_RoundTrip_KeepsExactComponents(float x, float y, float z)
        {
            var back = RoundTrip(new Vector3(x, y, z));

            Assert.AreEqual(x, back.x);
            Assert.AreEqual(y, back.y);
            Assert.AreEqual(z, back.z);
        }

        [Test]
        public void Vector4_RoundTrip_KeepsExactComponents()
        {
            var value = new Vector4(-1.25f, 2.5f, 1e-7f, 99f);

            Assert.AreEqual(value, RoundTrip(value));
            Assert.AreEqual(value.z, RoundTrip(value).z);
        }

        [TestCase(0, 0)]
        [TestCase(-7, 42)]
        [TestCase(int.MinValue, int.MaxValue)]
        public void Vector2Int_RoundTrip_KeepsComponents(int x, int y)
        {
            Assert.AreEqual(new Vector2Int(x, y), RoundTrip(new Vector2Int(x, y)));
        }

        [Test]
        public void Vector3Int_RoundTrip_KeepsComponents()
        {
            var value = new Vector3Int(-3, 0, int.MaxValue);

            Assert.AreEqual(value, RoundTrip(value));
        }

        [Test]
        public void Vector3_WritesLowercaseAxisNames()
        {
            using var doc = JsonDocument.Parse(ToJson(new Vector3(1, 2, 3)));
            var root = doc.RootElement;

            Assert.AreEqual(1f, root.GetProperty("x").GetSingle());
            Assert.AreEqual(2f, root.GetProperty("y").GetSingle());
            Assert.AreEqual(3f, root.GetProperty("z").GetSingle());
        }

        [Test]
        public void Vector3_NamedFloatLiterals_RoundTripAsStrings()
        {
            var json = ToJson(new Vector3(float.NaN, float.PositiveInfinity, float.NegativeInfinity));
            var back = FromJson<Vector3>(json);

            StringAssert.Contains("\"NaN\"", json);
            StringAssert.Contains("\"Infinity\"", json);
            StringAssert.Contains("\"-Infinity\"", json);
            Assert.IsTrue(float.IsNaN(back.x));
            Assert.IsTrue(float.IsPositiveInfinity(back.y));
            Assert.IsTrue(float.IsNegativeInfinity(back.z));
        }

        [Test]
        public void Vector3_MissingAxis_DefaultsToZero()
        {
            Assert.AreEqual(new Vector3(4, 0, 0), FromJson<Vector3>("{\"x\":4}"));
        }

        [Test]
        public void Vector3_UnknownProperty_Throws()
        {
            Assert.Catch<JsonException>(() => FromJson<Vector3>("{\"x\":1,\"w\":2}"));
        }

        [Test]
        public void Vector3_UnrecognizedStringLiteral_Throws()
        {
            Assert.Catch<JsonException>(() => FromJson<Vector3>("{\"x\":\"lots\",\"y\":0,\"z\":0}"));
        }

        [Test]
        public void Vector3_NonObjectToken_Throws()
        {
            Assert.Catch<JsonException>(() => FromJson<Vector3>("[1,2,3]"));
        }

        [Test]
        public void Quaternion_RoundTrip_KeepsExactComponents()
        {
            var value = Quaternion.Euler(12f, 250f, -33f);
            var back = RoundTrip(value);

            Assert.AreEqual(value.x, back.x);
            Assert.AreEqual(value.y, back.y);
            Assert.AreEqual(value.z, back.z);
            Assert.AreEqual(value.w, back.w);
        }

        [Test]
        public void Quaternion_MissingW_DefaultsToIdentity()
        {
            var back = FromJson<Quaternion>("{\"x\":0,\"y\":0,\"z\":0}");

            Assert.AreEqual(1f, back.w);
            Assert.AreEqual(Quaternion.identity, back);
        }

        [Test]
        public void Color_RoundTrip_KeepsHdrAndNegativeChannels()
        {
            var value = new Color(4.5f, -0.25f, 0.5f, 0.125f);

            Assert.AreEqual(value, RoundTrip(value));
            Assert.AreEqual(4.5f, RoundTrip(value).r);
        }

        [Test]
        public void Color_MissingAlpha_DefaultsToOpaque()
        {
            Assert.AreEqual(new Color(0.1f, 0.2f, 0.3f, 1f), FromJson<Color>("{\"r\":0.1,\"g\":0.2,\"b\":0.3}"));
        }

        [Test]
        public void Color32_RoundTrip_KeepsBytes()
        {
            var value = new Color32(0, 17, 200, 255);
            var back = RoundTrip(value);

            Assert.AreEqual((value.r, value.g, value.b, value.a), (back.r, back.g, back.b, back.a));
        }

        [Test]
        public void Color32_MissingAlpha_DefaultsTo255()
        {
            var back = FromJson<Color32>("{\"r\":1,\"g\":2,\"b\":3}");

            Assert.AreEqual(255, back.a);
            Assert.AreEqual(3, back.b);
        }

        [Test]
        public void Rect_RoundTrip_UsesPositionAndSizeNames()
        {
            var value = new Rect(-10f, 5.5f, 300f, 0.25f);
            var json = ToJson(value);

            StringAssert.Contains("\"width\"", json);
            StringAssert.Contains("\"height\"", json);
            Assert.AreEqual(value, FromJson<Rect>(json));
        }

        [Test]
        public void RectInt_RoundTrip_KeepsValues()
        {
            var value = new RectInt(-4, 8, 16, 32);

            Assert.AreEqual(value, RoundTrip(value));
        }

        [Test]
        public void Bounds_RoundTrip_KeepsCenterAndSize()
        {
            var value = new Bounds(new Vector3(1, -2, 3), new Vector3(10, 0.5f, 4));
            var back = RoundTrip(value);

            Assert.AreEqual(value.center, back.center);
            Assert.AreEqual(value.size, back.size);
        }

        [Test]
        public void Bounds_MissingSize_IsAPoint()
        {
            var back = FromJson<Bounds>("{\"center\":{\"x\":1,\"y\":2,\"z\":3}}");

            Assert.AreEqual(new Vector3(1, 2, 3), back.center);
            Assert.AreEqual(Vector3.zero, back.size);
        }

        [Test]
        public void BoundsInt_RoundTrip_KeepsPositionAndSize()
        {
            var value = new BoundsInt(new Vector3Int(-1, 2, -3), new Vector3Int(4, 5, 6));
            var back = RoundTrip(value);

            Assert.AreEqual(value.position, back.position);
            Assert.AreEqual(value.size, back.size);
        }

        [Test]
        public void Matrix4x4_Trs_RoundTripsEveryCell()
        {
            var value = Matrix4x4.TRS(new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 30), new Vector3(2, 0.5f, 1));
            var back = RoundTrip(value);

            for (var i = 0; i < 16; i++)
                Assert.AreEqual(value[i], back[i], $"cell {i}");
        }

        [Test]
        public void Matrix4x4_CellNamesFollowRowColumn()
        {
            var value = Matrix4x4.zero;
            value[0, 1] = 7f;
            value[2, 3] = -9f;

            using var doc = JsonDocument.Parse(ToJson(value));

            Assert.AreEqual(7f, doc.RootElement.GetProperty("m01").GetSingle());
            Assert.AreEqual(-9f, doc.RootElement.GetProperty("m23").GetSingle());
            Assert.AreEqual(0f, doc.RootElement.GetProperty("m10").GetSingle());
        }

        [Test]
        public void Gradient_RoundTrip_KeepsModeAndKeys()
        {
            var value = new Gradient { mode = GradientMode.Fixed };
            value.SetKeys(
                new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.25f, 1f) });

            var back = RoundTrip(value);

            Assert.AreEqual(GradientMode.Fixed, back.mode);
            Assert.AreEqual(2, back.colorKeys.Length);
            Assert.AreEqual(2, back.alphaKeys.Length);
            Assert.That(back.colorKeys[0].color.r, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(back.colorKeys[1].color.b, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(back.alphaKeys[1].alpha, Is.EqualTo(0.25f).Within(1e-4f));
            Assert.That(back.alphaKeys[1].time, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Gradient_EmptyObject_IsOpaqueWhite()
        {
            var back = FromJson<Gradient>("{}");
            var middle = back.Evaluate(0.5f);

            Assert.AreEqual(GradientMode.Blend, back.mode);
            Assert.That(middle.r, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(middle.g, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(middle.b, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(middle.a, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Gradient_UnknownMode_Throws()
        {
            Assert.Catch<JsonException>(() => FromJson<Gradient>("{\"mode\":\"Sideways\"}"));
        }
    }
}
