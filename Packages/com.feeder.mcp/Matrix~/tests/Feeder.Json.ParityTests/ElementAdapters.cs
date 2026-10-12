using System.Globalization;
using System.Text;
using static Feeder.Json.ParityTests.TestSupport;
using S = System.Text.Json;

namespace Feeder.Json.ParityTests;

// one walk over two element types; every member the package uses is recorded so the walks can be compared as text
internal interface IElement
{
    string Kind { get; }
    string Raw();
    string Text();
    string String();
    string Boolean();
    string Numbers();
    string Length();
    IEnumerable<IElement> Items();
    IEnumerable<(string Name, string Text, IElement Value)> Properties();
    string Lookup(string name);
    string Write(JsonWriterOptions options);
    string WriteProperties();
    string CloneRaw();
    string LinqCount();
}

internal static class ElementWalk
{
    public static string Describe(IElement e)
    {
        var sb = new StringBuilder();
        Describe(e, sb, 0);
        sb.Append("write=").Append(e.Write(default)).Append('\n');
        sb.Append("indented=").Append(e.Write(new JsonWriterOptions { Indented = true })).Append('\n');
        sb.Append("relaxed=").Append(e.Write(new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })).Append('\n');
        sb.Append("default-encoder=").Append(e.Write(new JsonWriterOptions { Encoder = JavaScriptEncoder.Default, Indented = true })).Append('\n');
        sb.Append("clone=").Append(e.CloneRaw()).Append('\n');
        return sb.ToString();
    }

    static void Describe(IElement e, StringBuilder sb, int level)
    {
        sb.Append(' ', level).Append(e.Kind).Append(" raw=").Append(e.Raw()).Append(" text=").Append(e.Text())
            .Append(" str=").Append(e.String()).Append(" bool=").Append(e.Boolean()).Append(" num=").Append(e.Numbers())
            .Append(" len=").Append(e.Length()).Append('\n');
        if (e.Kind == "Array")
        {
            sb.Append(' ', level).Append("linq=").Append(e.LinqCount()).Append('\n');
            foreach (IElement item in e.Items())
                Describe(item, sb, level + 1);
        }
        else if (e.Kind == "Object")
        {
            sb.Append(' ', level).Append("linq=").Append(e.LinqCount()).Append(" props=").Append(e.WriteProperties()).Append('\n');
            var names = new List<string> { "missing", "", "a" };
            foreach (var (name, text, value) in e.Properties())
            {
                sb.Append(' ', level + 1).Append("name=").Append(Show(name)).Append(" ptext=").Append(text).Append('\n');
                Describe(value, sb, level + 2);
                if (!name.StartsWith('!'))
                    names.Add(name);
            }
            foreach (string name in names)
                sb.Append(' ', level + 1).Append("lookup ").Append(Show(name)).Append('=').Append(e.Lookup(name)).Append('\n');
        }
    }
}

internal sealed class FeederElement : IElement
{
    readonly JsonElement e;

    public FeederElement(JsonElement e) => this.e = e;

    public string Kind => e.ValueKind.ToString();

    public string Raw() => Try(() => Show(e.GetRawText()));

    public string Text() => Try(() => Show(e.ToString()));

    public string String() => Try(() => Show(e.GetString()));

    public string Boolean() => Try(() => e.GetBoolean().ToString());

    public string Length() => Try(() => e.GetArrayLength().ToString(CultureInfo.InvariantCulture));

    public string Numbers() => Try(() =>
        string.Join(",",
            e.TryGetInt32(out int i32) ? i32.ToString(CultureInfo.InvariantCulture) : "F",
            e.TryGetInt64(out long i64) ? i64.ToString(CultureInfo.InvariantCulture) : "F",
            e.TryGetUInt32(out uint u32) ? u32.ToString(CultureInfo.InvariantCulture) : "F",
            e.TryGetUInt64(out ulong u64) ? u64.ToString(CultureInfo.InvariantCulture) : "F",
            e.TryGetByte(out byte u8) ? u8.ToString(CultureInfo.InvariantCulture) : "F",
            e.TryGetDecimal(out decimal m) ? Bits(m) : "F",
            e.TryGetDouble(out double d) ? Bits(d) : "F",
            e.TryGetSingle(out float f) ? Bits(f) : "F",
            Try(() => e.GetInt32().ToString(CultureInfo.InvariantCulture)),
            Try(() => e.GetInt64().ToString(CultureInfo.InvariantCulture)),
            Try(() => e.GetUInt64().ToString(CultureInfo.InvariantCulture)),
            Try(() => e.GetUInt32().ToString(CultureInfo.InvariantCulture)),
            Try(() => e.GetByte().ToString(CultureInfo.InvariantCulture)),
            Try(() => Bits(e.GetDecimal())),
            Try(() => Bits(e.GetDouble())),
            Try(() => Bits(e.GetSingle()))));

    public IEnumerable<IElement> Items()
    {
        foreach (JsonElement item in e.EnumerateArray())
            yield return new FeederElement(item);
    }

    public IEnumerable<(string, string, IElement)> Properties()
    {
        foreach (JsonProperty p in e.EnumerateObject())
            yield return (Try(() => p.Name), Try(() => Show(p.ToString())), new FeederElement(p.Value));
    }

    public string Lookup(string name) => Try(() => e.TryGetProperty(name, out JsonElement v) ? Show(v.GetRawText()) + "|" + Show(e.GetProperty(name).GetRawText()) : "F|" + Try(() => e.GetProperty(name).GetRawText()));

    public string Write(JsonWriterOptions options) => Try(() =>
    {
        var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, options))
            e.WriteTo(w);
        return Show(ms.ToArray());
    });

    public string WriteProperties() => Try(() =>
    {
        var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            foreach (JsonProperty p in e.EnumerateObject())
                p.WriteTo(w);
            w.WriteEndObject();
        }
        return Show(ms.ToArray());
    });

    public string CloneRaw() => Try(() => Show(e.Clone().GetRawText()));

    public string LinqCount() => Try(() =>
        e.ValueKind == JsonValueKind.Array
            ? e.EnumerateArray().Count() + "/" + e.EnumerateArray().Select(x => x.ValueKind.ToString()).DefaultIfEmpty().Aggregate((a, b) => a + b)
            : e.EnumerateObject().Count() + "/" + e.EnumerateObject().Select(x => x.Value.ValueKind.ToString()).DefaultIfEmpty().Aggregate((a, b) => a + b));
}

internal sealed class ReferenceElement : IElement
{
    readonly S.JsonElement e;

    public ReferenceElement(S.JsonElement e) => this.e = e;

    public string Kind => e.ValueKind.ToString();

    public string Raw() => Try(() => Show(e.GetRawText()));

    public string Text() => Try(() => Show(e.ToString()));

    public string String() => Try(() => Show(e.GetString()));

    public string Boolean() => Try(() => e.GetBoolean().ToString());

    public string Length() => Try(() => e.GetArrayLength().ToString(CultureInfo.InvariantCulture));

    public string Numbers() => Try(() =>
        string.Join(",",
            e.TryGetInt32(out int i32) ? i32.ToString(CultureInfo.InvariantCulture) : "F",
            e.TryGetInt64(out long i64) ? i64.ToString(CultureInfo.InvariantCulture) : "F",
            e.TryGetUInt32(out uint u32) ? u32.ToString(CultureInfo.InvariantCulture) : "F",
            e.TryGetUInt64(out ulong u64) ? u64.ToString(CultureInfo.InvariantCulture) : "F",
            e.TryGetByte(out byte u8) ? u8.ToString(CultureInfo.InvariantCulture) : "F",
            e.TryGetDecimal(out decimal m) ? Bits(m) : "F",
            e.TryGetDouble(out double d) ? Bits(d) : "F",
            e.TryGetSingle(out float f) ? Bits(f) : "F",
            Try(() => e.GetInt32().ToString(CultureInfo.InvariantCulture)),
            Try(() => e.GetInt64().ToString(CultureInfo.InvariantCulture)),
            Try(() => e.GetUInt64().ToString(CultureInfo.InvariantCulture)),
            Try(() => e.GetUInt32().ToString(CultureInfo.InvariantCulture)),
            Try(() => e.GetByte().ToString(CultureInfo.InvariantCulture)),
            Try(() => Bits(e.GetDecimal())),
            Try(() => Bits(e.GetDouble())),
            Try(() => Bits(e.GetSingle()))));

    public IEnumerable<IElement> Items()
    {
        foreach (S.JsonElement item in e.EnumerateArray())
            yield return new ReferenceElement(item);
    }

    public IEnumerable<(string, string, IElement)> Properties()
    {
        foreach (S.JsonProperty p in e.EnumerateObject())
            yield return (Try(() => p.Name), Try(() => Show(p.ToString())), new ReferenceElement(p.Value));
    }

    public string Lookup(string name) => Try(() => e.TryGetProperty(name, out S.JsonElement v) ? Show(v.GetRawText()) + "|" + Show(e.GetProperty(name).GetRawText()) : "F|" + Try(() => e.GetProperty(name).GetRawText()));

    public string Write(JsonWriterOptions options) => Try(() =>
    {
        var ms = new MemoryStream();
        using (var w = new S.Utf8JsonWriter(ms, ToReference(options)))
            e.WriteTo(w);
        return Show(ms.ToArray());
    });

    public string WriteProperties() => Try(() =>
    {
        var ms = new MemoryStream();
        using (var w = new S.Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            foreach (S.JsonProperty p in e.EnumerateObject())
                p.WriteTo(w);
            w.WriteEndObject();
        }
        return Show(ms.ToArray());
    });

    public string CloneRaw() => Try(() => Show(e.Clone().GetRawText()));

    public string LinqCount() => Try(() =>
        e.ValueKind == S.JsonValueKind.Array
            ? e.EnumerateArray().Count() + "/" + e.EnumerateArray().Select(x => x.ValueKind.ToString()).DefaultIfEmpty().Aggregate((a, b) => a + b)
            : e.EnumerateObject().Count() + "/" + e.EnumerateObject().Select(x => x.Value.ValueKind.ToString()).DefaultIfEmpty().Aggregate((a, b) => a + b));
}
