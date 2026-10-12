using System.Globalization;
using System.Text;
using static Feeder.Json.ParityTests.TestSupport;
using S = System.Text.Json;

namespace Feeder.Json.ParityTests;

public class ReaderParityTests
{
    static readonly Dictionary<string, JsonReaderOptions> optionSets = new()
    {
        ["default"] = new JsonReaderOptions(),
        ["commas"] = new JsonReaderOptions { AllowTrailingCommas = true },
        ["comments"] = new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip },
        ["both"] = new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip },
        ["deep"] = new JsonReaderOptions { MaxDepth = 1000, CommentHandling = JsonCommentHandling.Skip },
        ["shallow"] = new JsonReaderOptions { MaxDepth = 3 },
    };

    public static IEnumerable<object[]> Cases() =>
        from name in Corpus.Names from set in optionSets.Keys select new object[] { name, set };

    [Theory]
    [MemberData(nameof(Cases))]
    public void TokenStreamMatches(string name, string optionSet)
    {
        byte[] json = Corpus.Get(name);
        JsonReaderOptions options = optionSets[optionSet];
        Assert.Equal(TraceReference(json, ToReference(options), false), Trace(json, options, false));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SkipMatches(string name, string optionSet)
    {
        byte[] json = Corpus.Get(name);
        JsonReaderOptions options = optionSets[optionSet];
        Assert.Equal(TraceReference(json, ToReference(options), true), Trace(json, options, true));
    }

    [Fact]
    public void FreshReaderState()
    {
        var reference = new S.Utf8JsonReader(J("[1]"));
        var reader = new Utf8JsonReader(J("[1]"));
        Assert.Equal(reference.TokenType.ToString(), reader.TokenType.ToString());
        Assert.Equal(reference.TokenStartIndex, reader.TokenStartIndex);
        Assert.Equal(reference.BytesConsumed, reader.BytesConsumed);
        Assert.Equal(reference.CurrentDepth, reader.CurrentDepth);
        Assert.Equal(reference.ValueSpan.Length, reader.ValueSpan.Length);
        Assert.False(reader.HasValueSequence);
        Assert.True(reader.ValueSequence.IsEmpty);
    }

    [Fact]
    public void AllowCommentsIsRejected()
    {
        Assert.Throws<NotSupportedException>(() => new Utf8JsonReader(J("[]"), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Allow }));
    }

    [Fact]
    public void OptionValidationMatches()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JsonReaderOptions { MaxDepth = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new JsonReaderOptions { CommentHandling = (JsonCommentHandling)3 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new S.JsonReaderOptions { MaxDepth = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new S.JsonReaderOptions { CommentHandling = (S.JsonCommentHandling)3 });
    }

    [Fact]
    public void ExceptionPositionsMatchForCommonErrors()
    {
        string[] inputs =
        {
            "{\n  \"a\": x}", "[1,\r\n2,\r\n  3x]", "[1,2", "{\"a\" 1}", "[\"abc", "[tru]", "[01]", "[1.]", "{,}", "[1,]",
            "\"a\\qb\"", "[\"a\nb\"]", "{\"a\":1}}", "[[[[", "\n\n   @", "[1,", "{\"a\":1,", "[1, ", "[1,\n", "{\"a\":1,\r\n  ",
        };
        foreach (string text in inputs)
        {
            byte[] json = J(text);
            string expected = PositionReference(json, default), actual = Position(json, default);
            Assert.True(expected == actual, text + " expected " + expected + " actual " + actual);
        }
    }

    // the line and byte position a reader error reports, for every corpus input that fails
    [Theory]
    [MemberData(nameof(Cases))]
    public void ExceptionPositionsMatch(string name, string optionSet)
    {
        byte[] json = Corpus.Get(name);
        JsonReaderOptions options = optionSets[optionSet];
        Assert.Equal(PositionReference(json, ToReference(options)), Position(json, options));
    }

    static string PositionReference(byte[] json, S.JsonReaderOptions options)
    {
        try
        {
            var r = new S.Utf8JsonReader(json, options);
            while (r.Read())
            {
            }
            return "none";
        }
        catch (S.JsonException e)
        {
            return e.LineNumber + ":" + e.BytePositionInLine;
        }
    }

    static string Position(byte[] json, JsonReaderOptions options)
    {
        try
        {
            var r = new Utf8JsonReader(json, options);
            while (r.Read())
            {
            }
            return "none";
        }
        catch (JsonException e)
        {
            return e.LineNumber + ":" + e.BytePositionInLine;
        }
    }

    static string Trace(byte[] json, JsonReaderOptions options, bool skip)
    {
        var sb = new StringBuilder();
        try
        {
            var r = new Utf8JsonReader(json, options);
            while (r.Read())
            {
                sb.Append(r.TokenType).Append(" s=").Append(r.TokenStartIndex).Append(" c=").Append(r.BytesConsumed)
                    .Append(" d=").Append(r.CurrentDepth).Append(" e=").Append(r.ValueIsEscaped).Append(" v=").Append(Show(r.ValueSpan));
                Details(ref r, sb);
                sb.Append('\n');
                if (skip && (r.TokenType == JsonTokenType.PropertyName || r.TokenType == JsonTokenType.StartArray) && r.CurrentDepth <= 2)
                {
                    r.Skip();
                    sb.Append("skip ").Append(r.TokenType).Append(" c=").Append(r.BytesConsumed).Append(" d=").Append(r.CurrentDepth).Append('\n');
                }
            }
            sb.Append("END ").Append(r.TokenType).Append(" c=").Append(r.BytesConsumed).Append(" s=").Append(r.TokenStartIndex)
                .Append(" d=").Append(r.CurrentDepth).Append(" v=").Append(r.ValueSpan.Length);
        }
        catch (Exception e)
        {
            sb.Append('!').Append(Name(e));
        }
        return sb.ToString();
    }

    static void Details(ref Utf8JsonReader r, StringBuilder sb)
    {
        switch (r.TokenType)
        {
            case JsonTokenType.String:
            case JsonTokenType.PropertyName:
                try
                {
                    sb.Append(" str=").Append(Show(r.GetString()));
                }
                catch (Exception e)
                {
                    sb.Append(" str=!").Append(Name(e));
                }
                break;
            case JsonTokenType.Number:
                sb.Append(" i32=").Append(r.TryGetInt32(out int i32) ? i32.ToString(CultureInfo.InvariantCulture) : "F");
                sb.Append(" i64=").Append(r.TryGetInt64(out long i64) ? i64.ToString(CultureInfo.InvariantCulture) : "F");
                sb.Append(" u32=").Append(r.TryGetUInt32(out uint u32) ? u32.ToString(CultureInfo.InvariantCulture) : "F");
                sb.Append(" u64=").Append(r.TryGetUInt64(out ulong u64) ? u64.ToString(CultureInfo.InvariantCulture) : "F");
                sb.Append(" u8=").Append(r.TryGetByte(out byte u8) ? u8.ToString(CultureInfo.InvariantCulture) : "F");
                sb.Append(" m=").Append(r.TryGetDecimal(out decimal m) ? Bits(m) : "F");
                sb.Append(" d=").Append(r.TryGetDouble(out double d) ? Bits(d) : "F");
                sb.Append(" f=").Append(r.TryGetSingle(out float f) ? Bits(f) : "F");
                try
                {
                    sb.Append(" g=").Append(r.GetInt32()).Append(r.GetByte()).Append(r.GetUInt32()).Append(r.GetDecimal());
                }
                catch (Exception e)
                {
                    sb.Append(" g=!").Append(Name(e));
                }
                sb.Append(" gd=").Append(Bits(r.GetDouble())).Append(Bits(r.GetSingle()));
                break;
            case JsonTokenType.True:
            case JsonTokenType.False:
                sb.Append(" b=").Append(r.GetBoolean());
                break;
        }
        if (r.TokenType != JsonTokenType.Number)
        {
            try
            {
                r.GetInt32();
            }
            catch (Exception e)
            {
                sb.Append(" n=!").Append(Name(e));
            }
        }
        if (r.TokenType != JsonTokenType.True && r.TokenType != JsonTokenType.False)
        {
            try
            {
                r.GetBoolean();
            }
            catch (Exception e)
            {
                sb.Append(" bo=!").Append(Name(e));
            }
        }
        if (r.TokenType != JsonTokenType.String && r.TokenType != JsonTokenType.PropertyName)
        {
            try
            {
                sb.Append(" so=").Append(Show(r.GetString()));
            }
            catch (Exception e)
            {
                sb.Append(" so=!").Append(Name(e));
            }
        }
    }

    static string TraceReference(byte[] json, S.JsonReaderOptions options, bool skip)
    {
        var sb = new StringBuilder();
        try
        {
            var r = new S.Utf8JsonReader(json, options);
            while (r.Read())
            {
                sb.Append(r.TokenType).Append(" s=").Append(r.TokenStartIndex).Append(" c=").Append(r.BytesConsumed)
                    .Append(" d=").Append(r.CurrentDepth).Append(" e=").Append(r.ValueIsEscaped).Append(" v=").Append(Show(r.ValueSpan));
                DetailsReference(ref r, sb);
                sb.Append('\n');
                if (skip && (r.TokenType == S.JsonTokenType.PropertyName || r.TokenType == S.JsonTokenType.StartArray) && r.CurrentDepth <= 2)
                {
                    r.Skip();
                    sb.Append("skip ").Append(r.TokenType).Append(" c=").Append(r.BytesConsumed).Append(" d=").Append(r.CurrentDepth).Append('\n');
                }
            }
            sb.Append("END ").Append(r.TokenType).Append(" c=").Append(r.BytesConsumed).Append(" s=").Append(r.TokenStartIndex)
                .Append(" d=").Append(r.CurrentDepth).Append(" v=").Append(r.ValueSpan.Length);
        }
        catch (Exception e)
        {
            sb.Append('!').Append(Name(e));
        }
        return sb.ToString();
    }

    static void DetailsReference(ref S.Utf8JsonReader r, StringBuilder sb)
    {
        switch (r.TokenType)
        {
            case S.JsonTokenType.String:
            case S.JsonTokenType.PropertyName:
                try
                {
                    sb.Append(" str=").Append(Show(r.GetString()));
                }
                catch (Exception e)
                {
                    sb.Append(" str=!").Append(Name(e));
                }
                break;
            case S.JsonTokenType.Number:
                sb.Append(" i32=").Append(r.TryGetInt32(out int i32) ? i32.ToString(CultureInfo.InvariantCulture) : "F");
                sb.Append(" i64=").Append(r.TryGetInt64(out long i64) ? i64.ToString(CultureInfo.InvariantCulture) : "F");
                sb.Append(" u32=").Append(r.TryGetUInt32(out uint u32) ? u32.ToString(CultureInfo.InvariantCulture) : "F");
                sb.Append(" u64=").Append(r.TryGetUInt64(out ulong u64) ? u64.ToString(CultureInfo.InvariantCulture) : "F");
                sb.Append(" u8=").Append(r.TryGetByte(out byte u8) ? u8.ToString(CultureInfo.InvariantCulture) : "F");
                sb.Append(" m=").Append(r.TryGetDecimal(out decimal m) ? Bits(m) : "F");
                sb.Append(" d=").Append(r.TryGetDouble(out double d) ? Bits(d) : "F");
                sb.Append(" f=").Append(r.TryGetSingle(out float f) ? Bits(f) : "F");
                try
                {
                    sb.Append(" g=").Append(r.GetInt32()).Append(r.GetByte()).Append(r.GetUInt32()).Append(r.GetDecimal());
                }
                catch (Exception e)
                {
                    sb.Append(" g=!").Append(Name(e));
                }
                sb.Append(" gd=").Append(Bits(r.GetDouble())).Append(Bits(r.GetSingle()));
                break;
            case S.JsonTokenType.True:
            case S.JsonTokenType.False:
                sb.Append(" b=").Append(r.GetBoolean());
                break;
        }
        if (r.TokenType != S.JsonTokenType.Number)
        {
            try
            {
                r.GetInt32();
            }
            catch (Exception e)
            {
                sb.Append(" n=!").Append(Name(e));
            }
        }
        if (r.TokenType != S.JsonTokenType.True && r.TokenType != S.JsonTokenType.False)
        {
            try
            {
                r.GetBoolean();
            }
            catch (Exception e)
            {
                sb.Append(" bo=!").Append(Name(e));
            }
        }
        if (r.TokenType != S.JsonTokenType.String && r.TokenType != S.JsonTokenType.PropertyName)
        {
            try
            {
                sb.Append(" so=").Append(Show(r.GetString()));
            }
            catch (Exception e)
            {
                sb.Append(" so=!").Append(Name(e));
            }
        }
    }
}
