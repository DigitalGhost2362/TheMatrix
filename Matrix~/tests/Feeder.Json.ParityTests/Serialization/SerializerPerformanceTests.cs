using System.Diagnostics;
using Xunit.Abstractions;
using S = System.Text.Json;

namespace Feeder.Json.ParityTests.SerializerParity;

public class SerializerPerformanceTests
{
    readonly ITestOutputHelper output;

    public SerializerPerformanceTests(ITestOutputHelper output) => this.output = output;

    // ~8 MB of member trees plus a 4 MB byte payload, shaped like tool results and screenshots
    static (List<Member> Members, Dto Payload) Build()
    {
        var random = new Random(7);
        var members = new List<Member>();
        for (int i = 0; i < 20000; i++)
        {
            var member = new Member { name = "GameObject_" + i + " \"quoted\" \u00e9", typeName = "UnityEngine.Transform", weightValue = random.NextDouble() * 1000 };
            member.fields = new List<Member>();
            for (int k = 0; k < 4; k++)
                member.fields.Add(new Member { name = "f" + k, typeName = "System.Single", weightValue = (float)random.NextDouble() });
            members.Add(member);
        }
        var bytes = new byte[4 * 1024 * 1024];
        random.NextBytes(bytes);
        return (members, new Dto { OperationId = "op", Success = true, ContentJsonUtf8 = bytes, PendingOperations = new[] { "a" } });
    }

    [Fact]
    public void LargePayloadsSerializeAndDeserializeInReasonableTime()
    {
        var (members, payload) = Build();
        var options = OptionSet.Get("reflector").Feeder()!;
        var reference = OptionSet.Get("reflector").Reference()!;
        JsonSerializer.Serialize(members.Take(10).ToList(), options);
        S.JsonSerializer.Serialize(members.Take(10).ToList(), reference);

        var watch = Stopwatch.StartNew();
        string json = JsonSerializer.Serialize(members, options);
        long write = watch.ElapsedMilliseconds;
        watch.Restart();
        List<Member> back = JsonSerializer.Deserialize<List<Member>>(json, options)!;
        long read = watch.ElapsedMilliseconds;
        watch.Restart();
        string payloadJson = JsonSerializer.Serialize(payload, options);
        Dto payloadBack = JsonSerializer.Deserialize<Dto>(payloadJson, options)!;
        long bytes = watch.ElapsedMilliseconds;

        watch.Restart();
        string expected = S.JsonSerializer.Serialize(members, reference);
        long referenceWrite = watch.ElapsedMilliseconds;
        watch.Restart();
        S.JsonSerializer.Deserialize<List<Member>>(expected, reference);
        long referenceRead = watch.ElapsedMilliseconds;
        watch.Restart();
        string expectedPayload = S.JsonSerializer.Serialize(payload, reference);
        S.JsonSerializer.Deserialize<Dto>(expectedPayload, reference);
        long referenceBytes = watch.ElapsedMilliseconds;

        output.WriteLine($"{json.Length} chars: write {write} ms, read {read} ms, 4 MB bytes round trip {bytes} ms (reference {referenceWrite} / {referenceRead} / {referenceBytes} ms)");
        Assert.Equal(expected, json);
        Assert.Equal(expectedPayload, payloadJson);
        Assert.Equal(members.Count, back.Count);
        Assert.Equal(payload.ContentJsonUtf8, payloadBack.ContentJsonUtf8);
        Assert.True(write < 3000, "write took " + write + " ms");
        Assert.True(read < 3000, "read took " + read + " ms");
        Assert.True(bytes < 2000, "bytes took " + bytes + " ms");
    }

    [Fact]
    public void SharedOptionsAreSafeAcrossThreads()
    {
        var options = OptionSet.Get("reflector").Feeder()!;
        string expected = S.JsonSerializer.Serialize(Collections.Sample(), OptionSet.Get("reflector").Reference()!);
        var results = new string[64];
        Parallel.For(0, results.Length, i =>
        {
            object value = (i % 3) switch { 0 => Collections.Sample(), 1 => Member.Sample(), _ => Primitives.Sample() };
            string json = JsonSerializer.Serialize(value, value.GetType(), options);
            JsonSerializer.Deserialize(json, value.GetType(), options);
            results[i] = i % 3 == 0 ? json : expected;
        });
        Assert.All(results, r => Assert.Equal(expected, r));
    }
}
