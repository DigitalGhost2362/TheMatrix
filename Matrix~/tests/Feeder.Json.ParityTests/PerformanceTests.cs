using System.Diagnostics;
using System.Globalization;
using System.Text;
using Xunit.Abstractions;
using S = System.Text.Json;

namespace Feeder.Json.ParityTests;

public class PerformanceTests
{
    readonly ITestOutputHelper output;

    public PerformanceTests(ITestOutputHelper output) => this.output = output;

    // deterministic ~5 MB document shaped like tool results: objects, arrays, strings with escapes, floats
    static byte[] BuildDocument()
    {
        var random = new Random(42);
        var sb = new StringBuilder("{\"items\":[");
        int i = 0;
        while (sb.Length < 5_000_000)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append("{\"id\":").Append(i).Append(",\"name\":\"GameObject_").Append(i).Append(" \\\"quoted\\\" \\u00e9\",\"active\":")
                .Append(random.Next(2) == 0 ? "true" : "false").Append(",\"position\":{\"x\":")
                .Append(((float)(random.NextDouble() * 200 - 100)).ToString("R", CultureInfo.InvariantCulture)).Append(",\"y\":")
                .Append(((float)random.NextDouble()).ToString("R", CultureInfo.InvariantCulture)).Append(",\"z\":")
                .Append((random.NextDouble() * 1e6).ToString("R", CultureInfo.InvariantCulture)).Append("},\"tags\":[\"a\",\"b\",null],\"parent\":null}");
            i++;
        }
        sb.Append("],\"count\":").Append(i).Append('}');
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    static (int Strings, double Sum, int Nodes) Walk(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
            {
                (int s, double d, int n) = (0, 0, 1);
                foreach (JsonProperty p in e.EnumerateObject())
                {
                    var (cs, cd, cn) = Walk(p.Value);
                    s += cs + (p.Name.Length > 0 ? 1 : 0);
                    d += cd;
                    n += cn;
                }
                return (s, d, n);
            }
            case JsonValueKind.Array:
            {
                (int s, double d, int n) = (0, 0, 1);
                foreach (JsonElement item in e.EnumerateArray())
                {
                    var (cs, cd, cn) = Walk(item);
                    s += cs;
                    d += cd;
                    n += cn;
                }
                return (s, d, n);
            }
            case JsonValueKind.String:
                return (e.GetString()!.Length > 0 ? 1 : 0, 0, 1);
            case JsonValueKind.Number:
                return (0, e.GetSingle() + e.GetDouble(), 1);
            default:
                return (0, 0, 1);
        }
    }

    static (int Strings, double Sum, int Nodes) WalkReference(S.JsonElement e)
    {
        switch (e.ValueKind)
        {
            case S.JsonValueKind.Object:
            {
                (int s, double d, int n) = (0, 0, 1);
                foreach (S.JsonProperty p in e.EnumerateObject())
                {
                    var (cs, cd, cn) = WalkReference(p.Value);
                    s += cs + (p.Name.Length > 0 ? 1 : 0);
                    d += cd;
                    n += cn;
                }
                return (s, d, n);
            }
            case S.JsonValueKind.Array:
            {
                (int s, double d, int n) = (0, 0, 1);
                foreach (S.JsonElement item in e.EnumerateArray())
                {
                    var (cs, cd, cn) = WalkReference(item);
                    s += cs;
                    d += cd;
                    n += cn;
                }
                return (s, d, n);
            }
            case S.JsonValueKind.String:
                return (e.GetString()!.Length > 0 ? 1 : 0, 0, 1);
            case S.JsonValueKind.Number:
                return (0, e.GetSingle() + e.GetDouble(), 1);
            default:
                return (0, 0, 1);
        }
    }

    [Fact]
    public void FiveMegabyteDocumentParsesWellUnderASecond()
    {
        byte[] json = BuildDocument();
        using (JsonDocument warm = JsonDocument.Parse(json))
            Walk(warm.RootElement);

        var watch = Stopwatch.StartNew();
        using JsonDocument d = JsonDocument.Parse(json);
        long parsed = watch.ElapsedMilliseconds;
        var result = Walk(d.RootElement);
        long walked = watch.ElapsedMilliseconds;

        var reference = Stopwatch.StartNew();
        using S.JsonDocument r = S.JsonDocument.Parse(json);
        var expected = WalkReference(r.RootElement);
        long referenceMs = reference.ElapsedMilliseconds;

        output.WriteLine($"{json.Length} bytes: parse {parsed} ms, parse+walk {walked} ms (reference parse+walk {referenceMs} ms)");
        Assert.Equal(expected, result);
        Assert.True(walked < 1000, "parse+walk took " + walked + " ms");
    }

    [Fact]
    public void WriterThroughputIsReasonable()
    {
        byte[] json = BuildDocument();
        using JsonDocument d = JsonDocument.Parse(json);
        var watch = Stopwatch.StartNew();
        var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
            d.RootElement.WriteTo(w);
        long elapsed = watch.ElapsedMilliseconds;
        output.WriteLine($"rewrote {ms.Length} bytes in {elapsed} ms");
        Assert.True(elapsed < 1000, "rewrite took " + elapsed + " ms");
    }
}
