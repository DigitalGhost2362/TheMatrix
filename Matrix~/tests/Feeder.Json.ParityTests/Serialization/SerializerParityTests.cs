using System.Text;
using Feeder.Json.Nodes;
using Feeder.Json.Serialization;
using static Feeder.Json.ParityTests.TestSupport;
using S = System.Text.Json;
using SN = System.Text.Json.Nodes;
using SS = System.Text.Json.Serialization;

namespace Feeder.Json.ParityTests.SerializerParity;

public class SerializerParityTests
{
    static readonly (string Name, Func<object?> Create, Type Type)[] samples =
    {
        ("primitives", () => Primitives.Sample(), typeof(Primitives)),
        ("primitives-default", () => new Primitives(), typeof(Primitives)),
        ("collections", () => Collections.Sample(), typeof(Collections)),
        ("collections-empty", () => new Collections(), typeof(Collections)),
        ("config", () => new DerivedConfig { Tools = { new Feature { Name = "a" } }, LocalToken = "tok" }, typeof(DerivedConfig)),
        ("member", () => Member.Sample(), typeof(Member)),
        ("tree", () => TreeNode.Sample(), typeof(TreeNode)),
        ("struct", () => new WithStruct { Location = new Point { X = 1, Y = -1 }, Maybe = new Point(), Path = new List<Point> { new() { X = 3 } } }, typeof(WithStruct)),
        ("annotated", () => new Annotated(), typeof(Annotated)),
        ("interface", () => new HasInterface(), typeof(HasInterface)),
        ("wrapper", () => new Wrapper { Items = { new Child { Name = "w" } }, Note = "n" }, typeof(Wrapper)),
        ("logged", () => new Logged { LogType = Mode.On, Message = "m\n", Timestamp = new DateTime(2026, 10, 6, 1, 2, 3, DateTimeKind.Local).AddTicks(70) }, typeof(Logged)),
        ("dto", () => new Dto { OperationId = "op", Success = true, ContentJsonUtf8 = Encoding.UTF8.GetBytes("{\"a\":\"\\u00e9 + /\"}"), PendingOperations = new[] { "x" }, EditorState = Mode.Auto, DeadlineUnixMs = 1700000000000 }, typeof(Dto)),
        ("objects", () => new List<object?> { 1, "s", 2.5, true, null, new Child { Name = "c" }, new[] { 1, 2 }, new Dictionary<string, int> { ["k"] = 1 }, Mode.On, Guid.Empty, new DateTime(2020, 1, 1), 3L, 4.5f, 'c', (byte)7, 1.25m, new object(), Rights.Read | Rights.Write }, typeof(List<object?>)),
        ("dictionary", () => new Dictionary<string, Child?> { ["a"] = new(), ["b"] = null }, typeof(Dictionary<string, Child?>)),
        ("nullables", () => new int?[] { 1, null, 3 }, typeof(int?[])),
        ("string", () => "plain \u00e9 <tag>", typeof(string)),
        ("number", () => 12.5, typeof(double)),
        ("nan", () => double.NaN, typeof(double)),
        ("nan-object", () => double.NegativeInfinity, typeof(object)),
        ("float-nan", () => float.NaN, typeof(float)),
        ("deep", () => Deep(70), typeof(List<object>)),
        ("shallow", () => Deep(60), typeof(List<object>)),
        ("edge", () => Deep(64), typeof(List<object>)),
        ("edge-null", () => Deep(64, withNull: true), typeof(List<object>)),
        ("conflict", () => new Conflict { name = "a", Name = "b" }, typeof(Conflict)),
        ("throwing", () => new ThrowingGetter(), typeof(ThrowingGetter)),
        ("type", () => typeof(string), typeof(Type)),
        ("enum-undefined", () => (Mode)42, typeof(Mode)),
        ("flags", () => Rights.Read | Rights.Write, typeof(Rights)),
        ("huge", () => Huge.Max, typeof(Huge)),
        ("null", () => null, typeof(Child)),
        ("multi-dim", () => new int[2, 2], typeof(int[,])),
    };

    // round trips compare the object graphs through the reference writer; members typed object hold library-specific elements
    static readonly HashSet<string> plainGraphs = new() { "primitives", "primitives-default", "config", "member", "struct", "annotated", "wrapper", "logged", "dto", "dictionary", "nullables" };

    static List<object> Deep(int depth, bool withNull = false)
    {
        var root = new List<object>();
        var current = root;
        for (int i = 1; i < depth; i++)
        {
            var next = new List<object>();
            current.Add(next);
            current = next;
        }
        if (withNull)
            current.Add(null!);
        return root;
    }

    static (Func<object?> Create, Type Type) Sample(string name)
    {
        var sample = samples.First(s => s.Name == name);
        return (sample.Create, sample.Type);
    }

    public static IEnumerable<object[]> SampleCases() =>
        from set in OptionSet.All from sample in samples select new object[] { set.Name, sample.Name };

    [Theory]
    [MemberData(nameof(SampleCases))]
    public void SerializeMatches(string optionSet, string sample)
    {
        OptionSet set = OptionSet.Get(optionSet);
        var (create, type) = Sample(sample);
        string expected = Try(() => S.JsonSerializer.Serialize(create(), type, set.Reference()))
            + "\n" + Try(() => S.JsonSerializer.Serialize<object?>(create(), set.Reference()))
            + "\n" + Try(() => Encoding.UTF8.GetString(S.JsonSerializer.SerializeToUtf8Bytes(create(), type, set.Reference())));
        string actual = Try(() => JsonSerializer.Serialize(create(), type, set.Feeder()))
            + "\n" + Try(() => JsonSerializer.Serialize<object?>(create(), set.Feeder()))
            + "\n" + Try(() => Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(create(), type, set.Feeder())));
        ParityAssert.Same(expected, actual, "serialize_" + optionSet + "_" + sample);
    }

    [Theory]
    [MemberData(nameof(SampleCases))]
    public void RoundTripMatches(string optionSet, string sample)
    {
        OptionSet set = OptionSet.Get(optionSet);
        var (create, type) = Sample(sample);
        string json;
        try
        {
            json = S.JsonSerializer.Serialize(create(), type, set.Reference());
        }
        catch (Exception)
        {
            return;
        }
        object? referenceResult = null, feederResult = null;
        string expected = Try(() =>
        {
            S.JsonSerializerOptions? options = set.Reference();
            referenceResult = S.JsonSerializer.Deserialize(json, type, options);
            return S.JsonSerializer.Serialize(referenceResult, type, options);
        });
        string actual = Try(() =>
        {
            JsonSerializerOptions? options = set.Feeder();
            feederResult = JsonSerializer.Deserialize(json, type, options);
            return JsonSerializer.Serialize(feederResult, type, options);
        });
        ParityAssert.Same(expected, actual, "roundtrip_" + optionSet + "_" + sample);
        if (plainGraphs.Contains(sample))
        {
            var canonical = new S.JsonSerializerOptions { IncludeFields = true, ReferenceHandler = SS.ReferenceHandler.IgnoreCycles };
            ParityAssert.Same(
                Try(() => S.JsonSerializer.Serialize(referenceResult, type, canonical)),
                Try(() => S.JsonSerializer.Serialize(feederResult, type, canonical)),
                "graph_" + optionSet + "_" + sample);
        }
    }

    static readonly (string Json, Type Type)[] inputs =
    {
        ("{\"Int32\":\"12\"}", typeof(Primitives)),
        ("{\"Int32\":\"+12\",\"Int64\":\"-007\",\"UInt64\":\"18446744073709551615\",\"Byte\":\"255\",\"SByte\":\"-1\",\"UInt16\":\"1\",\"Int16\":\"-2\",\"UInt32\":\"3\"}", typeof(Primitives)),
        ("{\"Int32\":\" 12\"}", typeof(Primitives)),
        ("{\"Int32\":\"1.0\"}", typeof(Primitives)),
        ("{\"Int32\":12.5}", typeof(Primitives)),
        ("{\"Int32\":1e2}", typeof(Primitives)),
        ("{\"Int32\":null}", typeof(Primitives)),
        ("{\"Int32\":true}", typeof(Primitives)),
        ("{\"NullableInt\":null,\"String\":null,\"Uri\":null,\"Bytes\":null}", typeof(Primitives)),
        ("{\"NullableInt\":\"5\",\"NullableDouble\":\"2.5\"}", typeof(Primitives)),
        ("{\"Byte\":256}", typeof(Primitives)),
        ("{\"SByte\":-129}", typeof(Primitives)),
        ("{\"Int16\":40000}", typeof(Primitives)),
        ("{\"UInt16\":-1}", typeof(Primitives)),
        ("{\"UInt32\":4294967296}", typeof(Primitives)),
        ("{\"UInt64\":-1}", typeof(Primitives)),
        ("{\"Single\":\"NaN\",\"Double\":\"-Infinity\",\"NullableDouble\":\"Infinity\"}", typeof(Primitives)),
        ("{\"Double\":\"nan\"}", typeof(Primitives)),
        ("{\"Double\":\"1e400\"}", typeof(Primitives)),
        ("{\"Double\":\".5\",\"Single\":\"5.\"}", typeof(Primitives)),
        ("{\"Double\":\"1.7976931348623157e308\",\"Single\":\"3.4028235e38\"}", typeof(Primitives)),
        ("{\"Single\":1e39,\"Double\":1e400}", typeof(Primitives)),
        ("{\"Decimal\":\"1.50\",\"Double\":\"-0\",\"Single\":\"1e-50\"}", typeof(Primitives)),
        ("{\"Decimal\":1e40}", typeof(Primitives)),
        ("{\"Decimal\":\"1e2\"}", typeof(Primitives)),
        ("{\"Char\":\"ab\"}", typeof(Primitives)),
        ("{\"Char\":\"\"}", typeof(Primitives)),
        ("{\"Char\":5}", typeof(Primitives)),
        ("{\"Char\":\"\\u00e9\"}", typeof(Primitives)),
        ("{\"String\":5}", typeof(Primitives)),
        ("{\"String\":{}}", typeof(Primitives)),
        ("{\"Bool\":\"true\"}", typeof(Primitives)),
        ("{\"Bool\":1}", typeof(Primitives)),
        ("{\"Utc\":\"2024-01-01\"}", typeof(Primitives)),
        ("{\"Utc\":\"2024-01-01T10:00\"}", typeof(Primitives)),
        ("{\"Utc\":\"2024-01-01T10:00:00.123456789Z\"}", typeof(Primitives)),
        ("{\"Utc\":\"2024-01-01T10:00:00.1234567890123456Z\"}", typeof(Primitives)),
        ("{\"Utc\":\"2024-01-01T10:00:00.12345678901234567Z\"}", typeof(Primitives)),
        ("{\"Utc\":\"2024-02-30T00:00:00\"}", typeof(Primitives)),
        ("{\"Utc\":\"2024-13-01T00:00:00\"}", typeof(Primitives)),
        ("{\"Utc\":\"2024-01-01T24:00:00\"}", typeof(Primitives)),
        ("{\"Utc\":\"2024-01-01 10:00:00\"}", typeof(Primitives)),
        ("{\"Utc\":\"2024-01-01T10:00:00z\"}", typeof(Primitives)),
        ("{\"Utc\":\"0000-01-01T00:00:00\"}", typeof(Primitives)),
        ("{\"Utc\":\"2024-01-01T10:00:00.\"}", typeof(Primitives)),
        ("{\"Utc\":\"2024-01-01T10:00:00.Z\"}", typeof(Primitives)),
        ("{\"Utc\":\"2024-01-01T10:00Z\"}", typeof(Primitives)),
        ("{\"Utc\":5}", typeof(Primitives)),
        ("{\"Utc\":\"2024-01-01T10:00:00\\u002B01:00\"}", typeof(Primitives)),
        ("{\"Offset\":\"2024-01-01T10:00:00+05\"}", typeof(Primitives)),
        ("{\"Offset\":\"2024-01-01T10:00:00\"}", typeof(Primitives)),
        ("{\"Offset\":\"2024-01-01T10:00:00+14:00\"}", typeof(Primitives)),
        ("{\"Offset\":\"2024-01-01T10:00:00+14:01\"}", typeof(Primitives)),
        ("{\"Offset\":\"2024-01-01T10:00:00-02:30\",\"Local\":\"2024-06-01T10:00:00+05:30\",\"Unspecified\":\"2024-06-01T10:00:00Z\"}", typeof(Primitives)),
        ("{\"Offset\":\"0001-01-01T00:00:00+01:00\"}", typeof(Primitives)),
        ("{\"Guid\":\"0a1b2c3d4e5f60718293a4b5c6d7e8f9\"}", typeof(Primitives)),
        ("{\"Guid\":\"{0A1B2C3D-4E5F-6071-8293-A4B5C6D7E8F9}\"}", typeof(Primitives)),
        ("{\"Guid\":\"0A1B2C3D-4E5F-6071-8293-A4B5C6D7E8F9\"}", typeof(Primitives)),
        ("{\"Span\":\"1.02:03:04.5\"}", typeof(Primitives)),
        ("{\"Span\":\"-00:00:01\"}", typeof(Primitives)),
        ("{\"Span\":\" 1:00:00\"}", typeof(Primitives)),
        ("{\"Span\":\"1:00\"}", typeof(Primitives)),
        ("{\"Span\":\"1\"}", typeof(Primitives)),
        ("{\"Span\":\"24:00:00\"}", typeof(Primitives)),
        ("{\"Uri\":\"http://x/a b\"}", typeof(Primitives)),
        ("{\"Uri\":\"\"}", typeof(Primitives)),
        ("{\"Bytes\":\"AQID\"}", typeof(Primitives)),
        ("{\"Bytes\":\"\"}", typeof(Primitives)),
        ("{\"Bytes\":\"AQI\"}", typeof(Primitives)),
        ("{\"Bytes\":\"AQ ID\"}", typeof(Primitives)),
        ("{\"Bytes\":\"AQI=\"}", typeof(Primitives)),
        ("{\"Bytes\":\"AQ\\u002BD\"}", typeof(Primitives)),
        ("{\"Bytes\":[1,2]}", typeof(Primitives)),
        ("{\"Mode\":\"On\"}", typeof(Primitives)),
        ("{\"Mode\":\"on\"}", typeof(Primitives)),
        ("{\"Mode\":\" On \"}", typeof(Primitives)),
        ("{\"Mode\":\"5\"}", typeof(Primitives)),
        ("{\"Mode\":\"Nope\"}", typeof(Primitives)),
        ("{\"Mode\":1}", typeof(Primitives)),
        ("{\"Mode\":42}", typeof(Primitives)),
        ("{\"Mode\":1.5}", typeof(Primitives)),
        ("{\"Mode\":true}", typeof(Primitives)),
        ("{\"Rights\":\"Read, Exec\"}", typeof(Primitives)),
        ("{\"Rights\":\"Read,Write\"}", typeof(Primitives)),
        ("{\"Huge\":18446744073709551615,\"NullableMode\":null}", typeof(Primitives)),
        ("{\"Huge\":-1}", typeof(Primitives)),
        ("{\"NullableMode\":\"Off\",\"NullableDate\":null,\"NullableGuid\":\"0a1b2c3d-4e5f-6071-8293-a4b5c6d7e8f9\"}", typeof(Primitives)),
        ("{\"int32\":5,\"BOOL\":true,\"string\":\"s\"}", typeof(Primitives)),
        ("{\"Unknown\":{\"a\":[1,2,{}]},\"Int32\":3,\"Other\":null}", typeof(Primitives)),
        ("{\"Int32\":1,\"Int32\":2,\"int32\":3}", typeof(Primitives)),
        ("[1,2,3]", typeof(Primitives)),
        ("5", typeof(Child)),
        ("\"x\"", typeof(List<int>)),
        ("{}", typeof(int[])),
        ("[1,", typeof(int[])),
        ("[1,]", typeof(int[])),
        ("{\"Name\":\"a\"} x", typeof(Child)),
        ("{\"Name\":\"a\"}   ", typeof(Child)),
        ("", typeof(Child)),
        ("   ", typeof(Child)),
        ("null", typeof(Child)),
        ("null", typeof(int)),
        ("null", typeof(int?)),
        ("null", typeof(Mode)),
        ("null", typeof(object)),
        ("{\"a\":[1,{\"b\":null}],\"c\":\"\\u00e9\"}", typeof(object)),
        ("[1,\"2\",true,null,{\"x\":1.50}]", typeof(List<object>)),
        ("/*c*/{\"Name\":\"x\",//d\n\"Value\":1}", typeof(Child)),
        ("{\"Name\":\"x\",}", typeof(Child)),
        ("{\"Name\":\"x\" \"Value\":1}", typeof(Child)),
        ("{\"Name\":\"x\\ud800\"}", typeof(Child)),
        ("{\"items\":[{\"Name\":\"a\",\"Value\":1}],\"note\":null}", typeof(Wrapper)),
        ("{\"items\":[{\"Name\":\"a\",\"Value\":\"bad\"}],\"note\":null}", typeof(Wrapper)),
        ("{\"items\":[{\"Name\":\"a\"},],\"note\":\"n\"}", typeof(Wrapper)),
        ("{\"Items\":[{\"Name\":\"a\"}],\"Note\":\"n\"}", typeof(Wrapper)),
        ("{\"Thing\":{\"A\":1}}", typeof(HasInterface)),
        ("{\"Thing\":null,\"Boxed\":{\"A\":1}}", typeof(HasInterface)),
        ("{\"A\":1}", typeof(AbstractThing)),
        ("{\"Location\":{\"X\":1,\"Y\":2},\"Maybe\":null,\"Path\":[{\"X\":1}]}", typeof(WithStruct)),
        ("{\"Location\":[1,2],\"Maybe\":[3,4],\"Path\":[[5,6]]}", typeof(WithStruct)),
        ("{\"Location\":[1,2,3]}", typeof(WithStruct)),
        ("{\"Location\":null}", typeof(WithStruct)),
        ("{\"display name\":\"x\",\"Mode\":\"On\",\"Rights\":\"Read, Write\",\"Where\":[7,8]}", typeof(Annotated)),
        ("{\"Mode\":1,\"Rights\":null}", typeof(Annotated)),
        ("{\"name\":\"n\",\"typeName\":\"t\",\"weight\":1.5,\"publicField\":3,\"readonlyField\":9,\"hidden\":4,\"PrivateSetter\":8,\"GetterOnly\":1,\"SetterOnly\":6,\"Old\":1,\"oldField\":2,\"fields\":[{\"typeName\":\"x\"}]}", typeof(Member)),
        ("{\"NAME\":\"n\",\"weight\":null,\"fields\":null}", typeof(Member)),
        ("{\"host\":\"h\",\"Host\":\"H\",\"token\":\"t\",\"Level\":\"Auto\",\"tools\":[{\"name\":\"a\",\"enabled\":false}],\"flags\":{\"x\":true},\"ProjectRootPath\":\"p\",\"timeoutMs\":5,\"override\":[\"o\"]}", typeof(DerivedConfig)),
        ("{\"Map\":{\"a\":1,\"a\":2},\"Set\":[\"x\",\"x\"],\"Untyped\":[1,\"a\",null,{}],\"ReadOnlyMap\":{\"k\":{\"Name\":\"v\"}},\"Custom\":[\"c\"],\"Derived\":[{}],\"Jagged\":[[1],[]],\"Nullables\":[null,2]}", typeof(Collections)),
        ("{\"Map\":[],\"Set\":{}}", typeof(Collections)),
        ("{\"Map\":{\"a\":null}}", typeof(Collections)),
        ("[[1,2],[3]]", typeof(int[][])),
        ("{\"a\":{\"Name\":\"x\"},\"b\":null}", typeof(Dictionary<string, Child?>)),
        ("{\"a\":1}", typeof(Dictionary<string, object>)),
        ("{\"Children\":[{\"Name\":\"c\"}],\"Tag\":{\"a\":1},\"Map\":{\"k\":[1]}}", typeof(TreeNode)),
        ("{\"LogType\":1,\"Message\":\"m\",\"Timestamp\":\"2026-10-06T14:10:23.0000005+07:00\",\"StackTrace\":null}", typeof(Logged)),
        ("{\"logtype\":\"On\",\"timestamp\":\"2026-10-06T14:10:23\"}", typeof(Logged)),
        ("{\"operationId\":\"op\",\"contentJsonUtf8\":\"eyJhIjoxfQ==\",\"pendingOperations\":[\"a\"],\"editorState\":2,\"deadlineUnixMs\":1}", typeof(Dto)),
        ("{\"OperationId\":\"op\",\"ContentJsonUtf8\":null,\"PendingOperations\":null}", typeof(Dto)),
        ("\"Auto\"", typeof(Mode)),
        ("\"Read, Write\"", typeof(Rights)),
        ("\"2024-01-01T00:00:00Z\"", typeof(DateTime?)),
        ("[\"AQID\",null]", typeof(List<byte[]?>)),
        ("{\"x\":[1,2]}", typeof(Dictionary<string, int[]>)),
        ("[{\"X\":1,\"Y\":2}]", typeof(Point[])),
        ("[[1,2]]", typeof(Point[])),
    };

    public static IEnumerable<object[]> InputCases() =>
        from set in OptionSet.All from i in Enumerable.Range(0, inputs.Length) select new object[] { set.Name, i };

    [Theory]
    [MemberData(nameof(InputCases))]
    public void DeserializeMatches(string optionSet, int index)
    {
        OptionSet set = OptionSet.Get(optionSet);
        var (json, type) = inputs[index];
        string expected = Try(() =>
        {
            S.JsonSerializerOptions? options = set.Reference();
            return S.JsonSerializer.Serialize(S.JsonSerializer.Deserialize(json, type, options), type, options);
        });
        string actual = Try(() =>
        {
            JsonSerializerOptions? options = set.Feeder();
            return JsonSerializer.Serialize(JsonSerializer.Deserialize(json, type, options), type, options);
        });
        ParityAssert.Same(expected, actual, "deserialize_" + optionSet + "_" + index);
    }

    [Theory]
    [MemberData(nameof(InputCases))]
    public void FailuresReportTheSameLocation(string optionSet, int index)
    {
        OptionSet set = OptionSet.Get(optionSet);
        var (json, type) = inputs[index];
        string expected = Describe(() => S.JsonSerializer.Deserialize(json, type, set.Reference()));
        string actual = Describe(() => JsonSerializer.Deserialize(json, type, set.Feeder()));
        ParityAssert.Same(expected, actual, "failure_" + optionSet + "_" + index);
    }

    static string Describe(Action action)
    {
        try
        {
            action();
            return "ok";
        }
        catch (S.JsonException e)
        {
            return "JsonException path=" + e.Path + " line=" + e.LineNumber + " byte=" + e.BytePositionInLine + " inner=" + (e.InnerException == null ? "-" : Name(e.InnerException));
        }
        catch (JsonException e)
        {
            return "JsonException path=" + e.Path + " line=" + e.LineNumber + " byte=" + e.BytePositionInLine + " inner=" + (e.InnerException == null ? "-" : Name(e.InnerException));
        }
        catch (Exception e)
        {
            return Name(e);
        }
    }

    [Fact]
    public void CollectionShapesMatch()
    {
        const string json = "{\"Array\":[1],\"List\":[\"a\"],\"IList\":[1],\"Enumerable\":[\"e\"],\"ReadOnlyList\":[1.5],\"Collection\":[2],\"ReadOnlyCollection\":[true],\"Set\":[\"s\"],\"ISet\":[3],\"Map\":{\"m\":1},\"IMap\":{\"i\":null},\"ReadOnlyMap\":{\"r\":null},\"Nested\":{\"n\":[]},\"Children\":[],\"Derived\":[],\"Jagged\":[],\"Nullables\":[],\"Custom\":[],\"Untyped\":[1]}";
        Collections expected = S.JsonSerializer.Deserialize<Collections>(json)!;
        Collections actual = JsonSerializer.Deserialize<Collections>(json)!;
        foreach (var property in typeof(Collections).GetProperties())
        {
            object? e = property.GetValue(expected), a = property.GetValue(actual);
            string Shape(object? v) => v == null ? "null" : v.GetType().Name + (v is System.Collections.IEnumerable items && v.GetType().IsGenericType ? "" : "");
            Assert.Equal(property.Name + ":" + Shape(e), property.Name + ":" + Shape(a));
        }
        Assert.Equal("JsonElement", ((System.Collections.IList)actual.Untyped!)[0]!.GetType().Name);
    }

    [Fact]
    public void DocumentElementAndNodeEntryPointsMatch()
    {
        object value = Member.Sample();
        var sb = new StringBuilder();
        var fo = OptionSet.Get("reflector").Feeder()!;
        var so = OptionSet.Get("reflector").Reference()!;
        string expected = string.Join("\n",
            Try(() => S.JsonSerializer.SerializeToNode(value, so)!.ToJsonString()),
            Try(() => S.JsonSerializer.SerializeToNode(value, so)!["NAME"]?.ToJsonString() ?? "(null)"),
            Try(() => S.JsonSerializer.SerializeToNode(value, typeof(Member), so)!.ToJsonString(so)),
            Try(() => S.JsonSerializer.SerializeToElement(value, so).GetRawText()),
            Try(() => { using var d = S.JsonSerializer.SerializeToDocument(value, so); return d.RootElement.GetProperty("typeName").GetString()!; }),
            Try(() => { using var d = S.JsonSerializer.SerializeToDocument(value, typeof(Member), so); return d.RootElement.GetRawText(); }),
            Try(() => S.JsonSerializer.Serialize(S.JsonSerializer.Deserialize<Member>(S.JsonSerializer.SerializeToElement(value, so), so), so)),
            Try(() => S.JsonSerializer.Serialize(S.JsonSerializer.Deserialize<Member>(S.JsonSerializer.SerializeToNode(value, so), so), so)),
            Try(() => S.JsonSerializer.Serialize(S.JsonSerializer.Deserialize(S.JsonSerializer.SerializeToNode(value, so), typeof(Member), so), so)),
            Try(() => { using var d = S.JsonSerializer.SerializeToDocument(value, so); return S.JsonSerializer.Serialize(S.JsonSerializer.Deserialize<Member>(d, so), so); }),
            Try(() => S.JsonSerializer.Serialize(S.JsonSerializer.Deserialize<Member>((SN.JsonNode?)null, so), so)),
            Try(() => S.JsonSerializer.Serialize(S.JsonSerializer.Deserialize<Member>(Encoding.UTF8.GetBytes("{\"name\":\"bytes\"}"), so), so)),
            Try(() => S.JsonSerializer.Serialize(S.JsonSerializer.Deserialize(Encoding.UTF8.GetBytes("{\"name\":\"bytes\"}"), typeof(Member), so), so)),
            Try(() => S.JsonSerializer.SerializeToNode<object?>(null, so)?.ToJsonString() ?? "(null)"),
            Try(() => { var stream = new MemoryStream(); S.JsonSerializer.Serialize(stream, value, so); S.JsonSerializer.Serialize(stream, 5, so); return Encoding.UTF8.GetString(stream.ToArray()); }));
        string actual = string.Join("\n",
            Try(() => JsonSerializer.SerializeToNode(value, fo)!.ToJsonString()),
            Try(() => JsonSerializer.SerializeToNode(value, fo)!["NAME"]?.ToJsonString() ?? "(null)"),
            Try(() => JsonSerializer.SerializeToNode(value, typeof(Member), fo)!.ToJsonString(fo)),
            Try(() => JsonSerializer.SerializeToElement(value, fo).GetRawText()),
            Try(() => { using var d = JsonSerializer.SerializeToDocument(value, fo); return d.RootElement.GetProperty("typeName").GetString()!; }),
            Try(() => { using var d = JsonSerializer.SerializeToDocument(value, typeof(Member), fo); return d.RootElement.GetRawText(); }),
            Try(() => JsonSerializer.Serialize(JsonSerializer.SerializeToElement(value, fo).Deserialize<Member>(fo), fo)),
            Try(() => JsonSerializer.Serialize(JsonSerializer.SerializeToNode(value, fo).Deserialize<Member>(fo), fo)),
            Try(() => JsonSerializer.Serialize(JsonSerializer.SerializeToNode(value, fo).Deserialize(typeof(Member), fo), fo)),
            Try(() => { using var d = JsonSerializer.SerializeToDocument(value, fo); return JsonSerializer.Serialize(d.Deserialize<Member>(fo), fo); }),
            Try(() => JsonSerializer.Serialize(((JsonNode?)null).Deserialize<Member>(fo), fo)),
            Try(() => JsonSerializer.Serialize(JsonSerializer.Deserialize<Member>(Encoding.UTF8.GetBytes("{\"name\":\"bytes\"}"), fo), fo)),
            Try(() => JsonSerializer.Serialize(JsonSerializer.Deserialize(Encoding.UTF8.GetBytes("{\"name\":\"bytes\"}"), typeof(Member), fo), fo)),
            Try(() => JsonSerializer.SerializeToNode<object?>(null, fo)?.ToJsonString() ?? "(null)"),
            Try(() => { var stream = new MemoryStream(); JsonSerializer.Serialize(stream, value, fo); JsonSerializer.Serialize(stream, 5, fo); return Encoding.UTF8.GetString(stream.ToArray()); }));
        ParityAssert.Same(expected, actual, "entrypoints");
    }

    [Theory]
    [InlineData("{\"before\":1,\"value\":{\"Name\":\"n\",\"Value\":2},\"after\":true}", 3)]
    [InlineData("{\"before\":1,\"value\":null,\"after\":true}", 3)]
    [InlineData("{\"before\":1,\"value\":{\"Name\":\"n\",\"Value\":\"x\"},\"after\":true}", 3)]
    [InlineData("{\"before\":1,\"value\":{\"Name\":\"n\",\"Value\":2,},\"after\":true}", 3)]
    [InlineData("{\"before\":1,\"value\":5,\"after\":true}", 3)]
    [InlineData("{\"value\":{\"Name\":\"n\"}}", 1)]
    [InlineData("{\"value\":{\"Name\":\"n\"}}", 2)]
    [InlineData("{\"value\":{\"Name\":\"n\"}}", 0)]
    [InlineData("{\"value\":{\"Name\":\"n\"}}", 4)]
    public void NestedReaderCallsLeaveTheReaderInTheSamePlace(string json, int reads)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(json);
        string expected = Try(() =>
        {
            var reader = new S.Utf8JsonReader(utf8);
            for (int i = 0; i < reads; i++)
                reader.Read();
            string before = reader.TokenType + "@" + reader.BytesConsumed;
            string result;
            try
            {
                result = S.JsonSerializer.Serialize(S.JsonSerializer.Deserialize<Child>(ref reader));
            }
            catch (Exception e)
            {
                result = "!" + Name(e);
            }
            return before + " " + result + " " + reader.TokenType + "@" + reader.BytesConsumed + "/" + reader.CurrentDepth;
        });
        string actual = Try(() =>
        {
            var reader = new Utf8JsonReader(utf8);
            for (int i = 0; i < reads; i++)
                reader.Read();
            string before = reader.TokenType + "@" + reader.BytesConsumed;
            string result;
            try
            {
                result = JsonSerializer.Serialize(JsonSerializer.Deserialize<Child>(ref reader));
            }
            catch (Exception e)
            {
                result = "!" + Name(e);
            }
            return before + " " + result + " " + reader.TokenType + "@" + reader.BytesConsumed + "/" + reader.CurrentDepth;
        });
        ParityAssert.Same(expected, actual, "nested_" + reads + "_" + json.Length);
    }

    [Fact]
    public void ConverterContractsAreEnforcedTheSameWay()
    {
        var fo = new JsonSerializerOptions { Converters = { new FeederLazyChildConverter() } };
        var so = new S.JsonSerializerOptions { Converters = { new ReferenceLazyChildConverter() } };
        string expected = string.Join("|",
            Describe(() => S.JsonSerializer.Deserialize<Child>("{\"Name\":\"a\"}", so)),
            Describe(() => S.JsonSerializer.Deserialize<List<Child>>("[{}]", so)),
            Describe(() => S.JsonSerializer.Serialize(new Child(), so)));
        string actual = string.Join("|",
            Describe(() => JsonSerializer.Deserialize<Child>("{\"Name\":\"a\"}", fo)),
            Describe(() => JsonSerializer.Deserialize<List<Child>>("[{}]", fo)),
            Describe(() => JsonSerializer.Serialize(new Child(), fo)));
        ParityAssert.Same(expected, actual, "contracts");
    }

    [Fact]
    public void OptionsFreezeOnFirstUseButNotOnGetConverter()
    {
        var fo = OptionSet.Get("reflector").Feeder()!;
        var so = OptionSet.Get("reflector").Reference()!;
        string expected = string.Join("|",
            so.GetConverter(typeof(int)).GetType().Name, so.GetConverter(typeof(int?)).GetType().Name, so.GetConverter(typeof(Mode)).GetType().Name,
            so.GetConverter(typeof(Wrapper)).GetType().Name, so.GetConverter(typeof(S.JsonElement)).GetType().Name,
            Try(() => { so.NumberHandling = SS.JsonNumberHandling.AllowNamedFloatingPointLiterals; return "set"; }),
            Try(() => so.GetConverter(typeof(Conflict)).GetType().Name),
            Try(() => { S.JsonSerializer.Serialize(1, so); return "used"; }),
            Try(() => { so.WriteIndented = false; return "set"; }),
            Try(() => { so.Converters.Add(new ReferenceIntAsObject()); return "added"; }),
            Try(() => { so.Converters.Clear(); return "cleared"; }),
            so.GetConverter(typeof(int)).GetType().Name);
        string actual = string.Join("|",
            fo.GetConverter(typeof(int)).GetType().Name.Replace("Feeder", "Reference"), fo.GetConverter(typeof(int?)).GetType().Name.Replace("Feeder", "Reference"),
            fo.GetConverter(typeof(Mode)).GetType().Name.Replace("Feeder", "Reference"),
            fo.GetConverter(typeof(Wrapper)).GetType().Name.Replace("Feeder", "Reference"), fo.GetConverter(typeof(JsonElement)).GetType().Name.Replace("Feeder", "Reference"),
            Try(() => { fo.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals; return "set"; }),
            Try(() => fo.GetConverter(typeof(Conflict)).GetType().Name),
            Try(() => { JsonSerializer.Serialize(1, fo); return "used"; }),
            Try(() => { fo.WriteIndented = false; return "set"; }),
            Try(() => { fo.Converters.Add(new FeederIntAsObject()); return "added"; }),
            Try(() => { fo.Converters.Clear(); return "cleared"; }),
            fo.GetConverter(typeof(int)).GetType().Name.Replace("Feeder", "Reference"));
        ParityAssert.Same(expected, actual, "freeze");
        var plain = new JsonSerializerOptions();
        Assert.Equal("Conflict", Try(() => { plain.IncludeFields = true; plain.PropertyNameCaseInsensitive = true; plain.GetConverter(typeof(Conflict)); return "Conflict"; }));
        Assert.Throws<ArgumentNullException>(() => plain.Converters.Add(null!));
    }

    [Fact]
    public void NodeMembersMatch()
    {
        const string json = "{\"Element\":{\"x\":[1,\"\\u00e9\"]},\"MaybeElement\":null,\"Node\":[1,{\"a\":null}],\"Obj\":{\"k\":\"v\"},\"Arr\":[true],\"Val\":2.50,\"Doc\":{\"d\":1},\"Any\":{\"z\":[]}}";
        foreach (OptionSet set in OptionSet.All)
        {
            string expected = Try(() =>
            {
                var options = set.Reference();
                var value = S.JsonSerializer.Deserialize<ReferenceNodes>(json, options)!;
                return S.JsonSerializer.Serialize(value, options) + "|" + S.JsonSerializer.Serialize(new ReferenceNodes(), options);
            });
            string actual = Try(() =>
            {
                var options = set.Feeder();
                var value = JsonSerializer.Deserialize<FeederNodes>(json, options)!;
                return JsonSerializer.Serialize(value, options) + "|" + JsonSerializer.Serialize(new FeederNodes(), options);
            });
            ParityAssert.Same(expected, actual, "nodes_" + set.Name);
            foreach (string bad in new[] { "{\"Obj\":[1]}", "{\"Arr\":{}}", "{\"Val\":{}}", "{\"Val\":null,\"Node\":null,\"Element\":null}" })
            {
                ParityAssert.Same(
                    Try(() => S.JsonSerializer.Serialize(S.JsonSerializer.Deserialize<ReferenceNodes>(bad, set.Reference()), set.Reference())),
                    Try(() => JsonSerializer.Serialize(JsonSerializer.Deserialize<FeederNodes>(bad, set.Feeder()), set.Feeder())),
                    "nodes_bad_" + set.Name);
            }
        }
    }

    [Fact]
    public void NamingPolicyMatches()
    {
        foreach (string name in new[] { "", "a", "A", "AB", "ABc", "URLValue", "IOStream", "Already_camel", "ID", "X Y", "\u00c9COLE", "PascalCase", "IPAddress2", "AB CD" })
            Assert.Equal(S.JsonNamingPolicy.CamelCase.ConvertName(name), JsonNamingPolicy.CamelCase.ConvertName(name));
    }
}

public class FeederNodes
{
    public JsonElement Element { get; set; }
    public JsonElement? MaybeElement { get; set; }
    public JsonNode? Node { get; set; }
    public JsonObject? Obj { get; set; }
    public JsonArray? Arr { get; set; }
    public JsonValue? Val { get; set; }
    public JsonDocument? Doc { get; set; }
    public object? Any { get; set; }
}

public class ReferenceNodes
{
    public S.JsonElement Element { get; set; }
    public S.JsonElement? MaybeElement { get; set; }
    public SN.JsonNode? Node { get; set; }
    public SN.JsonObject? Obj { get; set; }
    public SN.JsonArray? Arr { get; set; }
    public SN.JsonValue? Val { get; set; }
    public S.JsonDocument? Doc { get; set; }
    public object? Any { get; set; }
}
