using System.Text;
using static Feeder.Json.ParityTests.TestSupport;
using S = System.Text.Json;

namespace Feeder.Json.ParityTests;

public class WriterParityTests
{
    internal interface IWriter
    {
        void StartObject();
        void StartArray();
        void StartObject(string name);
        void StartArray(string name);
        void EndObject();
        void EndArray();
        void Name(string name);
        void String(string name, string? value);
        void Number(string name, int value);
        void Number(string name, long value);
        void Number(string name, uint value);
        void Number(string name, ulong value);
        void Number(string name, float value);
        void Number(string name, double value);
        void Number(string name, decimal value);
        void Boolean(string name, bool value);
        void Null(string name);
        void StringValue(string? value);
        void NumberValue(int value);
        void NumberValue(long value);
        void NumberValue(uint value);
        void NumberValue(ulong value);
        void NumberValue(float value);
        void NumberValue(double value);
        void NumberValue(decimal value);
        void BooleanValue(bool value);
        void NullValue();
        int Depth { get; }
    }

    sealed class FeederWriter : IWriter
    {
        readonly Utf8JsonWriter w;
        public FeederWriter(Utf8JsonWriter w) => this.w = w;
        public void StartObject() => w.WriteStartObject();
        public void StartArray() => w.WriteStartArray();
        public void StartObject(string name) => w.WriteStartObject(name);
        public void StartArray(string name) => w.WriteStartArray(name);
        public void EndObject() => w.WriteEndObject();
        public void EndArray() => w.WriteEndArray();
        public void Name(string name) => w.WritePropertyName(name);
        public void String(string name, string? value) => w.WriteString(name, value);
        public void Number(string name, int value) => w.WriteNumber(name, value);
        public void Number(string name, long value) => w.WriteNumber(name, value);
        public void Number(string name, uint value) => w.WriteNumber(name, value);
        public void Number(string name, ulong value) => w.WriteNumber(name, value);
        public void Number(string name, float value) => w.WriteNumber(name, value);
        public void Number(string name, double value) => w.WriteNumber(name, value);
        public void Number(string name, decimal value) => w.WriteNumber(name, value);
        public void Boolean(string name, bool value) => w.WriteBoolean(name, value);
        public void Null(string name) => w.WriteNull(name);
        public void StringValue(string? value) => w.WriteStringValue(value);
        public void NumberValue(int value) => w.WriteNumberValue(value);
        public void NumberValue(long value) => w.WriteNumberValue(value);
        public void NumberValue(uint value) => w.WriteNumberValue(value);
        public void NumberValue(ulong value) => w.WriteNumberValue(value);
        public void NumberValue(float value) => w.WriteNumberValue(value);
        public void NumberValue(double value) => w.WriteNumberValue(value);
        public void NumberValue(decimal value) => w.WriteNumberValue(value);
        public void BooleanValue(bool value) => w.WriteBooleanValue(value);
        public void NullValue() => w.WriteNullValue();
        public int Depth => w.CurrentDepth;
    }

    sealed class ReferenceWriter : IWriter
    {
        readonly S.Utf8JsonWriter w;
        public ReferenceWriter(S.Utf8JsonWriter w) => this.w = w;
        public void StartObject() => w.WriteStartObject();
        public void StartArray() => w.WriteStartArray();
        public void StartObject(string name) => w.WriteStartObject(name);
        public void StartArray(string name) => w.WriteStartArray(name);
        public void EndObject() => w.WriteEndObject();
        public void EndArray() => w.WriteEndArray();
        public void Name(string name) => w.WritePropertyName(name);
        public void String(string name, string? value) => w.WriteString(name, value);
        public void Number(string name, int value) => w.WriteNumber(name, value);
        public void Number(string name, long value) => w.WriteNumber(name, value);
        public void Number(string name, uint value) => w.WriteNumber(name, value);
        public void Number(string name, ulong value) => w.WriteNumber(name, value);
        public void Number(string name, float value) => w.WriteNumber(name, value);
        public void Number(string name, double value) => w.WriteNumber(name, value);
        public void Number(string name, decimal value) => w.WriteNumber(name, value);
        public void Boolean(string name, bool value) => w.WriteBoolean(name, value);
        public void Null(string name) => w.WriteNull(name);
        public void StringValue(string? value) => w.WriteStringValue(value);
        public void NumberValue(int value) => w.WriteNumberValue(value);
        public void NumberValue(long value) => w.WriteNumberValue(value);
        public void NumberValue(uint value) => w.WriteNumberValue(value);
        public void NumberValue(ulong value) => w.WriteNumberValue(value);
        public void NumberValue(float value) => w.WriteNumberValue(value);
        public void NumberValue(double value) => w.WriteNumberValue(value);
        public void NumberValue(decimal value) => w.WriteNumberValue(value);
        public void BooleanValue(bool value) => w.WriteBooleanValue(value);
        public void NullValue() => w.WriteNullValue();
        public int Depth => w.CurrentDepth;
    }

    static readonly Dictionary<string, JsonWriterOptions> optionSets = new()
    {
        ["default"] = new JsonWriterOptions(),
        ["indented"] = new JsonWriterOptions { Indented = true },
        ["relaxed"] = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping },
        ["relaxed-indented"] = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = true },
        ["default-encoder"] = new JsonWriterOptions { Encoder = JavaScriptEncoder.Default, Indented = true },
        ["skip"] = new JsonWriterOptions { SkipValidation = true },
        ["skip-indented"] = new JsonWriterOptions { SkipValidation = true, Indented = true },
        ["depth3"] = new JsonWriterOptions { MaxDepth = 3 },
    };

    static readonly string specials = new string(Enumerable.Range(0, 0x80).Select(i => (char)i).ToArray())
        + "caf" + (char)0xE9 + " " + (char)0x65E5 + (char)0x672C + " " + (char)0xD83D + (char)0xDE00 + " " + (char)0xA0 + (char)0x2014 + (char)0x2028
        + (char)0x2029 + (char)0xFEFF + (char)0xAD + (char)0x200B + (char)0xFFFD + (char)0xFFFE + (char)0x0897 + " lone:" + (char)0xD800 + "x" + (char)0xDC00
        + (char)0xDBFF + (char)0xD800 + (char)0xDC00 + "end" + (char)0xD800;

    static readonly double[] doubles =
    {
        0, -0.0, 1, -1, 0.1, 0.2, 0.30000000000000004, 1.5, 100, 1e15, 1e16, 1e17, 1e21, 1e22, 1e23, 123456789012345678, 9007199254740993,
        1e-4, 1e-5, 1.234e-5, 5e-324, double.Epsilon, double.MaxValue, double.MinValue, 2.2250738585072014E-308, 2.225073858507201E-308,
        Math.PI, Math.E, 1.0 / 3, 2.0 / 3, 0.1f, 3.4028234663852886E+38, 1e308, 123.456, -987654321.123456789,
    };

    static readonly float[] floats =
    {
        0, -0f, 1, 0.1f, 1.1f, 0.3f, 1e6f, 1e7f, 1e8f, 1e9f, 16777216f, 16777217f, 123456789f, 1.5e9f, 1e-4f, 1e-5f, 1.234e-5f,
        float.Epsilon, float.MaxValue, float.MinValue, 1.17549435E-38f, (float)Math.PI, 7.0e-45f, 3.3333333f,
    };

    static readonly decimal[] decimals =
    {
        0m, 1m, -1m, 1.50m, 0.0m, -0.0m, new decimal(0, 0, 0, true, 0), new decimal(0, 0, 0, true, 5), decimal.MaxValue, decimal.MinValue,
        0.0000000000000000000000000001m, 1e-28m * 10, 123456789.123456789m, 1m / 3, 79228162514264337593543950335m / 10000m,
    };

    static readonly Dictionary<string, Action<IWriter>> scripts = new()
    {
        ["object_all_property_overloads"] = w =>
        {
            w.StartObject();
            w.String("s", "text");
            w.String("n", null);
            w.Number("i", int.MinValue);
            w.Number("l", long.MinValue);
            w.Number("u", uint.MaxValue);
            w.Number("ul", ulong.MaxValue);
            w.Number("f", 1.1f);
            w.Number("d", 0.1);
            w.Number("m", 1.50m);
            w.Boolean("t", true);
            w.Boolean("fa", false);
            w.Null("nu");
            w.Name("arr");
            w.StartArray();
            w.EndArray();
            w.StartObject("obj");
            w.StartArray("inner");
            w.NumberValue(1);
            w.StartObject();
            w.EndObject();
            w.StartArray();
            w.StartArray();
            w.EndArray();
            w.EndArray();
            w.EndArray();
            w.EndObject();
            w.Name("last");
            w.StartObject();
            w.EndObject();
            w.EndObject();
        },
        ["array_all_value_overloads"] = w =>
        {
            w.StartArray();
            w.StringValue("s");
            w.StringValue(null);
            w.NumberValue(int.MaxValue);
            w.NumberValue(long.MaxValue);
            w.NumberValue(uint.MaxValue);
            w.NumberValue(ulong.MaxValue);
            w.NumberValue(1.1f);
            w.NumberValue(0.1);
            w.NumberValue(-0.0m);
            w.BooleanValue(true);
            w.BooleanValue(false);
            w.NullValue();
            w.EndArray();
        },
        ["root_primitives"] = w => w.NumberValue(42),
        ["root_string"] = w => w.StringValue("<root>"),
        ["root_null"] = w => w.NullValue(),
        ["special_strings"] = w =>
        {
            w.StartObject();
            w.String(specials, specials);
            w.Name(specials);
            w.StringValue(specials);
            w.StartArray(specials);
            w.StringValue("");
            w.StringValue("plain ascii with spaces");
            w.EndArray();
            w.EndObject();
        },
        ["doubles"] = w =>
        {
            w.StartObject();
            foreach (double d in doubles)
            {
                w.Number("d", d);
                w.Name("v");
                w.NumberValue(d);
            }
            w.EndObject();
        },
        ["floats"] = w =>
        {
            w.StartArray();
            foreach (float f in floats)
                w.NumberValue(f);
            w.EndArray();
        },
        ["decimals"] = w =>
        {
            w.StartObject();
            foreach (decimal m in decimals)
                w.Number("m", m);
            w.Name("a");
            w.StartArray();
            foreach (decimal m in decimals)
                w.NumberValue(m);
            w.EndArray();
            w.EndObject();
        },
        ["error_value_in_object"] = w =>
        {
            w.StartObject();
            w.NumberValue(1);
        },
        ["error_name_in_array"] = w =>
        {
            w.StartArray();
            w.Name("a");
        },
        ["error_name_at_root"] = w => w.Name("a"),
        ["error_property_value_in_array"] = w =>
        {
            w.StartArray();
            w.String("a", "b");
        },
        ["error_start_named_in_array"] = w =>
        {
            w.StartArray();
            w.StartObject("a");
        },
        ["error_end_mismatch"] = w =>
        {
            w.StartObject();
            w.EndArray();
        },
        ["error_end_mismatch_array"] = w =>
        {
            w.StartArray();
            w.EndObject();
        },
        ["error_end_after_name"] = w =>
        {
            w.StartObject();
            w.Name("a");
            w.EndObject();
        },
        ["error_name_after_name"] = w =>
        {
            w.StartObject();
            w.Name("a");
            w.Name("b");
        },
        ["error_end_at_root"] = w => w.EndArray(),
        ["error_two_roots"] = w =>
        {
            w.NumberValue(1);
            w.NumberValue(2);
            w.StartObject();
            w.EndObject();
            w.StartArray();
            w.EndArray();
        },
        ["error_two_root_containers"] = w =>
        {
            w.StartObject();
            w.EndObject();
            w.StartObject();
            w.EndObject();
        },
        ["error_nan"] = w =>
        {
            w.StartArray();
            w.NumberValue(double.NaN);
        },
        ["error_infinity_property"] = w =>
        {
            w.StartObject();
            w.Number("x", double.PositiveInfinity);
        },
        ["error_float_infinity"] = w =>
        {
            w.StartArray();
            w.NumberValue(float.NegativeInfinity);
        },
        ["error_null_property_name"] = w =>
        {
            w.StartObject();
            w.String(null!, "x");
        },
        ["error_null_name"] = w =>
        {
            w.StartObject();
            w.Name(null!);
        },
        ["error_null_start_name"] = w =>
        {
            w.StartObject();
            w.StartArray(null!);
        },
        ["deep_nesting"] = w =>
        {
            for (int i = 0; i < 70; i++)
            {
                if (i % 2 == 0)
                {
                    w.StartArray();
                }
                else
                {
                    w.StartObject();
                    w.Name("k" + i);
                }
            }
            w.StringValue("bottom");
            for (int i = 69; i >= 0; i--)
            {
                if (i % 2 == 0)
                    w.EndArray();
                else
                    w.EndObject();
            }
        },
        ["values_after_close_in_array"] = w =>
        {
            w.StartArray();
            w.StartObject();
            w.EndObject();
            w.StartArray();
            w.EndArray();
            w.NumberValue(1);
            w.StartObject();
            w.Name("x");
            w.NullValue();
            w.EndObject();
            w.EndArray();
        },
        ["unbalanced_end"] = w =>
        {
            w.StartArray();
            w.EndArray();
            w.EndArray();
            w.EndObject();
        },
    };

    public static IEnumerable<object[]> Cases() =>
        from script in scripts.Keys from set in optionSets.Keys select new object[] { script, set };

    [Theory]
    [MemberData(nameof(Cases))]
    public void OutputMatches(string script, string optionSet)
    {
        JsonWriterOptions options = optionSets[optionSet];
        Assert.Equal(RunReference(scripts[script], ToReference(options)), Run(scripts[script], options));
    }

    static string Run(Action<IWriter> script, JsonWriterOptions options)
    {
        var ms = new MemoryStream();
        var w = new Utf8JsonWriter(ms, options);
        string outcome = Outcome(() => script(new FeederWriter(w)));
        int depth = w.CurrentDepth;
        w.Flush();
        string flushed = Show(ms.ToArray());
        w.Dispose();
        return outcome + " depth=" + depth + " out=" + flushed;
    }

    static string RunReference(Action<IWriter> script, S.JsonWriterOptions options)
    {
        var ms = new MemoryStream();
        var w = new S.Utf8JsonWriter(ms, options);
        string outcome = Outcome(() => script(new ReferenceWriter(w)));
        int depth = w.CurrentDepth;
        w.Flush();
        string flushed = Show(ms.ToArray());
        w.Dispose();
        return outcome + " depth=" + depth + " out=" + flushed;
    }

    static string Outcome(Action action)
    {
        try
        {
            action();
            return "ok";
        }
        catch (Exception e)
        {
            return "!" + Name(e);
        }
    }

    [Fact]
    public void BuffersUntilFlushAndDisposeFlushes()
    {
        var ms = new MemoryStream();
        var w = new Utf8JsonWriter(ms);
        w.WriteStartArray();
        w.WriteNumberValue(1);
        Assert.Equal(0, ms.Length);
        w.Flush();
        Assert.Equal("[1", Encoding.UTF8.GetString(ms.ToArray()));
        w.WriteEndArray();
        w.Dispose();
        Assert.Equal("[1]", Encoding.UTF8.GetString(ms.ToArray()));
        w.Dispose();
        Assert.Throws<ObjectDisposedException>(() => w.Flush());
        Assert.True(ms.CanWrite);

        var open = new Utf8JsonWriter(new MemoryStream());
        open.WriteStartArray();
        open.Dispose();
        Assert.Throws<ObjectDisposedException>(() => open.WriteNullValue());
    }

    [Fact]
    public void ConstructorChecksMatch()
    {
        Assert.Equal(Try(() => new S.Utf8JsonWriter((Stream)null!).ToString()!), Try(() => new Utf8JsonWriter(null!).ToString()!));
        var readOnly = new MemoryStream(new byte[4], false);
        Assert.Equal(Try(() => new S.Utf8JsonWriter(readOnly).ToString()!), Try(() => new Utf8JsonWriter(readOnly).ToString()!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new JsonWriterOptions { MaxDepth = -1 });
    }

    [Fact]
    public void LargeStringsMatch()
    {
        var sb = new StringBuilder();
        var random = new Random(7);
        for (int i = 0; i < 200_000; i++)
            sb.Append((char)(random.Next(4) == 0 ? random.Next(0x20, 0x3000) : random.Next(0x20, 0x7F)));
        string big = sb.ToString() + Convert.ToBase64String(Enumerable.Range(0, 300_000).Select(i => (byte)(i * 31)).ToArray());
        foreach (JsonWriterOptions options in new[] { new JsonWriterOptions(), new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping } })
        {
            string expected = RunReference(w =>
            {
                w.StartObject();
                w.String("data", big);
                w.EndObject();
            }, ToReference(options));
            string actual = Run(w =>
            {
                w.StartObject();
                w.String("data", big);
                w.EndObject();
            }, options);
            Assert.True(expected == actual, "large string output differs");
        }
    }
}
