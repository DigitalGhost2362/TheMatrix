using System.Text;
using static Feeder.Json.ParityTests.TestSupport;
using S = System.Text.Json;

namespace Feeder.Json.ParityTests;

public class DocumentParityTests
{
    static readonly Dictionary<string, JsonDocumentOptions> optionSets = new()
    {
        ["default"] = new JsonDocumentOptions(),
        ["lenient"] = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip },
        ["deep"] = new JsonDocumentOptions { MaxDepth = 1000 },
    };

    public static IEnumerable<object[]> Cases() =>
        from name in Corpus.Names from set in optionSets.Keys select new object[] { name, set };

    [Theory]
    [MemberData(nameof(Cases))]
    public void DocumentFromBytesMatches(string name, string optionSet)
    {
        byte[] json = Corpus.Get(name);
        JsonDocumentOptions options = optionSets[optionSet];
        string expected = Try(() =>
        {
            using var d = S.JsonDocument.Parse(json, ToReference(options));
            return ElementWalk.Describe(new ReferenceElement(d.RootElement));
        });
        string actual = Try(() =>
        {
            using var d = JsonDocument.Parse(json, options);
            return ElementWalk.Describe(new FeederElement(d.RootElement));
        });
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void DocumentFromStringMatches(string name, string optionSet)
    {
        byte[] json = Corpus.Get(name);
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(json);
        }
        catch (DecoderFallbackException)
        {
            return;
        }
        JsonDocumentOptions options = optionSets[optionSet];
        string expected = Try(() =>
        {
            using var d = S.JsonDocument.Parse(text, ToReference(options));
            return ElementWalk.Describe(new ReferenceElement(d.RootElement));
        });
        string actual = Try(() =>
        {
            using var d = JsonDocument.Parse(text, options);
            return ElementWalk.Describe(new FeederElement(d.RootElement));
        });
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ParseValueMatches(string name, string optionSet)
    {
        byte[] json = Corpus.Get(name);
        JsonReaderOptions options = new JsonReaderOptions
        {
            AllowTrailingCommas = optionSets[optionSet].AllowTrailingCommas,
            CommentHandling = optionSets[optionSet].CommentHandling,
            MaxDepth = optionSets[optionSet].MaxDepth,
        };
        Assert.Equal(ParseValuesReference(json, ToReference(options)), ParseValues(json, options));
    }

    // a value parsed out of the middle of a stream, at every start token, property name and primitive
    static string ParseValues(byte[] json, JsonReaderOptions options)
    {
        var sb = new StringBuilder();
        try
        {
            var r = new Utf8JsonReader(json, options);
            int step = 0;
            while (r.Read())
            {
                step++;
                if (step % 3 == 0 && r.TokenType != JsonTokenType.EndArray && r.TokenType != JsonTokenType.EndObject)
                {
                    using JsonDocument d = JsonDocument.ParseValue(ref r);
                    sb.Append("doc ").Append(Show(d.RootElement.GetRawText())).Append(" at ").Append(r.TokenType).Append(' ').Append(r.BytesConsumed).Append('\n');
                }
                else if (step % 3 == 1)
                {
                    bool ok = JsonElement.TryParseValue(ref r, out JsonElement? e);
                    sb.Append("try ").Append(ok).Append(' ').Append(e.HasValue ? Show(e.Value.GetRawText()) + " " + e.Value.ValueKind : "-")
                        .Append(" at ").Append(r.TokenType).Append(' ').Append(r.BytesConsumed).Append('\n');
                }
            }
        }
        catch (Exception e)
        {
            sb.Append('!').Append(Name(e));
        }
        return sb.ToString();
    }

    static string ParseValuesReference(byte[] json, S.JsonReaderOptions options)
    {
        var sb = new StringBuilder();
        try
        {
            var r = new S.Utf8JsonReader(json, options);
            int step = 0;
            while (r.Read())
            {
                step++;
                if (step % 3 == 0 && r.TokenType != S.JsonTokenType.EndArray && r.TokenType != S.JsonTokenType.EndObject)
                {
                    using S.JsonDocument d = S.JsonDocument.ParseValue(ref r);
                    sb.Append("doc ").Append(Show(d.RootElement.GetRawText())).Append(" at ").Append(r.TokenType).Append(' ').Append(r.BytesConsumed).Append('\n');
                }
                else if (step % 3 == 1)
                {
                    bool ok = S.JsonElement.TryParseValue(ref r, out S.JsonElement? e);
                    sb.Append("try ").Append(ok).Append(' ').Append(e.HasValue ? Show(e.Value.GetRawText()) + " " + e.Value.ValueKind : "-")
                        .Append(" at ").Append(r.TokenType).Append(' ').Append(r.BytesConsumed).Append('\n');
                }
            }
        }
        catch (Exception e)
        {
            sb.Append('!').Append(Name(e));
        }
        return sb.ToString();
    }

    [Fact]
    public void DisposedDocumentThrowsAndCloneSurvives()
    {
        var d = JsonDocument.Parse("{\"a\":[1,2],\"b\":\"x\"}");
        JsonElement root = d.RootElement;
        JsonElement clone = root.GetProperty("a").Clone();
        JsonElement.ArrayEnumerator items = root.GetProperty("a").EnumerateArray();
        d.Dispose();
        Assert.Throws<ObjectDisposedException>(() => root.ValueKind);
        Assert.Throws<ObjectDisposedException>(() => root.GetRawText());
        Assert.Throws<ObjectDisposedException>(() => root.GetProperty("a"));
        Assert.Equal("[1,2]", clone.GetRawText());
        Assert.Equal(2, clone.GetArrayLength());
        Assert.Equal(clone.GetRawText(), clone.Clone().GetRawText());

        var reference = S.JsonDocument.Parse("{\"a\":[1,2]}");
        S.JsonElement referenceRoot = reference.RootElement;
        reference.Dispose();
        Assert.Throws<ObjectDisposedException>(() => referenceRoot.ValueKind);
        d.Dispose();
    }

    [Fact]
    public void DefaultElementMatches()
    {
        JsonElement e = default;
        S.JsonElement r = default;
        Assert.Equal(r.ValueKind.ToString(), e.ValueKind.ToString());
        Assert.Equal(r.ToString(), e.ToString());
        Assert.Equal(Try(() => r.GetRawText()), Try(() => e.GetRawText()));
        Assert.Equal(Try(() => r.GetString()!), Try(() => e.GetString()!));
        Assert.Equal(Try(() => r.GetBoolean().ToString()), Try(() => e.GetBoolean().ToString()));
        Assert.Equal(Try(() => r.Clone().ToString()!), Try(() => e.Clone().ToString()!));
        Assert.Equal(Try(() => r.EnumerateArray().ToString()!), Try(() => e.EnumerateArray().ToString()!));
        Assert.Equal(Try(() => r.GetProperty("x").ToString()!), Try(() => e.GetProperty("x").ToString()!));
        Assert.Equal(default(S.JsonProperty).Value.ValueKind.ToString(), default(JsonProperty).Value.ValueKind.ToString());
        Assert.Equal(Try(() => default(S.JsonProperty).Name), Try(() => default(JsonProperty).Name));
    }

    [Fact]
    public void ArgumentChecksMatch()
    {
        using var d = JsonDocument.Parse("{}");
        using var r = S.JsonDocument.Parse("{}");
        Assert.Equal(Try(() => r.RootElement.TryGetProperty((string)null!, out _).ToString()), Try(() => d.RootElement.TryGetProperty((string)null!, out _).ToString()));
        Assert.Equal(Try(() => S.JsonDocument.Parse((string)null!).ToString()!), Try(() => JsonDocument.Parse((string)null!).ToString()!));
        Assert.Equal(Try(() => { r.RootElement.WriteTo(null!); return ""; }), Try(() => { d.RootElement.WriteTo(null!); return ""; }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Allow });
        Assert.Throws<ArgumentOutOfRangeException>(() => new S.JsonDocumentOptions { CommentHandling = S.JsonCommentHandling.Allow });
        Assert.Throws<ArgumentOutOfRangeException>(() => new JsonDocumentOptions { MaxDepth = -1 });
    }

    [Fact]
    public void LoneSurrogateInSourceStringMatches()
    {
        string text = "[\"a" + (char)0xD800 + "\"]";
        Assert.Equal(Try(() => S.JsonDocument.Parse(text).RootElement.GetRawText()), Try(() => JsonDocument.Parse(text).RootElement.GetRawText()));
        using var d = JsonDocument.Parse("{\"a\":1}");
        using var r = S.JsonDocument.Parse("{\"a\":1}");
        string name = "a" + (char)0xDC00;
        Assert.Equal(Try(() => r.RootElement.TryGetProperty(name, out _).ToString()), Try(() => d.RootElement.TryGetProperty(name, out _).ToString()));
    }

    [Fact]
    public void EnumeratorsResetAndCompose()
    {
        using var d = JsonDocument.Parse("{\"a\":[1,[2,3],{\"x\":4}],\"b\":{\"c\":5,\"d\":[6]},\"e\":7}");
        JsonElement.ArrayEnumerator items = d.RootElement.GetProperty("a").EnumerateArray();
        Assert.Equal(3, items.Count());
        Assert.Equal(3, items.Count());
        Assert.True(items.MoveNext());
        Assert.Equal("1", items.Current.GetRawText());
        Assert.Equal(3, items.GetEnumerator().Count());
        items.Reset();
        Assert.Equal(JsonValueKind.Undefined, items.Current.ValueKind);
        items.Dispose();
        Assert.False(items.MoveNext());

        JsonElement.ObjectEnumerator props = d.RootElement.EnumerateObject();
        Assert.Equal("a,b,e", string.Join(",", props.Select(p => p.Name)));
        Assert.Equal(new[] { "c", "d" }, d.RootElement.GetProperty("b").EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(JsonValueKind.Undefined, props.Current.Value.ValueKind);
    }

    [Fact]
    public void ElementsOfDifferentDocumentsStayIndependent()
    {
        using var a = JsonDocument.Parse("[1]");
        using var b = JsonDocument.Parse("[2]");
        Assert.Equal("1", a.RootElement.EnumerateArray().First().GetRawText());
        Assert.Equal("2", b.RootElement.EnumerateArray().First().GetRawText());
    }

    [Fact]
    public void ParseFromMemorySlice()
    {
        byte[] bytes = J("xx{\"a\":[true]}yy");
        using var d = JsonDocument.Parse(new ReadOnlyMemory<byte>(bytes, 2, bytes.Length - 4));
        Assert.Equal("{\"a\":[true]}", d.RootElement.GetRawText());
        Assert.True(d.RootElement.GetProperty("a").EnumerateArray().First().GetBoolean());
    }
}
