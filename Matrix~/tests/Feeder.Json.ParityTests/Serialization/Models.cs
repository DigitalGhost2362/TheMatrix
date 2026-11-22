using FS = Feeder.Json.Serialization;
using SS = System.Text.Json.Serialization;

namespace Feeder.Json.ParityTests.SerializerParity;

// every model carries both libraries' attributes, so one instance feeds both serializers

public enum Mode
{
    Off,
    On,
    Auto = 5,
}

[Flags]
public enum Rights
{
    None = 0,
    Read = 1,
    Write = 2,
    Exec = 4,
}

public enum Huge : ulong
{
    Small = 1,
    Max = ulong.MaxValue,
}

public class Primitives
{
    public bool Bool { get; set; }
    public byte Byte { get; set; }
    public sbyte SByte { get; set; }
    public short Int16 { get; set; }
    public ushort UInt16 { get; set; }
    public int Int32 { get; set; }
    public uint UInt32 { get; set; }
    public long Int64 { get; set; }
    public ulong UInt64 { get; set; }
    public float Single { get; set; }
    public double Double { get; set; }
    public decimal Decimal { get; set; }
    public char Char { get; set; }
    public string? String { get; set; }
    public DateTime Utc { get; set; }
    public DateTime Unspecified { get; set; }
    public DateTime Local { get; set; }
    public DateTimeOffset Offset { get; set; }
    public Guid Guid { get; set; }
    public TimeSpan Span { get; set; }
    public Uri? Uri { get; set; }
    public byte[]? Bytes { get; set; }
    public Mode Mode { get; set; }
    public Rights Rights { get; set; }
    public Huge Huge { get; set; }
    public int? NullableInt { get; set; }
    public double? NullableDouble { get; set; }
    public Mode? NullableMode { get; set; }
    public DateTime? NullableDate { get; set; }
    public Guid? NullableGuid { get; set; }

    public static Primitives Sample() => new()
    {
        Bool = true,
        Byte = 255,
        SByte = -128,
        Int16 = -32768,
        UInt16 = 65535,
        Int32 = int.MinValue,
        UInt32 = uint.MaxValue,
        Int64 = long.MinValue,
        UInt64 = ulong.MaxValue,
        Single = 3.4028235E+38f,
        Double = 0.1 + 0.2,
        Decimal = 79228162514264337593543950335m,
        Char = '\u00e9',
        String = "quote \" back \\ ctrl \u0001 tab \t <html> & 'x' + emoji \ud83d\ude00 sep \u2028 nul \0",
        Utc = new DateTime(2024, 2, 29, 23, 59, 59, DateTimeKind.Utc).AddTicks(1234500),
        Unspecified = new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
        Local = new DateTime(2026, 10, 6, 14, 10, 23, DateTimeKind.Local).AddTicks(5),
        Offset = new DateTimeOffset(2020, 5, 6, 7, 8, 9, TimeSpan.FromMinutes(-150)).AddTicks(10),
        Guid = new Guid("0A1B2C3D-4E5F-6071-8293-A4B5C6D7E8F9"),
        Span = new TimeSpan(-3, -4, -5, -6, -7).Add(TimeSpan.FromTicks(-8)),
        Uri = new Uri("../relative path?q=1#frag", UriKind.Relative),
        Bytes = new byte[] { 0, 1, 2, 250, 251, 252, 253, 254, 255 },
        Mode = Mode.Auto,
        Rights = Rights.Read | Rights.Exec,
        Huge = Huge.Max,
        NullableInt = 0,
        NullableDouble = null,
        NullableMode = (Mode)3,
        NullableDate = new DateTime(2000, 1, 1),
        NullableGuid = null,
    };
}

public class Child
{
    public string? Name { get; set; }
    public int Value { get; set; }
}

public class ChildList : List<Child>
{
}

public class Collections
{
    public int[]? Array { get; set; }
    public List<string?>? List { get; set; }
    public IList<int>? IList { get; set; }
    public IEnumerable<string>? Enumerable { get; set; }
    public IReadOnlyList<double>? ReadOnlyList { get; set; }
    public ICollection<long>? Collection { get; set; }
    public IReadOnlyCollection<bool>? ReadOnlyCollection { get; set; }
    public HashSet<string>? Set { get; set; }
    public ISet<int>? ISet { get; set; }
    public Dictionary<string, int>? Map { get; set; }
    public IDictionary<string, string?>? IMap { get; set; }
    public IReadOnlyDictionary<string, Child?>? ReadOnlyMap { get; set; }
    public Dictionary<string, List<int>>? Nested { get; set; }
    public List<Child>? Children { get; set; }
    public ChildList? Derived { get; set; }
    public int[][]? Jagged { get; set; }
    public List<int?>? Nullables { get; set; }
    public System.Collections.ObjectModel.Collection<string>? Custom { get; set; }
    public System.Collections.IEnumerable? Untyped { get; set; }

    public static Collections Sample() => new()
    {
        Array = new[] { 1, -2, 3 },
        List = new List<string?> { "a", null, "" },
        IList = new List<int> { 4 },
        Enumerable = new[] { "x", "y" },
        ReadOnlyList = new[] { 1.5, -0.0, 1e300 },
        Collection = new List<long> { long.MaxValue },
        ReadOnlyCollection = new[] { true, false },
        Set = new HashSet<string> { "s1", "s2" },
        ISet = new HashSet<int> { 9, 8 },
        Map = new Dictionary<string, int> { ["one"] = 1, ["two words"] = 2, ["\u00e9"] = 3 },
        IMap = new Dictionary<string, string?> { ["k"] = null },
        ReadOnlyMap = new Dictionary<string, Child?> { ["c"] = new Child { Name = "n", Value = 1 }, ["none"] = null },
        Nested = new Dictionary<string, List<int>> { ["l"] = new List<int> { 1, 2 } },
        Children = new List<Child> { new() { Name = "a" }, new() { Value = 2 } },
        Derived = new ChildList { new() { Name = "d" } },
        Jagged = new[] { new[] { 1 }, System.Array.Empty<int>() },
        Nullables = new List<int?> { 1, null },
        Custom = new System.Collections.ObjectModel.Collection<string> { "c" },
        Untyped = new System.Collections.ArrayList { 1, "two", null },
    };
}

public struct Point
{
    public int X { get; set; }
    public int Y { get; set; }
}

public class WithStruct
{
    public Point Location { get; set; }
    public Point? Maybe { get; set; }
    public List<Point>? Path { get; set; }
}

public class Feature
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

// mirrors the package's connection config: a virtual pair overridden and ignored, renamed replacements, ignored extras
public class BaseConfig
{
    public virtual string Host { get; set; } = "http://localhost:25000";
    public virtual int TimeoutMs { get; set; } = 60000;
    public virtual bool KeepConnected { get; set; } = true;
    public virtual string? Token { get; set; }
    public virtual string SkillsPath { get; set; } = "SKILLS";

    [FS.JsonIgnore, SS.JsonIgnore]
    public string? ProjectRootPath { get; set; }
}

public class DerivedConfig : BaseConfig
{
    [FS.JsonPropertyName("host"), SS.JsonPropertyName("host")]
    public string LocalHost { get; set; } = "http://localhost:1";

    [FS.JsonPropertyName("token"), SS.JsonPropertyName("token")]
    public string? LocalToken { get; set; }

    [FS.JsonIgnore, SS.JsonIgnore]
    public override string Host
    {
        get => LocalHost;
        set => LocalHost = value;
    }

    [FS.JsonIgnore, SS.JsonIgnore]
    public override string? Token
    {
        get => LocalToken;
        set => LocalToken = value;
    }

    public Mode Level { get; set; } = Mode.On;
    public List<Feature> Tools { get; set; } = new();
    public Dictionary<string, bool> Flags { get; set; } = new() { ["claude-code"] = true, ["other"] = false };

    [FS.JsonIgnore, SS.JsonIgnore]
    public List<string>? Override { get; set; }
}

// mirrors the reflector's serialized member: included public fields, a renamed nullable element, plus edge members
public class Member
{
    [FS.JsonInclude, SS.JsonInclude]
    public string? name = string.Empty;

    [FS.JsonInclude, SS.JsonInclude]
    public string typeName = string.Empty;

    [FS.JsonInclude, SS.JsonInclude]
    public List<Member>? fields;

    [FS.JsonInclude, SS.JsonInclude]
    [FS.JsonPropertyName("weight"), SS.JsonPropertyName("weight")]
    public double? weightValue;

    public int publicField = 11;

    public readonly int readonlyField = 7;

    [FS.JsonInclude, SS.JsonInclude]
    private int hidden = 3;

    [FS.JsonInclude, SS.JsonInclude]
    public int PrivateSetter { get; private set; } = 5;

    public int GetterOnly => 42 + hidden;

    public int SetterOnly
    {
        set => hidden = value;
    }

    [Obsolete("old")]
    public int Old { get; set; } = 9;

    [Obsolete("old")]
    public int oldField = 10;

    public static Member Sample() => new()
    {
        name = "root",
        typeName = "System.Object",
        weightValue = 0.5,
        fields = new List<Member> { new() { name = null, typeName = "System.Int32" }, new() { name = "n2", typeName = "T", fields = new List<Member>() } },
    };
}

public class TreeNode
{
    public string Name { get; set; } = "";
    public TreeNode? Parent { get; set; }
    public List<TreeNode> Children { get; set; } = new();
    public object? Tag { get; set; }
    public Dictionary<string, object?>? Map { get; set; }

    public static TreeNode Sample()
    {
        var root = new TreeNode { Name = "root" };
        var child = new TreeNode { Name = "child", Parent = root };
        root.Children.Add(child);
        root.Children.Add(root);
        child.Tag = root;
        child.Map = new Dictionary<string, object?> { ["up"] = root, ["self"] = child, ["text"] = "t" };
        root.Tag = new List<object?> { root, child, 1 };
        return root;
    }
}

public class Conflict
{
    public string? name;

    public string? Name { get; set; }
}

public class Annotated
{
    [FS.JsonPropertyName("display name"), SS.JsonPropertyName("display name")]
    public string Display { get; set; } = "d";

    [FS.JsonConverter(typeof(FS.JsonStringEnumConverter)), SS.JsonConverter(typeof(SS.JsonStringEnumConverter))]
    public Mode Mode { get; set; } = Mode.Auto;

    [FS.JsonConverter(typeof(FS.JsonStringEnumConverter)), SS.JsonConverter(typeof(SS.JsonStringEnumConverter))]
    public Rights? Rights { get; set; } = ParityTests.SerializerParity.Rights.Read | ParityTests.SerializerParity.Rights.Write;

    [FS.JsonConverter(typeof(FeederPointConverter)), SS.JsonConverter(typeof(ReferencePointConverter))]
    public Point Where { get; set; } = new() { X = 1, Y = 2 };
}

public interface IThing
{
    int A { get; }
}

public class Thing : IThing
{
    public int A => 1;
    public int B => 2;
}

public class HasInterface
{
    public IThing? Thing { get; set; } = new Thing();
    public object? Boxed { get; set; } = new Thing();
}

public abstract class AbstractThing
{
    public int A { get; set; }
}

public class ThrowingGetter
{
    public int Ok => 1;
    public int Bad => throw new InvalidOperationException("boom");
}

public class Wrapper
{
    public List<Child> Items { get; set; } = new();
    public string? Note { get; set; }
}

public class Logged
{
    public Mode LogType { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? StackTrace { get; set; }
}

public class Dto
{
    public string OperationId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public byte[]? ContentJsonUtf8 { get; set; }
    public string[] PendingOperations { get; set; } = Array.Empty<string>();
    public Mode EditorState { get; set; }
    public long DeadlineUnixMs { get; set; }
}
