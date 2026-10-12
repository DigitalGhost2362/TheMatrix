using System.Globalization;
using Feeder.Json.Nodes;
using Feeder.Json.ParityTests.SerializerParity;
using static Feeder.Json.ParityTests.TestSupport;
using S = System.Text.Json;
using SN = System.Text.Json.Nodes;
using SS = System.Text.Json.Serialization;

namespace Feeder.Json.ParityTests.DomParity;

// the conversion operators and JsonValue.Create overloads, value by value, against the reference
public class NodeConversionTests
{
    static readonly JsonSerializerOptions named = new() { NumberHandling = Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals, WriteIndented = true };
    static readonly S.JsonSerializerOptions referenceNamed = new() { NumberHandling = SS.JsonNumberHandling.AllowNamedFloatingPointLiterals, WriteIndented = true };
    static readonly Guid sampleGuid = new("0A1B2C3D-4E5F-6071-8293-A4B5C6D7E8F9");
    static readonly DateTime utc = new DateTime(2024, 2, 29, 23, 59, 59, DateTimeKind.Utc).AddTicks(1234500);
    static readonly DateTime local = new DateTime(2026, 10, 6, 14, 10, 23, DateTimeKind.Local).AddTicks(5);
    static readonly DateTime unspecified = new(2000, 1, 1);
    static readonly DateTimeOffset offset = new DateTimeOffset(2020, 5, 6, 7, 8, 9, TimeSpan.FromMinutes(-150)).AddTicks(10);

    static string Describe(JsonNode? node) => node == null ? "(null)" : string.Join("|",
        Try(() => node.ToJsonString()), Try(() => node.ToJsonString(named)), Try(() => Show(node.ToString())), node.GetValueKind().ToString(), Try(() => node.DeepClone().ToJsonString()));

    static string Describe(SN.JsonNode? node) => node == null ? "(null)" : string.Join("|",
        Try(() => node.ToJsonString()), Try(() => node.ToJsonString(referenceNamed)), Try(() => Show(node.ToString())), node.GetValueKind().ToString(), Try(() => node.DeepClone().ToJsonString()));

    // one transcript per value: the implicit node, JsonValue.Create, a params array and an object member
    static void Same<T>(T[] values, Func<T, JsonNode?> convert, Func<T, SN.JsonNode?> referenceConvert, Func<T, JsonNode?> create, Func<T, SN.JsonNode?> referenceCreate,
        Func<T, JsonNode> array, Func<T, SN.JsonNode> referenceArray, Func<T, JsonNode> member, Func<T, SN.JsonNode> referenceMember)
    {
        foreach (T value in values)
        {
            string expected = string.Join("\n", Try(() => Describe(referenceConvert(value))), Try(() => Describe(referenceCreate(value))),
                Try(() => Describe(referenceArray(value))), Try(() => Describe(referenceMember(value))));
            string actual = string.Join("\n", Try(() => Describe(convert(value))), Try(() => Describe(create(value))),
                Try(() => Describe(array(value))), Try(() => Describe(member(value))));
            ParityAssert.Same(expected, actual, "implicit_" + typeof(T).Name + "_" + Array.IndexOf(values, value));
        }
    }

    [Fact]
    public void BoolConverts()
    {
        Same(new[] { true, false }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
        Same(new bool?[] { null, true }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
    }

    [Fact]
    public void ByteConverts()
    {
        Same(new byte[] { 0, 7, 255 }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
        Same(new byte?[] { null, 9 }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
    }

    [Fact]
    public void CharConverts()
    {
        Same(new[] { 'a', '"', '\\', '\n', '\0', '<', '+', '\u00e9', '\ud800', '\uffff' }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
        Same(new char?[] { null, 'z' }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
    }

    [Fact]
    public void DateTimeConverts()
    {
        Same(new[] { utc, local, unspecified, DateTime.MinValue, DateTime.MaxValue }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
        Same(new DateTime?[] { null, utc }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
    }

    [Fact]
    public void DateTimeOffsetConverts()
    {
        Same(new[] { offset, DateTimeOffset.MinValue, DateTimeOffset.MaxValue, new DateTimeOffset(utc) }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
        Same(new DateTimeOffset?[] { null, offset }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
    }

    [Fact]
    public void DecimalConverts()
    {
        Same(new[] { 0m, 2m, -1.50m, 0.0000001m, decimal.MaxValue, decimal.MinValue, 1e-28m }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
        Same(new decimal?[] { null, 3.25m }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
    }

    [Fact]
    public void DoubleConverts()
    {
        Same(new[] { 0.0, -0.0, 1.5, 2.5, 0.1 + 0.2, 1e300, double.Epsilon, double.MaxValue, double.NaN, double.PositiveInfinity, double.NegativeInfinity }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
        Same(new double?[] { null, 1.25, double.NaN }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
    }

    [Fact]
    public void GuidConverts()
    {
        Same(new[] { sampleGuid, Guid.Empty }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
        Same(new Guid?[] { null, sampleGuid }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
    }

    [Fact]
    public void Int16Converts()
    {
        Same(new short[] { 0, -3, short.MinValue, short.MaxValue }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
        Same(new short?[] { null, 12 }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
    }

    [Fact]
    public void Int32Converts()
    {
        Same(new[] { 0, 1, -1, int.MinValue, int.MaxValue }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
        Same(new int?[] { null, 5 }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
    }

    [Fact]
    public void Int64Converts()
    {
        Same(new[] { 0L, 1L << 40, long.MinValue, long.MaxValue }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
        Same(new long?[] { null, -7 }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
    }

    [Fact]
    public void SByteConverts()
    {
        Same(new sbyte[] { 0, -5, sbyte.MinValue, sbyte.MaxValue }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
        Same(new sbyte?[] { null, 3 }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
    }

    [Fact]
    public void SingleConverts()
    {
        Same(new[] { 0f, 1.5f, 0.1f, -0f, float.MaxValue, float.Epsilon, 3.4028235E+38f, float.NaN, float.PositiveInfinity, float.NegativeInfinity }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
        Same(new float?[] { null, 2.5f }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
    }

    [Fact]
    public void StringConverts()
    {
        Same(new string?[] { null, "", "plain", "quote \" back \\ <tag> & 'x' + \u00e9 \ud83d\ude00 \u2028", "\ud800" }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
    }

    [Fact]
    public void UInt16Converts()
    {
        Same(new ushort[] { 0, 1, ushort.MaxValue }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
        Same(new ushort?[] { null, 4 }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
    }

    [Fact]
    public void UInt32Converts()
    {
        Same(new uint[] { 0, 1, uint.MaxValue }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
        Same(new uint?[] { null, 4 }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
    }

    [Fact]
    public void UInt64Converts()
    {
        Same(new ulong[] { 0, 1, ulong.MaxValue }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
        Same(new ulong?[] { null, 4 }, v => v, v => v, v => JsonValue.Create(v), v => SN.JsonValue.Create(v), v => new JsonArray(v, v), v => new SN.JsonArray(v, v), v => new JsonObject { ["x"] = v }, v => new SN.JsonObject { ["x"] = v });
    }

    // every node a conversion may meet: created from each type, parsed from text, containers and null
    static readonly (string Name, Func<JsonNode?> Feeder, Func<SN.JsonNode?> Reference)[] sources =
    {
        ("null", () => null, () => null),
        ("bool", () => true, () => true),
        ("byte", () => (byte)200, () => (byte)200),
        ("char", () => 'x', () => 'x'),
        ("datetime", () => utc, () => utc),
        ("datetimeoffset", () => offset, () => offset),
        ("decimal", () => 1.5m, () => 1.5m),
        ("double", () => 2.5, () => 2.5),
        ("double-nan", () => double.NaN, () => double.NaN),
        ("guid", () => sampleGuid, () => sampleGuid),
        ("short", () => (short)-3, () => (short)-3),
        ("int", () => 42, () => 42),
        ("long", () => 1L << 40, () => 1L << 40),
        ("sbyte", () => (sbyte)-5, () => (sbyte)-5),
        ("float", () => 0.1f, () => 0.1f),
        ("string", () => "text", () => "text"),
        ("string-digits", () => "5", () => "5"),
        ("string-char", () => "c", () => "c"),
        ("string-date", () => "2024-01-02T03:04:05Z", () => "2024-01-02T03:04:05Z"),
        ("ushort", () => (ushort)65535, () => (ushort)65535),
        ("uint", () => uint.MaxValue, () => uint.MaxValue),
        ("ulong", () => ulong.MaxValue, () => ulong.MaxValue),
        ("parsed-42", () => JsonNode.Parse("42"), () => SN.JsonNode.Parse("42")),
        ("parsed-neg", () => JsonNode.Parse("-1"), () => SN.JsonNode.Parse("-1")),
        ("parsed-300", () => JsonNode.Parse("300"), () => SN.JsonNode.Parse("300")),
        ("parsed-70000", () => JsonNode.Parse("70000"), () => SN.JsonNode.Parse("70000")),
        ("parsed-fraction", () => JsonNode.Parse("1.5"), () => SN.JsonNode.Parse("1.5")),
        ("parsed-exponent", () => JsonNode.Parse("1e3"), () => SN.JsonNode.Parse("1e3")),
        ("parsed-small", () => JsonNode.Parse("2.5e-3"), () => SN.JsonNode.Parse("2.5e-3")),
        ("parsed-ulong-max", () => JsonNode.Parse("18446744073709551615"), () => SN.JsonNode.Parse("18446744073709551615")),
        ("parsed-long-min", () => JsonNode.Parse("-9223372036854775808"), () => SN.JsonNode.Parse("-9223372036854775808")),
        ("parsed-minus-zero", () => JsonNode.Parse("-0"), () => SN.JsonNode.Parse("-0")),
        ("parsed-huge", () => JsonNode.Parse("1e400"), () => SN.JsonNode.Parse("1e400")),
        ("parsed-decimal-overflow", () => JsonNode.Parse("1e30"), () => SN.JsonNode.Parse("1e30")),
        ("parsed-text", () => JsonNode.Parse("\"text\""), () => SN.JsonNode.Parse("\"text\"")),
        ("parsed-digits", () => JsonNode.Parse("\"5\""), () => SN.JsonNode.Parse("\"5\"")),
        ("parsed-char", () => JsonNode.Parse("\"c\""), () => SN.JsonNode.Parse("\"c\"")),
        ("parsed-escaped-char", () => JsonNode.Parse("\"\\u00e9\""), () => SN.JsonNode.Parse("\"\\u00e9\"")),
        ("parsed-empty", () => JsonNode.Parse("\"\""), () => SN.JsonNode.Parse("\"\"")),
        ("parsed-date", () => JsonNode.Parse("\"2024-01-02T03:04:05Z\""), () => SN.JsonNode.Parse("\"2024-01-02T03:04:05Z\"")),
        ("parsed-date-offset", () => JsonNode.Parse("\"2024-01-02T03:04:05.5+02:00\""), () => SN.JsonNode.Parse("\"2024-01-02T03:04:05.5+02:00\"")),
        ("parsed-date-only", () => JsonNode.Parse("\"2024-01-02\""), () => SN.JsonNode.Parse("\"2024-01-02\"")),
        ("parsed-bad-date", () => JsonNode.Parse("\"2024-13-02\""), () => SN.JsonNode.Parse("\"2024-13-02\"")),
        ("parsed-guid", () => JsonNode.Parse("\"0a1b2c3d-4e5f-6071-8293-a4b5c6d7e8f9\""), () => SN.JsonNode.Parse("\"0a1b2c3d-4e5f-6071-8293-a4b5c6d7e8f9\"")),
        ("parsed-guid-n", () => JsonNode.Parse("\"0a1b2c3d4e5f60718293a4b5c6d7e8f9\""), () => SN.JsonNode.Parse("\"0a1b2c3d4e5f60718293a4b5c6d7e8f9\"")),
        ("parsed-true", () => JsonNode.Parse("true"), () => SN.JsonNode.Parse("true")),
        ("parsed-false", () => JsonNode.Parse("false"), () => SN.JsonNode.Parse("false")),
        ("parsed-null", () => JsonNode.Parse("null"), () => SN.JsonNode.Parse("null")),
        ("parsed-object", () => JsonNode.Parse("{\"a\":1}"), () => SN.JsonNode.Parse("{\"a\":1}")),
        ("parsed-array", () => JsonNode.Parse("[1]"), () => SN.JsonNode.Parse("[1]")),
        ("object", () => new JsonObject(), () => new SN.JsonObject()),
        ("array", () => new JsonArray(), () => new SN.JsonArray()),
        ("member", () => JsonNode.Parse("{\"v\":7}")!["v"], () => SN.JsonNode.Parse("{\"v\":7}")!["v"]),
    };

    static readonly (string Name, Func<JsonNode?, object?> Feeder, Func<SN.JsonNode?, object?> Reference)[] targets =
    {
        ("bool", n => (bool)n!, n => (bool)n!),
        ("bool?", n => (bool?)n, n => (bool?)n),
        ("byte", n => (byte)n!, n => (byte)n!),
        ("byte?", n => (byte?)n, n => (byte?)n),
        ("char", n => (char)n!, n => (char)n!),
        ("char?", n => (char?)n, n => (char?)n),
        ("DateTime", n => (DateTime)n!, n => (DateTime)n!),
        ("DateTime?", n => (DateTime?)n, n => (DateTime?)n),
        ("DateTimeOffset", n => (DateTimeOffset)n!, n => (DateTimeOffset)n!),
        ("DateTimeOffset?", n => (DateTimeOffset?)n, n => (DateTimeOffset?)n),
        ("decimal", n => (decimal)n!, n => (decimal)n!),
        ("decimal?", n => (decimal?)n, n => (decimal?)n),
        ("double", n => (double)n!, n => (double)n!),
        ("double?", n => (double?)n, n => (double?)n),
        ("Guid", n => (Guid)n!, n => (Guid)n!),
        ("Guid?", n => (Guid?)n, n => (Guid?)n),
        ("short", n => (short)n!, n => (short)n!),
        ("short?", n => (short?)n, n => (short?)n),
        ("int", n => (int)n!, n => (int)n!),
        ("int?", n => (int?)n, n => (int?)n),
        ("long", n => (long)n!, n => (long)n!),
        ("long?", n => (long?)n, n => (long?)n),
        ("sbyte", n => (sbyte)n!, n => (sbyte)n!),
        ("sbyte?", n => (sbyte?)n, n => (sbyte?)n),
        ("float", n => (float)n!, n => (float)n!),
        ("float?", n => (float?)n, n => (float?)n),
        ("string", n => (string?)n, n => (string?)n),
        ("ushort", n => (ushort)n!, n => (ushort)n!),
        ("ushort?", n => (ushort?)n, n => (ushort?)n),
        ("uint", n => (uint)n!, n => (uint)n!),
        ("uint?", n => (uint?)n, n => (uint?)n),
        ("ulong", n => (ulong)n!, n => (ulong)n!),
        ("ulong?", n => (ulong?)n, n => (ulong?)n),
    };

    public static IEnumerable<object[]> TargetNames() => targets.Select(t => new object[] { t.Name });

    static string Format(object? value) => value switch
    {
        null => "(null)",
        double d => "double:" + Bits(d),
        float f => "float:" + Bits(f),
        decimal m => "decimal:" + Bits(m),
        DateTime t => "DateTime:" + t.ToString("O", CultureInfo.InvariantCulture) + "/" + t.Kind,
        DateTimeOffset o => "DateTimeOffset:" + o.ToString("O", CultureInfo.InvariantCulture),
        string s => "string:" + Show(s),
        char c => "char:" + ((int)c).ToString(CultureInfo.InvariantCulture),
        _ => value.GetType().Name + ":" + Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    [Theory]
    [MemberData(nameof(TargetNames))]
    public void ExplicitConversionMatches(string target)
    {
        var conversion = targets.First(t => t.Name == target);
        var expected = new List<string>();
        var actual = new List<string>();
        foreach (var source in sources)
        {
            expected.Add(source.Name + " -> " + Try(() => Format(conversion.Reference(source.Reference()))));
            actual.Add(source.Name + " -> " + Try(() => Format(conversion.Feeder(source.Feeder()))));
        }
        ParityAssert.Same(string.Join("\n", expected), string.Join("\n", actual), "explicit_" + target);
    }

    // TryGetValue for each type; the out value only counts when the call succeeds
    [Fact]
    public void TryGetValueMatchesForEveryType()
    {
        foreach (var source in sources)
        {
            string expected = Try(() => source.Reference() is SN.JsonValue v ? ReferenceTries(v) : "not a value");
            string actual = Try(() => source.Feeder() is JsonValue v ? FeederTries(v) : "not a value");
            ParityAssert.Same(expected, actual, "tryget_" + source.Name);
        }
    }

    static string Tried<T>(bool success, T value) => success ? Format(value) : "-";

    static string ReferenceTries(SN.JsonValue v) => string.Join(" ",
        Tried(v.TryGetValue(out bool a), a), Tried(v.TryGetValue(out byte b), b), Tried(v.TryGetValue(out char c), c), Tried(v.TryGetValue(out DateTime d), d),
        Tried(v.TryGetValue(out DateTimeOffset e), e), Tried(v.TryGetValue(out decimal f), f), Tried(v.TryGetValue(out double g), g), Tried(v.TryGetValue(out Guid h), h),
        Tried(v.TryGetValue(out short i), i), Tried(v.TryGetValue(out int j), j), Tried(v.TryGetValue(out long k), k), Tried(v.TryGetValue(out sbyte l), l),
        Tried(v.TryGetValue(out float m), m), Tried(v.TryGetValue(out string? n), n), Tried(v.TryGetValue(out ushort o), o), Tried(v.TryGetValue(out uint p), p),
        Tried(v.TryGetValue(out ulong q), q), Tried(v.TryGetValue(out int? r), r), Tried(v.TryGetValue(out Guid? s), s), Tried(v.TryGetValue(out object? t), t?.GetType().Name));

    static string FeederTries(JsonValue v) => string.Join(" ",
        Tried(v.TryGetValue(out bool a), a), Tried(v.TryGetValue(out byte b), b), Tried(v.TryGetValue(out char c), c), Tried(v.TryGetValue(out DateTime d), d),
        Tried(v.TryGetValue(out DateTimeOffset e), e), Tried(v.TryGetValue(out decimal f), f), Tried(v.TryGetValue(out double g), g), Tried(v.TryGetValue(out Guid h), h),
        Tried(v.TryGetValue(out short i), i), Tried(v.TryGetValue(out int j), j), Tried(v.TryGetValue(out long k), k), Tried(v.TryGetValue(out sbyte l), l),
        Tried(v.TryGetValue(out float m), m), Tried(v.TryGetValue(out string? n), n), Tried(v.TryGetValue(out ushort o), o), Tried(v.TryGetValue(out uint p), p),
        Tried(v.TryGetValue(out ulong q), q), Tried(v.TryGetValue(out int? r), r), Tried(v.TryGetValue(out Guid? s), s),
        Tried(v.TryGetValue(out object? t), t?.GetType().Name));

    [Fact]
    public void RequestedShapesCompileAndMatch()
    {
        double someDouble = 0.1;
        string expected = Try(() =>
        {
            var pair = new SN.JsonArray(1.5, 2.5);
            var triple = new SN.JsonArray(someDouble, someDouble, someDouble);
            var obj = new SN.JsonObject { ["x"] = 1.5f, ["d"] = 2m };
            SN.JsonNode node = SN.JsonNode.Parse("{\"name\":\"n\",\"v\":2.5,\"i\":7}")!;
            var s = (string)node["name"]!;
            double d = (double)node["v"]!;
            int? i = (int?)node["i"];
            node["k"] = (long?)null;
            var nodes = new SN.JsonArray(new SN.JsonObject { ["a"] = 1 }, null, "s", 3);
            var withOptions = new SN.JsonArray(new SN.JsonNodeOptions { PropertyNameCaseInsensitive = true }, new SN.JsonObject { ["A"] = 1 });
            return string.Join("|", pair.ToJsonString(), triple.ToJsonString(), obj.ToJsonString(), s, Bits(d), i, node.ToJsonString(), nodes.ToJsonString(),
                withOptions[0]!["a"]?.ToJsonString() ?? "(null)", new SN.JsonArray().Count, new SN.JsonArray(new SN.JsonNode?[] { 1, 2 }).ToJsonString());
        });
        string actual = Try(() =>
        {
            var pair = new JsonArray(1.5, 2.5);
            var triple = new JsonArray(someDouble, someDouble, someDouble);
            var obj = new JsonObject { ["x"] = 1.5f, ["d"] = 2m };
            JsonNode node = JsonNode.Parse("{\"name\":\"n\",\"v\":2.5,\"i\":7}")!;
            var s = (string)node["name"]!;
            double d = (double)node["v"]!;
            int? i = (int?)node["i"];
            node["k"] = (long?)null;
            var nodes = new JsonArray(new JsonObject { ["a"] = 1 }, null, "s", 3);
            var withOptions = new JsonArray(new JsonNodeOptions { PropertyNameCaseInsensitive = true }, new JsonObject { ["A"] = 1 });
            return string.Join("|", pair.ToJsonString(), triple.ToJsonString(), obj.ToJsonString(), s, Bits(d), i, node.ToJsonString(), nodes.ToJsonString(),
                withOptions[0]!["a"]?.ToJsonString() ?? "(null)", new JsonArray().Count, new JsonArray(new JsonNode?[] { 1, 2 }).ToJsonString());
        });
        ParityAssert.Same(expected, actual, "requested_shapes");
    }

    [Fact]
    public void ParamsArrayRefusesNodesThatAlreadyHaveAParent()
    {
        string expected = Try(() =>
        {
            SN.JsonNode shared = 5;
            var owner = new SN.JsonArray(shared);
            string first = Try(() => new SN.JsonArray(1, shared).ToJsonString());
            string again = Try(() => new SN.JsonArray(shared, shared).ToJsonString());
            return first + "|" + again + "|" + owner.ToJsonString();
        });
        string actual = Try(() =>
        {
            JsonNode shared = 5;
            var owner = new JsonArray(shared);
            string first = Try(() => new JsonArray(1, shared).ToJsonString());
            string again = Try(() => new JsonArray(shared, shared).ToJsonString());
            return first + "|" + again + "|" + owner.ToJsonString();
        });
        Assert.Equal(expected, actual);
    }
}
