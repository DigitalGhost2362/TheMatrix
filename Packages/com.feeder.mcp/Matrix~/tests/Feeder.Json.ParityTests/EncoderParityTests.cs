using System.Text;
using static Feeder.Json.ParityTests.TestSupport;
using S = System.Text.Json;

namespace Feeder.Json.ParityTests;

public class EncoderParityTests
{
    public static IEnumerable<object[]> Encoders() => new[] { new object[] { "null" }, new object[] { "default" }, new object[] { "relaxed" } };

    static JsonWriterOptions Options(string encoder) => new JsonWriterOptions
    {
        Encoder = encoder == "relaxed" ? JavaScriptEncoder.UnsafeRelaxedJsonEscaping : encoder == "default" ? JavaScriptEncoder.Default : null,
    };

    // every UTF-16 code unit on its own, separated by an always-safe letter so lone surrogates stay lone
    static string AllCodeUnits()
    {
        var sb = new StringBuilder(0x20000);
        for (int c = 0; c <= 0xFFFF; c++)
            sb.Append((char)c).Append('x');
        return sb.ToString();
    }

    static string Supplementary()
    {
        var sb = new StringBuilder();
        for (int cp = 0x10000; cp <= 0x10FFFF; cp += 97)
            sb.Append(char.ConvertFromUtf32(cp)).Append('x');
        return sb.ToString();
    }

    [Theory]
    [MemberData(nameof(Encoders))]
    public void EveryCodeUnitEscapesAlike(string encoder)
    {
        string text = AllCodeUnits() + Supplementary();
        JsonWriterOptions options = Options(encoder);
        Assert.True(Same(WriteReference(text, ToReference(options)), Write(text, options), out string where), where);
    }

    [Theory]
    [MemberData(nameof(Encoders))]
    public void EveryScalarThroughDocumentWriteToEscapesAlike(string encoder)
    {
        var sb = new StringBuilder("[\"");
        for (int c = 0x20; c <= 0xFFFF; c++)
        {
            if (c == '"' || c == '\\' || (c >= 0xD800 && c <= 0xDFFF))
                continue;
            sb.Append((char)c);
        }
        sb.Append(Supplementary()).Append("\",\"");
        for (int c = 0; c <= 0xFFFF; c += 7)
        {
            // a lone low surrogate escape is a read error for both libraries, a high one gets its partner
            if (c >= 0xDC00 && c <= 0xDFFF)
                continue;
            sb.Append("\\u").Append(c.ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
            if (c >= 0xD800 && c <= 0xDBFF)
                sb.Append("\\uDC00");
        }
        sb.Append("\"]");
        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        JsonWriterOptions options = Options(encoder);
        string expected = Try(() => WriteReferenceElement(bytes, ToReference(options)));
        string actual = Try(() => WriteElement(bytes, options));
        Assert.DoesNotContain("!", expected[..1]);
        Assert.True(Same(expected, actual, out string where), where);
    }

    [Theory]
    [MemberData(nameof(Encoders))]
    public void InvalidUtf8InDocumentStringsIsReplacedAlike(string encoder)
    {
        string[] cases =
        {
            "<C3>(<F0><9F>", "<FF>", "<80>", "<C0><AF>", "<E0><80><80>", "<ED><A0><80>", "<F4><90><80><80>", "<F5><80>", "<E2><82>", "<E2><82>x",
            "<F0><9F><98>", "<F0><9F><98><80>", "a<C2>", "<C2><C2><A0>", "<EF><BF><BD>", "<FC><83><BF><BF><BF><BF>", "<E6><97><A5><D1><88><FA>",
        };
        JsonWriterOptions options = Options(encoder);
        foreach (string text in cases)
        {
            byte[] json = J("{\"" + text + "\":\"" + text + "\"}");
            Assert.Equal(Try(() => WriteReferenceElement(json, ToReference(options))), Try(() => WriteElement(json, options)));
        }
    }

    [Fact]
    public void RelaxedTableMatchesReferenceEncoder()
    {
        var relaxed = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        var defaults = System.Text.Encodings.Web.JavaScriptEncoder.Default;
        for (int c = 0; c < 0x10000; c++)
        {
            if (c >= 0xD800 && c <= 0xDFFF)
                continue;
            string s = ((char)c).ToString();
            Assert.True(JavaScriptEncoder.UnsafeRelaxedJsonEscaping.Allows(c) == (relaxed.Encode(s) == s), "relaxed U+" + c.ToString("X4"));
            Assert.True(JavaScriptEncoder.Default.Allows(c) == (defaults.Encode(s) == s), "default U+" + c.ToString("X4"));
        }
    }

    static string Write(string text, JsonWriterOptions options)
    {
        var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, options))
        {
            w.WriteStartObject();
            w.WriteString(text, text);
            w.WritePropertyName(text);
            w.WriteStartArray();
            w.WriteStringValue(text);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Show(ms.ToArray());
    }

    static string WriteReference(string text, S.JsonWriterOptions options)
    {
        var ms = new MemoryStream();
        using (var w = new S.Utf8JsonWriter(ms, options))
        {
            w.WriteStartObject();
            w.WriteString(text, text);
            w.WritePropertyName(text);
            w.WriteStartArray();
            w.WriteStringValue(text);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Show(ms.ToArray());
    }

    static string WriteElement(byte[] json, JsonWriterOptions options)
    {
        using var d = JsonDocument.Parse(json);
        var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, options))
            d.RootElement.WriteTo(w);
        return Show(ms.ToArray());
    }

    static string WriteReferenceElement(byte[] json, S.JsonWriterOptions options)
    {
        using var d = S.JsonDocument.Parse(json);
        var ms = new MemoryStream();
        using (var w = new S.Utf8JsonWriter(ms, options))
            d.RootElement.WriteTo(w);
        return Show(ms.ToArray());
    }

    static bool Same(string expected, string actual, out string where)
    {
        where = "";
        if (expected == actual)
            return true;
        int i = 0;
        while (i < Math.Min(expected.Length, actual.Length) && expected[i] == actual[i])
            i++;
        int from = Math.Max(0, i - 40);
        where = "first difference at " + i + ": expected ..." + expected.Substring(from, Math.Min(80, expected.Length - from))
            + " actual ..." + actual.Substring(from, Math.Min(80, actual.Length - from));
        return false;
    }
}
