using System.Globalization;
using System.Numerics;
using System.Text;
using static Feeder.Json.ParityTests.TestSupport;
using S = System.Text.Json;

namespace Feeder.Json.ParityTests;

public class NumberParityTests
{
    static double RandomDouble(Random random)
    {
        while (true)
        {
            double d = BitConverter.Int64BitsToDouble(random.NextInt64() ^ ((long)random.Next() << 33));
            if (double.IsFinite(d))
                return d;
        }
    }

    static float RandomSingle(Random random)
    {
        while (true)
        {
            float f = BitConverter.Int32BitsToSingle(random.Next() ^ (random.Next(2) << 31));
            if (float.IsFinite(f))
                return f;
        }
    }

    static IEnumerable<double> DoubleSamples()
    {
        var random = new Random(12345);
        for (int i = 0; i < 300_000; i++)
            yield return RandomDouble(random);
        for (int e = -1074; e <= 1023; e++)
        {
            double p = Math.ScaleB(1, e);
            yield return p;
            yield return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(p) + 1);
            if (e > -1074)
                yield return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(p) - 1);
        }
        for (int e = -323; e <= 308; e++)
        {
            double p = double.Parse("1e" + e, CultureInfo.InvariantCulture);
            yield return p;
            yield return -BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(p) + 1);
            yield return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(p) - 1);
        }
        for (int i = 0; i < 20_000; i++)
        {
            yield return i;
            yield return i / 1000.0;
            yield return random.Next() / Math.Pow(10, random.Next(0, 20));
            yield return random.NextDouble();
            yield return (float)random.NextDouble();
        }
        yield return 1e23;
        yield return 9007199254740993;
        yield return double.MaxValue;
        yield return double.MinValue;
        yield return double.Epsilon;
        yield return -0.0;
        yield return 0.0;
    }

    static IEnumerable<float> SingleSamples()
    {
        var random = new Random(54321);
        for (int i = 0; i < 300_000; i++)
            yield return RandomSingle(random);
        for (int e = -149; e <= 127; e++)
        {
            float p = MathF.ScaleB(1, e);
            yield return p;
            yield return BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(p) + 1);
            yield return -BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(p) - 1);
        }
        for (int i = 0; i < 20_000; i++)
        {
            yield return i;
            yield return i / 1000f;
            yield return (float)random.NextDouble();
            yield return (float)(random.Next() / Math.Pow(10, random.Next(0, 12)));
        }
        yield return float.MaxValue;
        yield return float.MinValue;
        yield return float.Epsilon;
        yield return -0f;
    }

    readonly Xunit.Abstractions.ITestOutputHelper output;

    public NumberParityTests(Xunit.Abstractions.ITestOutputHelper output) => this.output = output;

    // the reference prints 2^-958 and 2^-25 with 16 digits that parse back to a different double; there the
    // shortest text that does round-trip is required instead
    [Fact]
    public void DoubleFormattingMatches()
    {
        double[] values = DoubleSamples().ToArray();
        string[] expected = Elements(WriteReference(w => { foreach (double d in values) w.WriteNumberValue(d); }));
        string[] actual = Elements(Write(w => { foreach (double d in values) w.WriteNumberValue(d); }));
        Assert.Equal(expected.Length, actual.Length);
        var exceptions = new List<string>();
        for (int i = 0; i < values.Length; i++)
        {
            if (expected[i] == actual[i])
                continue;
            Assert.True(Bits(double.Parse(expected[i], CultureInfo.InvariantCulture)) != Bits(values[i]), "element " + i + ": expected " + expected[i] + " actual " + actual[i]);
            Assert.Equal(Bits(values[i]), Bits(double.Parse(actual[i], CultureInfo.InvariantCulture)));
            exceptions.Add(Bits(values[i]) + " reference " + expected[i] + " (does not round-trip), written " + actual[i]);
        }
        foreach (string e in exceptions)
            output.WriteLine(e);
        Assert.True(exceptions.Count <= 2, string.Join("\n", exceptions));
    }

    [Fact]
    public void SingleFormattingMatches()
    {
        float[] values = SingleSamples().ToArray();
        string[] expected = Elements(WriteReference(w => { foreach (float f in values) w.WriteNumberValue(f); }));
        string[] actual = Elements(Write(w => { foreach (float f in values) w.WriteNumberValue(f); }));
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < values.Length; i++)
            Assert.True(expected[i] == actual[i], "element " + i + ": expected " + expected[i] + " actual " + actual[i]);
    }

    static string[] Elements(string array) => array.Substring(1, array.Length - 2).Split(',');

    [Fact]
    public void DecimalFormattingMatches()
    {
        var random = new Random(99);
        var values = new List<decimal>();
        for (int i = 0; i < 100_000; i++)
        {
            int hiBits = random.Next(4) switch { 0 => 0, 1 => random.Next(16), _ => random.Next() };
            values.Add(new decimal(random.Next(), random.Next(4) == 0 ? 0 : random.Next(), hiBits, random.Next(2) == 0, (byte)random.Next(29)));
            values.Add(new decimal(random.Next(1000), 0, 0, random.Next(2) == 0, (byte)random.Next(29)));
        }
        AssertSameElements(WriteReference(w => { foreach (decimal m in values) w.WriteNumberValue(m); }), Write(w => { foreach (decimal m in values) w.WriteNumberValue(m); }));
    }

    [Fact]
    public void IntegerFormattingMatches()
    {
        var random = new Random(3);
        long[] longs = new long[] { 0, -1, 1, long.MinValue, long.MaxValue, int.MinValue, int.MaxValue, 10, -10, 999999999999 }
            .Concat(Enumerable.Range(0, 20_000).Select(_ => random.NextInt64() >> random.Next(64))).ToArray();
        ulong[] ulongs = new ulong[] { 0, ulong.MaxValue, 10, 18446744073709551610 }.Concat(longs.Select(l => (ulong)l)).ToArray();
        AssertSameElements(
            WriteReference(w => { foreach (long l in longs) { w.WriteNumberValue(l); w.WriteNumberValue((int)l); w.WriteNumberValue((uint)l); } foreach (ulong u in ulongs) w.WriteNumberValue(u); }),
            Write(w => { foreach (long l in longs) { w.WriteNumberValue(l); w.WriteNumberValue((int)l); w.WriteNumberValue((uint)l); } foreach (ulong u in ulongs) w.WriteNumberValue(u); }));
    }

    [Fact]
    public void ShortestTextParsesBack()
    {
        foreach (double d in DoubleSamples().Take(50_000))
        {
            byte[] text = Encoding.ASCII.GetBytes(Write(w => w.WriteNumberValue(d))[1..^1]);
            Assert.Equal(Bits(d), Bits(JsonNumber.ParseDouble(text)));
        }
        foreach (float f in SingleSamples().Take(50_000))
        {
            byte[] text = Encoding.ASCII.GetBytes(Write(w => w.WriteNumberValue(f))[1..^1]);
            Assert.Equal(Bits(f), Bits(JsonNumber.ParseSingle(text)));
        }
    }

    [Fact]
    public void ParsingMatchesForGeneratedText()
    {
        var random = new Random(2024);
        var texts = new List<string>();
        foreach (double d in DoubleSamples().Where((_, i) => i % 7 == 0))
        {
            texts.Add(d.ToString("R", CultureInfo.InvariantCulture));
            texts.Add(d.ToString("G17", CultureInfo.InvariantCulture));
            texts.Add(d.ToString("E25", CultureInfo.InvariantCulture));
        }
        foreach (float f in SingleSamples().Where((_, i) => i % 7 == 0))
        {
            texts.Add(f.ToString("R", CultureInfo.InvariantCulture));
            texts.Add(f.ToString("G9", CultureInfo.InvariantCulture));
        }
        for (int i = 0; i < 100_000; i++)
            texts.Add(RandomNumberText(random));
        for (int i = 0; i < 4_000; i++)
        {
            double d = Math.Abs(RandomDouble(random));
            if (d != double.MaxValue)
                texts.AddRange(Midpoints(d));
            float f = Math.Abs(RandomSingle(random));
            if (f != float.MaxValue)
                texts.AddRange(Midpoints(f));
        }
        foreach (int e in new[] { -1074, -1073, -1022, -1, 0, 1, 52, 53, 1023 })
            texts.AddRange(Midpoints(Math.ScaleB(1, e)));
        texts.AddRange(Midpoints(double.MaxValue - Math.ScaleB(1, 970)));
        texts.AddRange(Midpoints(float.MaxValue - MathF.ScaleB(1, 103)));
        texts.AddRange(Midpoints((BigInteger.One << 54) - 1, 970));
        texts.AddRange(Midpoints((BigInteger.One << 25) - 1, 103));

        byte[] json = Encoding.ASCII.GetBytes("[" + string.Join(",", texts.Select(t => t.Replace("+", "", StringComparison.Ordinal).Replace("E", "e+", StringComparison.Ordinal).Replace("e+-", "e-", StringComparison.Ordinal))) + "]");
        string[] expected = ParseAllReference(json).Split('\n'), actual = ParseAll(json).Split('\n');
        Assert.Equal(expected.Length, actual.Length);
        int referenceMisrounded = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] == actual[i])
                continue;
            // the reference UTF-8 reader rounds some exact ties of long digit strings away from even; the string
            // parsers of the same runtime round them correctly and are the arbiter for those lines only
            string text = expected[i].Split(' ')[0];
            string correct = text + " " + Bits(double.Parse(text, CultureInfo.InvariantCulture)) + " " + Bits(float.Parse(text, CultureInfo.InvariantCulture));
            Assert.True(actual[i].StartsWith(correct + " ", StringComparison.Ordinal), "expected " + expected[i] + "\nactual   " + actual[i] + "\ncorrect  " + correct);
            Assert.False(expected[i].StartsWith(correct + " ", StringComparison.Ordinal));
            Assert.Equal(expected[i].Split(' ')[3], actual[i].Split(' ')[3]);
            referenceMisrounded++;
        }
        output.WriteLine(expected.Length + " numbers, reference reader misrounded " + referenceMisrounded + " exact ties");
    }

    // the table-driven paths must agree with the big-integer path everywhere, including subnormal and overflow edges
    [Fact]
    public void FastPathsAgreeWithExactPath()
    {
        var random = new Random(77);
        for (int i = 0; i < 150_000; i++)
        {
            var sb = new StringBuilder();
            if (random.Next(2) == 0)
                sb.Append('-');
            int digits = random.Next(5) == 0 ? random.Next(19, 30) : random.Next(1, 20);
            sb.Append((char)('1' + random.Next(9)));
            for (int k = 1; k < digits; k++)
                sb.Append(random.Next(4) == 0 ? '9' : (char)('0' + random.Next(10)));
            int exponent = (i % 3) switch { 0 => random.Next(-360, 330), 1 => random.Next(-70, 50), _ => random.Next(-30, 30) };
            sb.Append('e').Append(exponent);
            byte[] text = Encoding.ASCII.GetBytes(sb.ToString());
            double sign = text[0] == '-' ? -1 : 1;
            Assert.True(Bits(sign * JsonNumber.ParseExact(text, false)) == Bits(JsonNumber.ParseDouble(text)), "double " + sb);
            Assert.True(Bits((float)(sign * JsonNumber.ParseExact(text, true))) == Bits(JsonNumber.ParseSingle(text)), "single " + sb);
        }
    }

    [Fact]
    public void BoundaryTextsAgreeWithStringParsers()
    {
        string[] texts =
        {
            "2.2250738585072011e-308", "2.2250738585072012e-308", "2.2250738585072014e-308", "4.9406564584124654e-324", "2.4703282292062327e-324",
            "2.4703282292062328e-324", "1.7976931348623157e308", "1.7976931348623158e308", "1.7976931348623159e308", "179769313486231580793728971405301e276",
            "1.1754942e-38", "1.17549435e-38", "1.4012984e-45", "7.006492e-46", "7.0064923e-46", "3.4028235e38", "3.40282356e38", "3.4028236e38",
            "9007199254740993", "9007199254740993.0000000000001", "123456789012345678901234567890", "0.000000000000000000000000000000000001",
            "18446744073709551615", "18446744073709551616e-5", "1e-342", "1e-343", "1e308", "1e309", "1e-64", "1e-65", "1e38", "1e39",
        };
        foreach (string t in texts)
        {
            byte[] text = Encoding.ASCII.GetBytes(t);
            Assert.True(Bits(double.Parse(t, CultureInfo.InvariantCulture)) == Bits(JsonNumber.ParseDouble(text)), "double " + t);
            Assert.True(Bits(float.Parse(t, CultureInfo.InvariantCulture)) == Bits(JsonNumber.ParseSingle(text)), "single " + t);
        }
    }

    static string RandomNumberText(Random random)
    {
        var sb = new StringBuilder();
        if (random.Next(2) == 0)
            sb.Append('-');
        int intDigits = random.Next(1, 25);
        sb.Append(random.Next(4) == 0 ? '0' : (char)('1' + random.Next(9)));
        if (sb[^1] != '0')
        {
            for (int i = 1; i < intDigits; i++)
                sb.Append((char)('0' + random.Next(10)));
        }
        if (random.Next(2) == 0)
        {
            sb.Append('.');
            int fraction = random.Next(1, 35);
            for (int i = 0; i < fraction; i++)
                sb.Append(random.Next(5) == 0 ? '0' : (char)('0' + random.Next(10)));
        }
        if (random.Next(2) == 0)
            sb.Append(random.Next(2) == 0 ? 'e' : 'E').Append(random.Next(3) switch { 0 => "", 1 => "+", _ => "-" }).Append(random.Next(0, 400));
        return sb.ToString();
    }

    // exact decimal text of the point halfway to the next representable value, and of numbers just above and below it
    static IEnumerable<string> Midpoints(double d)
    {
        long bits = BitConverter.DoubleToInt64Bits(d);
        int exponent = (int)((bits >> 52) & 0x7FF);
        long mantissa = bits & ((1L << 52) - 1);
        if (exponent == 0)
            exponent = 1;
        else
            mantissa |= 1L << 52;
        return Midpoints(new BigInteger(mantissa) * 2 + 1, exponent - 1075 - 1);
    }

    static IEnumerable<string> Midpoints(float f)
    {
        int bits = BitConverter.SingleToInt32Bits(f);
        int exponent = (bits >> 23) & 0xFF;
        int mantissa = bits & ((1 << 23) - 1);
        if (exponent == 0)
            exponent = 1;
        else
            mantissa |= 1 << 23;
        return Midpoints(new BigInteger(mantissa) * 2 + 1, exponent - 150 - 1);
    }

    static IEnumerable<string> Midpoints(BigInteger numerator, int binaryExponent)
    {
        string digits;
        int exp10;
        if (binaryExponent >= 0)
        {
            digits = (numerator << binaryExponent).ToString(CultureInfo.InvariantCulture);
            exp10 = 0;
        }
        else
        {
            digits = (numerator * BigInteger.Pow(5, -binaryExponent)).ToString(CultureInfo.InvariantCulture);
            exp10 = binaryExponent;
        }
        yield return digits + "e" + exp10;
        yield return digits + "1e" + (exp10 - 1);
        yield return (BigInteger.Parse(digits, CultureInfo.InvariantCulture) - 1).ToString(CultureInfo.InvariantCulture) + "9e" + (exp10 - 1);
    }

    static string ParseAll(byte[] json)
    {
        var sb = new StringBuilder();
        var r = new Utf8JsonReader(json);
        while (r.Read())
        {
            if (r.TokenType != JsonTokenType.Number)
                continue;
            sb.Append(Show(r.ValueSpan)).Append(' ').Append(Bits(r.GetDouble())).Append(' ').Append(Bits(r.GetSingle())).Append(' ')
                .Append(r.TryGetDecimal(out decimal m) ? Bits(m) : "F").Append(' ').Append(r.TryGetInt64(out long l) ? l : 0).Append('\n');
        }
        return sb.ToString();
    }

    static string ParseAllReference(byte[] json)
    {
        var sb = new StringBuilder();
        var r = new S.Utf8JsonReader(json);
        while (r.Read())
        {
            if (r.TokenType != S.JsonTokenType.Number)
                continue;
            sb.Append(Show(r.ValueSpan)).Append(' ').Append(Bits(r.GetDouble())).Append(' ').Append(Bits(r.GetSingle())).Append(' ')
                .Append(r.TryGetDecimal(out decimal m) ? Bits(m) : "F").Append(' ').Append(r.TryGetInt64(out long l) ? l : 0).Append('\n');
        }
        return sb.ToString();
    }

    static string Write(Action<Utf8JsonWriter> body)
    {
        var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartArray();
            body(w);
            w.WriteEndArray();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    static string WriteReference(Action<S.Utf8JsonWriter> body)
    {
        var ms = new MemoryStream();
        using (var w = new S.Utf8JsonWriter(ms))
        {
            w.WriteStartArray();
            body(w);
            w.WriteEndArray();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // reports the first differing element instead of two megabyte strings
    static void AssertSameElements(string expected, string actual)
    {
        if (expected == actual)
            return;
        string[] e = expected.Split(','), a = actual.Split(',');
        for (int i = 0; i < Math.Min(e.Length, a.Length); i++)
            Assert.True(e[i] == a[i], "element " + i + ": expected " + e[i] + " actual " + a[i]);
        Assert.Equal(e.Length, a.Length);
    }
}
