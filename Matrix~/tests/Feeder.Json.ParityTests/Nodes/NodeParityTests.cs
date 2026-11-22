using System.Globalization;
using System.Text;
using Feeder.Json.Nodes;
using static Feeder.Json.ParityTests.TestSupport;
using S = System.Text.Json;
using SN = System.Text.Json.Nodes;
using SS = System.Text.Json.Serialization;
using Feeder.Json.ParityTests.SerializerParity;

namespace Feeder.Json.ParityTests.DomParity;

// every scenario runs once per library and the resulting transcripts must be identical
public class NodeParityTests
{
    static readonly JsonSerializerOptions indented = new() { WriteIndented = true };
    static readonly S.JsonSerializerOptions referenceIndented = new() { WriteIndented = true };
    static readonly JsonSerializerOptions relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };
    static readonly S.JsonSerializerOptions referenceRelaxed = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };

    public static IEnumerable<object[]> CorpusNames() => Corpus.Names.Select(n => new object[] { n });

    static string Text(byte[] json)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(json);
        }
        catch (DecoderFallbackException)
        {
            return "";
        }
    }

    [Theory]
    [MemberData(nameof(CorpusNames))]
    public void ParsedNodeWritesAndWalksTheSame(string name)
    {
        string text = Text(Corpus.Get(name));
        if (text.Length == 0)
            return;
        string expected = Try(() => Describe(SN.JsonNode.Parse(text)));
        string actual = Try(() => Describe(JsonNode.Parse(text)));
        ParityAssert.Same(expected, actual, nameof(NodeParityTests) + "_1");
    }

    [Theory]
    [MemberData(nameof(CorpusNames))]
    public void NodeFromReaderMatches(string name)
    {
        byte[] json = Corpus.Get(name);
        string expected = Try(() =>
        {
            var reader = new S.Utf8JsonReader(json);
            return Describe(SN.JsonNode.Parse(ref reader)) + "@" + reader.BytesConsumed;
        });
        string actual = Try(() =>
        {
            var reader = new Utf8JsonReader(json);
            return Describe(JsonNode.Parse(ref reader)) + "@" + reader.BytesConsumed;
        });
        ParityAssert.Same(expected, actual, nameof(NodeParityTests) + "_2");
    }

    static string Describe(SN.JsonNode? node)
    {
        if (node == null)
            return "(null)";
        var sb = new StringBuilder();
        sb.Append(node.ToJsonString()).Append('\n');
        sb.Append(node.ToJsonString(referenceIndented)).Append('\n');
        sb.Append(node.ToJsonString(referenceRelaxed)).Append('\n');
        sb.Append(Show(node.ToString())).Append('\n');
        sb.Append(Show(node.DeepClone().ToJsonString())).Append('\n');
        Walk(node, sb, 0);
        return sb.ToString();
    }

    static string Describe(JsonNode? node)
    {
        if (node == null)
            return "(null)";
        var sb = new StringBuilder();
        sb.Append(node.ToJsonString()).Append('\n');
        sb.Append(node.ToJsonString(indented)).Append('\n');
        sb.Append(node.ToJsonString(relaxed)).Append('\n');
        sb.Append(Show(node.ToString())).Append('\n');
        sb.Append(Show(node.DeepClone().ToJsonString())).Append('\n');
        Walk(node, sb, 0);
        return sb.ToString();
    }

    static void Walk(SN.JsonNode? node, StringBuilder sb, int level)
    {
        sb.Append(' ', level);
        if (node == null)
        {
            sb.Append("null\n");
            return;
        }
        sb.Append(node.GetValueKind()).Append(' ');
        switch (node)
        {
            case SN.JsonObject o:
                sb.Append("count=").Append(Try(() => o.Count.ToString())).Append('\n');
                if (o.Count < 0)
                    return;
                foreach (var pair in o)
                {
                    sb.Append(' ', level + 1).Append(Show(pair.Key)).Append(" has=").Append(o.ContainsKey(pair.Key))
                        .Append(" try=").Append(o.TryGetPropertyValue(pair.Key, out var found) && ReferenceEquals(found, pair.Value)).Append('\n');
                    Walk(pair.Value, sb, level + 2);
                }
                break;
            case SN.JsonArray a:
                sb.Append("count=").Append(a.Count).Append('\n');
                for (int i = 0; i < a.Count; i++)
                    Walk(a[i], sb, level + 1);
                break;
            case SN.JsonValue v:
                sb.Append(Values(v)).Append('\n');
                break;
        }
    }

    static void Walk(JsonNode? node, StringBuilder sb, int level)
    {
        sb.Append(' ', level);
        if (node == null)
        {
            sb.Append("null\n");
            return;
        }
        sb.Append(node.GetValueKind()).Append(' ');
        switch (node)
        {
            case JsonObject o:
                sb.Append("count=").Append(Try(() => o.Count.ToString())).Append('\n');
                if (o.Count < 0)
                    return;
                foreach (var pair in o)
                {
                    sb.Append(' ', level + 1).Append(Show(pair.Key)).Append(" has=").Append(o.ContainsKey(pair.Key))
                        .Append(" try=").Append(o.TryGetPropertyValue(pair.Key, out var found) && ReferenceEquals(found, pair.Value)).Append('\n');
                    Walk(pair.Value, sb, level + 2);
                }
                break;
            case JsonArray a:
                sb.Append("count=").Append(a.Count).Append('\n');
                for (int i = 0; i < a.Count; i++)
                    Walk(a[i], sb, level + 1);
                break;
            case JsonValue v:
                sb.Append(Values(v)).Append('\n');
                break;
        }
    }

    static string Values(SN.JsonValue v) => string.Join(" ",
        "s=" + Try(() => Show(v.GetValue<string>())),
        "ts=" + (v.TryGetValue(out string? s) ? Show(s) : "-"),
        "i=" + (v.TryGetValue(out int i) ? i.ToString(CultureInfo.InvariantCulture) : "-"),
        "l=" + (v.TryGetValue(out long l) ? l.ToString(CultureInfo.InvariantCulture) : "-"),
        "d=" + (v.TryGetValue(out double d) ? Bits(d) : "-"),
        "f=" + (v.TryGetValue(out float f) ? Bits(f) : "-"),
        "m=" + (v.TryGetValue(out decimal m) ? Bits(m) : "-"),
        "b=" + (v.TryGetValue(out bool b) ? b.ToString() : "-"),
        "ni=" + (v.TryGetValue(out int? ni) ? ni.ToString() : "-"),
        "gi=" + Try(() => v.GetValue<int>().ToString(CultureInfo.InvariantCulture)),
        "gb=" + Try(() => v.GetValue<bool>().ToString()),
        "gd=" + Try(() => Bits(v.GetValue<double>())));

    static string Values(JsonValue v) => string.Join(" ",
        "s=" + Try(() => Show(v.GetValue<string>())),
        "ts=" + (v.TryGetValue(out string? s) ? Show(s) : "-"),
        "i=" + (v.TryGetValue(out int i) ? i.ToString(CultureInfo.InvariantCulture) : "-"),
        "l=" + (v.TryGetValue(out long l) ? l.ToString(CultureInfo.InvariantCulture) : "-"),
        "d=" + (v.TryGetValue(out double d) ? Bits(d) : "-"),
        "f=" + (v.TryGetValue(out float f) ? Bits(f) : "-"),
        "m=" + (v.TryGetValue(out decimal m) ? Bits(m) : "-"),
        "b=" + (v.TryGetValue(out bool b) ? b.ToString() : "-"),
        "ni=" + (v.TryGetValue(out int? ni) ? ni.ToString() : "-"),
        "gi=" + Try(() => v.GetValue<int>().ToString(CultureInfo.InvariantCulture)),
        "gb=" + Try(() => v.GetValue<bool>().ToString()),
        "gd=" + Try(() => Bits(v.GetValue<double>())));

    [Fact]
    public void BuiltTreeMatches()
    {
        string expected = Try(() =>
        {
            var defines = new SN.JsonObject();
            var root = new SN.JsonObject
            {
                ["type"] = "object",
                ["count"] = 0,
                ["max"] = 255,
                ["flag"] = true,
                ["off"] = false,
                ["text"] = "quote \" slash \\ tab \t <tag> & 'apos' + \u00e9 \ud83d\ude00 \u2028",
                ["none"] = null,
                ["value"] = SN.JsonValue.Create(1.5),
                ["empty"] = SN.JsonValue.Create((string?)null, (SN.JsonNodeOptions?)null),
                ["nested"] = new SN.JsonObject { ["a"] = new SN.JsonArray { "x", 1, true, null, new SN.JsonObject(), new SN.JsonArray() } },
            };
            for (int k = 0; k < 12; k++)
                defines["T" + k] = new SN.JsonObject { ["type"] = "integer", ["index"] = k };
            root["$defs"] = defines;
            var sb = new StringBuilder();
            sb.Append(root.ToJsonString()).Append('\n').Append(root.ToJsonString(referenceIndented)).Append('\n').Append(root.ToJsonString(referenceRelaxed)).Append('\n');
            sb.Append(root.ToString()).Append('\n');
            sb.Append(defines.ContainsKey("T11")).Append(defines.ContainsKey("t11")).Append(defines.Count).Append('\n');
            sb.Append(defines.Remove("T3")).Append(defines.Remove("T3")).Append(defines.Count).Append('\n');
            defines["T5"] = SN.JsonValue.Create("replaced");
            defines["T99"] = 1;
            sb.Append(defines.ToJsonString()).Append('\n');
            sb.Append(Try(() => { var other = new SN.JsonObject(); other["again"] = defines; return "ok"; })).Append('\n');
            sb.Append(Try(() => { var other = root["nested"]!.DeepClone(); root["copy"] = other; return root.ToJsonString(); })).Append('\n');
            sb.Append(Try(() => root["nested"]!["a"]![1]!.GetValue<int>().ToString())).Append('\n');
            sb.Append(Try(() => root["nested"]!["a"]![0]!.GetValue<string>())).Append('\n');
            sb.Append(Try(() => root["count"]!.GetValue<long>().ToString())).Append('\n');
            sb.Append(Try(() => root["flag"]!.AsArray().Count.ToString())).Append('\n');
            sb.Append(Try(() => root["text"]!.AsObject().Count.ToString())).Append('\n');
            sb.Append(Try(() => root["nested"]!["a"]!["k"]!.ToJsonString())).Append('\n');
            sb.Append(root["missing"] == null).Append(root.GetValueKind()).Append(root["flag"]!.GetValueKind()).Append(root["off"]!.GetValueKind())
                .Append(root["max"]!.GetValueKind()).Append(root["text"]!.GetValueKind()).Append(root["value"]!.GetValueKind()).Append('\n');
            var array = root["nested"]!["a"]!.AsArray();
            var first = array[0];
            sb.Append(array.Remove(first)).Append(array.Remove(first)).Append(array.Count).Append(array.IndexOf(array[0])).Append('\n');
            array[0] = "set";
            array.Add(SN.JsonValue.Create(2));
            array.Insert(0, first);
            array.RemoveAt(1);
            sb.Append(array.ToJsonString()).Append('\n');
            sb.Append(Try(() => { array.Add(root); return "ok"; })).Append('\n');
            sb.Append(Try(() => { var loop = new SN.JsonObject(); var inner = new SN.JsonObject(); loop["inner"] = inner; inner["outer"] = loop; return "ok"; })).Append('\n');
            sb.Append(Try(() => { var o = new SN.JsonObject(); o.Add("k", 1); o.Add("k", 2); return "ok"; })).Append('\n');
            root.Remove("copy");
            ((SN.JsonObject)root["nested"]!).Clear();
            sb.Append(root.ToJsonString()).Append('\n');
            var schema = new SN.JsonObject { ["properties"] = new SN.JsonObject { ["p"] = new SN.JsonObject { ["description"] = "d", ["type"] = "string" } } };
            foreach (var item in (SN.JsonObject)schema["properties"]!)
                ((SN.JsonObject)item.Value!).Remove("description");
            sb.Append(SN.JsonNode.Parse(schema.ToJsonString())!.ToJsonString(referenceRelaxed)).Append('\n');
            return sb.ToString();
        });
        string actual = Try(() =>
        {
            var defines = new JsonObject();
            var root = new JsonObject
            {
                ["type"] = "object",
                ["count"] = 0,
                ["max"] = 255,
                ["flag"] = true,
                ["off"] = false,
                ["text"] = "quote \" slash \\ tab \t <tag> & 'apos' + \u00e9 \ud83d\ude00 \u2028",
                ["none"] = null,
                ["value"] = JsonValue.Create(1.5),
                ["empty"] = JsonValue.Create((string?)null, (JsonNodeOptions?)null),
                ["nested"] = new JsonObject { ["a"] = new JsonArray { "x", 1, true, null, new JsonObject(), new JsonArray() } },
            };
            for (int k = 0; k < 12; k++)
                defines["T" + k] = new JsonObject { ["type"] = "integer", ["index"] = k };
            root["$defs"] = defines;
            var sb = new StringBuilder();
            sb.Append(root.ToJsonString()).Append('\n').Append(root.ToJsonString(indented)).Append('\n').Append(root.ToJsonString(relaxed)).Append('\n');
            sb.Append(root.ToString()).Append('\n');
            sb.Append(defines.ContainsKey("T11")).Append(defines.ContainsKey("t11")).Append(defines.Count).Append('\n');
            sb.Append(defines.Remove("T3")).Append(defines.Remove("T3")).Append(defines.Count).Append('\n');
            defines["T5"] = JsonValue.Create("replaced");
            defines["T99"] = 1;
            sb.Append(defines.ToJsonString()).Append('\n');
            sb.Append(Try(() => { var other = new JsonObject(); other["again"] = defines; return "ok"; })).Append('\n');
            sb.Append(Try(() => { var other = root["nested"]!.DeepClone(); root["copy"] = other; return root.ToJsonString(); })).Append('\n');
            sb.Append(Try(() => root["nested"]!["a"]![1]!.GetValue<int>().ToString())).Append('\n');
            sb.Append(Try(() => root["nested"]!["a"]![0]!.GetValue<string>())).Append('\n');
            sb.Append(Try(() => root["count"]!.GetValue<long>().ToString())).Append('\n');
            sb.Append(Try(() => root["flag"]!.AsArray().Count.ToString())).Append('\n');
            sb.Append(Try(() => root["text"]!.AsObject().Count.ToString())).Append('\n');
            sb.Append(Try(() => root["nested"]!["a"]!["k"]!.ToJsonString())).Append('\n');
            sb.Append(root["missing"] == null).Append(root.GetValueKind()).Append(root["flag"]!.GetValueKind()).Append(root["off"]!.GetValueKind())
                .Append(root["max"]!.GetValueKind()).Append(root["text"]!.GetValueKind()).Append(root["value"]!.GetValueKind()).Append('\n');
            var array = root["nested"]!["a"]!.AsArray();
            var first = array[0];
            sb.Append(array.Remove(first)).Append(array.Remove(first)).Append(array.Count).Append(array.IndexOf(array[0])).Append('\n');
            array[0] = "set";
            array.Add(JsonValue.Create(2));
            array.Insert(0, first);
            array.RemoveAt(1);
            sb.Append(array.ToJsonString()).Append('\n');
            sb.Append(Try(() => { array.Add(root); return "ok"; })).Append('\n');
            sb.Append(Try(() => { var loop = new JsonObject(); var inner = new JsonObject(); loop["inner"] = inner; inner["outer"] = loop; return "ok"; })).Append('\n');
            sb.Append(Try(() => { var o = new JsonObject(); o.Add("k", 1); o.Add("k", 2); return "ok"; })).Append('\n');
            root.Remove("copy");
            ((JsonObject)root["nested"]!).Clear();
            sb.Append(root.ToJsonString()).Append('\n');
            var schema = new JsonObject { ["properties"] = new JsonObject { ["p"] = new JsonObject { ["description"] = "d", ["type"] = "string" } } };
            foreach (var item in (JsonObject)schema["properties"]!)
                ((JsonObject)item.Value!).Remove("description");
            sb.Append(JsonNode.Parse(schema.ToJsonString())!.ToJsonString(relaxed)).Append('\n');
            return sb.ToString();
        });
        ParityAssert.Same(expected, actual, nameof(NodeParityTests) + "_3");
    }

    [Fact]
    public void SharedStaticNodeFailsOnSecondParentLikeReference()
    {
        string expected = Try(() =>
        {
            var any = new SN.JsonObject { ["type"] = "string" };
            var first = new SN.JsonObject { ["items"] = any };
            var sb = new StringBuilder(first.ToJsonString());
            sb.Append(Try(() => new SN.JsonObject { ["items"] = any }.ToJsonString()));
            first.Remove("items");
            sb.Append(new SN.JsonObject { ["items"] = any }.ToJsonString());
            return sb.ToString();
        });
        string actual = Try(() =>
        {
            var any = new JsonObject { ["type"] = "string" };
            var first = new JsonObject { ["items"] = any };
            var sb = new StringBuilder(first.ToJsonString());
            sb.Append(Try(() => new JsonObject { ["items"] = any }.ToJsonString()));
            first.Remove("items");
            sb.Append(new JsonObject { ["items"] = any }.ToJsonString());
            return sb.ToString();
        });
        ParityAssert.Same(expected, actual, nameof(NodeParityTests) + "_4");
    }

    [Theory]
    [InlineData("{\"a\":1,\"a\":2}")]
    [InlineData("{\"Name\":1,\"name\":2}")]
    [InlineData("{\"x\":{\"y\":[1,{\"z\":null}]},\"w\":\"\\u00e9\"}")]
    public void LazyObjectsKeepTheirTextUntilTouched(string json)
    {
        foreach (bool insensitive in new[] { false, true })
        {
            string expected = Try(() =>
            {
                var node = SN.JsonNode.Parse(json, new SN.JsonNodeOptions { PropertyNameCaseInsensitive = insensitive })!;
                var sb = new StringBuilder(node.ToJsonString());
                sb.Append('|').Append(Try(() => node["NAME"]?.ToJsonString() ?? "(null)"));
                sb.Append('|').Append(Try(() => node.AsObject().Count.ToString()));
                sb.Append('|').Append(Try(() => node.ToJsonString()));
                return sb.ToString();
            });
            string actual = Try(() =>
            {
                var node = JsonNode.Parse(json, new JsonNodeOptions { PropertyNameCaseInsensitive = insensitive })!;
                var sb = new StringBuilder(node.ToJsonString());
                sb.Append('|').Append(Try(() => node["NAME"]?.ToJsonString() ?? "(null)"));
                sb.Append('|').Append(Try(() => node.AsObject().Count.ToString()));
                sb.Append('|').Append(Try(() => node.ToJsonString()));
                return sb.ToString();
            });
            ParityAssert.Same(expected, actual, nameof(NodeParityTests) + "_5");
        }
    }

    [Theory]
    [InlineData("{\"a\":1}")]
    [InlineData("[1,2]")]
    [InlineData("\"s\"")]
    [InlineData("12.50")]
    [InlineData("true")]
    [InlineData("null")]
    public void CreateFromElementMatches(string json)
    {
        string expected = Try(() =>
        {
            using var document = S.JsonDocument.Parse(json);
            S.JsonElement e = document.RootElement;
            return string.Join("|",
                Try(() => SN.JsonObject.Create(e)?.ToJsonString() ?? "(null)"),
                Try(() => SN.JsonArray.Create(e)?.ToJsonString() ?? "(null)"),
                Try(() => SN.JsonValue.Create(e)?.ToJsonString() ?? "(null)"),
                Try(() => SN.JsonValue.Create(e)?.ToString() ?? "(null)"));
        });
        string actual = Try(() =>
        {
            using var document = JsonDocument.Parse(json);
            JsonElement e = document.RootElement;
            return string.Join("|",
                Try(() => JsonObject.Create(e)?.ToJsonString() ?? "(null)"),
                Try(() => JsonArray.Create(e)?.ToJsonString() ?? "(null)"),
                Try(() => JsonValue.Create(e)?.ToJsonString() ?? "(null)"),
                Try(() => JsonValue.Create(e)?.ToString() ?? "(null)"));
        });
        ParityAssert.Same(expected, actual, nameof(NodeParityTests) + "_6");
    }

    [Fact]
    public void CreatedValuesMatch()
    {
        string expected = string.Join("|",
            Try(() => Values(SN.JsonValue.Create(5))), Try(() => Values(SN.JsonValue.Create(true))), Try(() => Values(SN.JsonValue.Create(2.5))),
            Try(() => Values(SN.JsonValue.Create("5")!)), Try(() => SN.JsonValue.Create(double.NaN).ToJsonString()),
            Try(() => SN.JsonValue.Create(double.NaN).ToJsonString(new S.JsonSerializerOptions { NumberHandling = SS.JsonNumberHandling.AllowNamedFloatingPointLiterals })),
            Try(() => SN.JsonValue.Create(0.1).ToJsonString()), Try(() => SN.JsonValue.Create(1e300).ToJsonString()), Try(() => SN.JsonValue.Create(-0.0).ToJsonString()),
            Try(() => new SN.JsonArray { "a", 1, 2.25, false, (string?)null, new SN.JsonObject { ["k"] = "v" } }.ToJsonString()),
            Try(() => SN.JsonValue.Create("x")!.ToString()), Try(() => SN.JsonValue.Create(3)!.ToString()));
        string actual = string.Join("|",
            Try(() => Values(JsonValue.Create(5))), Try(() => Values(JsonValue.Create(true))), Try(() => Values(JsonValue.Create(2.5))),
            Try(() => Values(JsonValue.Create("5")!)), Try(() => JsonValue.Create(double.NaN).ToJsonString()),
            Try(() => JsonValue.Create(double.NaN).ToJsonString(new JsonSerializerOptions { NumberHandling = Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals })),
            Try(() => JsonValue.Create(0.1).ToJsonString()), Try(() => JsonValue.Create(1e300).ToJsonString()), Try(() => JsonValue.Create(-0.0).ToJsonString()),
            Try(() => new JsonArray { "a", 1, 2.25, false, (string?)null, new JsonObject { ["k"] = "v" } }.ToJsonString()),
            Try(() => JsonValue.Create("x")!.ToString()), Try(() => JsonValue.Create(3)!.ToString()));
        ParityAssert.Same(expected, actual, nameof(NodeParityTests) + "_7");
    }

    [Fact]
    public void WriteToHonoursWriterAndOptions()
    {
        const string json = "{\"a\":[1,2.5,\"<x>\"],\"b\":{\"c\":null}}";
        string expected = Try(() =>
        {
            var node = SN.JsonNode.Parse(json)!;
            node["added"] = SN.JsonValue.Create(double.PositiveInfinity);
            var stream = new MemoryStream();
            using (var writer = new S.Utf8JsonWriter(stream, new S.JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartArray();
                node.WriteTo(writer, new S.JsonSerializerOptions { NumberHandling = SS.JsonNumberHandling.AllowNamedFloatingPointLiterals });
                writer.WriteEndArray();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        });
        string actual = Try(() =>
        {
            var node = JsonNode.Parse(json)!;
            node["added"] = JsonValue.Create(double.PositiveInfinity);
            var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartArray();
                node.WriteTo(writer, new JsonSerializerOptions { NumberHandling = Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
                writer.WriteEndArray();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        });
        ParityAssert.Same(expected, actual, nameof(NodeParityTests) + "_8");
    }
}
