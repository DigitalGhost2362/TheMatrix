using System.Text;
using Feeder.Json.Serialization;
using static Feeder.Json.ParityTests.TestSupport;
using FS = Feeder.Json.Serialization;
using S = System.Text.Json;
using SS = System.Text.Json.Serialization;

namespace Feeder.Json.ParityTests.SerializerParity;

[FS.JsonConverter(typeof(FeederTaggedConverter)), SS.JsonConverter(typeof(ReferenceTaggedConverter))]
public class Tagged
{
    public string? Value { get; set; }
}

public class HoldsTagged
{
    public Tagged? One { get; set; }
    public List<Tagged>? Many { get; set; }
}

public sealed class FeederTaggedConverter : JsonConverter<Tagged>
{
    public override Tagged? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new() { Value = reader.GetString()!.Substring(4) };

    public override void Write(Utf8JsonWriter writer, Tagged value, JsonSerializerOptions options) => writer.WriteStringValue("tag:" + value.Value);
}

public sealed class ReferenceTaggedConverter : SS.JsonConverter<Tagged>
{
    public override Tagged? Read(ref S.Utf8JsonReader reader, Type typeToConvert, S.JsonSerializerOptions options) =>
        new() { Value = reader.GetString()!.Substring(4) };

    public override void Write(S.Utf8JsonWriter writer, Tagged value, S.JsonSerializerOptions options) => writer.WriteStringValue("tag:" + value.Value);
}

// claims a type it cannot produce: both serializers must refuse it up front
public sealed class FeederWrongConverter : JsonConverter<string>
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(int);

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => "x";

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}

public sealed class ReferenceWrongConverter : SS.JsonConverter<string>
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(int);

    public override string? Read(ref S.Utf8JsonReader reader, Type typeToConvert, S.JsonSerializerOptions options) => "x";

    public override void Write(S.Utf8JsonWriter writer, string value, S.JsonSerializerOptions options) => writer.WriteStringValue(value);
}

public class SerializerApiTests
{
    [Fact]
    public void TypeLevelConverterAttributeApplies()
    {
        var value = new HoldsTagged { One = new Tagged { Value = "a" }, Many = new List<Tagged> { new() { Value = "b" } } };
        string expected = Try(() => S.JsonSerializer.Serialize(value)) + "|" + Try(() => S.JsonSerializer.Serialize(S.JsonSerializer.Deserialize<HoldsTagged>("{\"One\":\"tag:x\",\"Many\":[\"tag:y\",null]}")));
        string actual = Try(() => JsonSerializer.Serialize(value)) + "|" + Try(() => JsonSerializer.Serialize(JsonSerializer.Deserialize<HoldsTagged>("{\"One\":\"tag:x\",\"Many\":[\"tag:y\",null]}")));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IncompatibleConverterIsRejected()
    {
        string expected = Try(() => S.JsonSerializer.Serialize(5, new S.JsonSerializerOptions { Converters = { new ReferenceWrongConverter() } }));
        string actual = Try(() => JsonSerializer.Serialize(5, new JsonSerializerOptions { Converters = { new FeederWrongConverter() } }));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void InputTypeIsValidated()
    {
        string expected = string.Join("|",
            Try(() => S.JsonSerializer.Serialize("text", typeof(int))),
            Try(() => S.JsonSerializer.Serialize((object?)null, typeof(int))),
            Try(() => S.JsonSerializer.Serialize(new Thing(), typeof(IThing))),
            Try(() => S.JsonSerializer.Serialize(5, (Type)null!)),
            Try(() => S.JsonSerializer.Deserialize<int>((string)null!).ToString()));
        string actual = string.Join("|",
            Try(() => JsonSerializer.Serialize("text", typeof(int))),
            Try(() => JsonSerializer.Serialize((object?)null, typeof(int))),
            Try(() => JsonSerializer.Serialize(new Thing(), typeof(IThing))),
            Try(() => JsonSerializer.Serialize(5, (Type)null!)),
            Try(() => JsonSerializer.Deserialize<int>((string)null!).ToString()));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WritingIntoACallersWriterMatches()
    {
        var stream = new MemoryStream();
        using (var writer = new S.Utf8JsonWriter(stream, new S.JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("a");
            S.JsonSerializer.Serialize(writer, new Child { Name = "c" }, new S.JsonSerializerOptions { WriteIndented = false });
            writer.WritePropertyName("b");
            S.JsonSerializer.Serialize(writer, new[] { 1, 2 });
            writer.WriteEndObject();
        }
        string expected = Encoding.UTF8.GetString(stream.ToArray());
        stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("a");
            JsonSerializer.Serialize(writer, new Child { Name = "c" }, new JsonSerializerOptions { WriteIndented = false });
            writer.WritePropertyName("b");
            JsonSerializer.Serialize(writer, new[] { 1, 2 });
            writer.WriteEndObject();
        }
        Assert.Equal(expected, Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Theory]
    [InlineData("  {\"Name\":\"a\",\"Value\":1}  ")]
    [InlineData("/*x*/[1]")]
    [InlineData("{\"Name\":\"a\"} trailing")]
    [InlineData("")]
    public void TopLevelReaderCallsMatch(string json)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(json);
        string expected, actual;
        var referenceReader = new S.Utf8JsonReader(utf8, new S.JsonReaderOptions { CommentHandling = S.JsonCommentHandling.Skip });
        try
        {
            expected = S.JsonSerializer.Serialize(S.JsonSerializer.Deserialize<object>(ref referenceReader)) + "@" + referenceReader.BytesConsumed + referenceReader.TokenType;
        }
        catch (Exception e)
        {
            expected = Name(e) + "@" + referenceReader.BytesConsumed + referenceReader.TokenType;
        }
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
        try
        {
            actual = JsonSerializer.Serialize(JsonSerializer.Deserialize<object>(ref reader)) + "@" + reader.BytesConsumed + reader.TokenType;
        }
        catch (Exception e)
        {
            actual = Name(e) + "@" + reader.BytesConsumed + reader.TokenType;
        }
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void MessagesForCommonFailuresMatch()
    {
        foreach (var (json, type) in new[]
        {
            ("{\"Int32\":\"x\"}", typeof(Primitives)),
            ("{\"Int32\":null}", typeof(Primitives)),
            ("{\"Children\":[{\"Value\":true}]}", typeof(TreeNode)),
            ("{\"Map\":{\"k\":\"v\"}}", typeof(Collections)),
            ("[1,2,\"x\"]", typeof(int[])),
            ("{\"Name\":1}", typeof(Child)),
            ("5", typeof(Child)),
            ("{\"Name\":\"a\"", typeof(Child)),
            ("{\"Mode\":\"Nope\"}", typeof(Primitives)),
            ("{\"Utc\":\"nope\"}", typeof(Primitives)),
        })
        {
            Assert.Equal(Try(() => Message(() => S.JsonSerializer.Deserialize(json, type))), Try(() => Message(() => JsonSerializer.Deserialize(json, type))));
        }
    }

    static string Message(Action action)
    {
        try
        {
            action();
            return "ok";
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }
}
