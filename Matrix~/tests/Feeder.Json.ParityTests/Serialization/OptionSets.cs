using Feeder.Json.Serialization;
using Feeder.Json.Serialization.Metadata;
using S = System.Text.Json;
using SS = System.Text.Json.Serialization;
using SM = System.Text.Json.Serialization.Metadata;
using SW = System.Text.Encodings.Web;

namespace Feeder.Json.ParityTests.SerializerParity;

// the option sets the package builds, each created fresh per test for both libraries
internal sealed class OptionSet
{
    OptionSet(string name, Func<JsonSerializerOptions?> feeder, Func<S.JsonSerializerOptions?> reference)
    {
        Name = name;
        Feeder = feeder;
        Reference = reference;
    }

    public string Name { get; }

    public Func<JsonSerializerOptions?> Feeder { get; }

    public Func<S.JsonSerializerOptions?> Reference { get; }

    public override string ToString() => Name;

    public static readonly OptionSet[] All =
    {
        new("default", () => null, () => null),
        new("reflector", Reflector, ReflectorReference),
        new("skill-pretty",
            () => new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, TypeInfoResolver = new DefaultJsonTypeInfoResolver() },
            () => new S.JsonSerializerOptions { WriteIndented = true, Encoder = SW.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, TypeInfoResolver = new SM.DefaultJsonTypeInfoResolver() }),
        new("common-pretty",
            () => new JsonSerializerOptions
            {
                PropertyNamingPolicy = null,
                PropertyNameCaseInsensitive = true,
                WriteIndented = true,
                AllowTrailingCommas = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                ReadCommentHandling = JsonCommentHandling.Skip,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
                Converters = { new JsonStringEnumConverter() },
            },
            () => new S.JsonSerializerOptions
            {
                PropertyNamingPolicy = null,
                PropertyNameCaseInsensitive = true,
                WriteIndented = true,
                AllowTrailingCommas = false,
                DefaultIgnoreCondition = SS.JsonIgnoreCondition.WhenWritingNull,
                ReadCommentHandling = S.JsonCommentHandling.Skip,
                NumberHandling = SS.JsonNumberHandling.AllowReadingFromString,
                Converters = { new SS.JsonStringEnumConverter() },
            }),
        new("agent-config", () => new JsonSerializerOptions { WriteIndented = true }, () => new S.JsonSerializerOptions { WriteIndented = true }),
        new("file-log",
            () => new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull },
            () => new S.JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = false, DefaultIgnoreCondition = SS.JsonIgnoreCondition.WhenWritingNull }),
        new("schema",
            () => new JsonSerializerOptions { WriteIndented = false, TypeInfoResolver = new DefaultJsonTypeInfoResolver() },
            () => new S.JsonSerializerOptions { WriteIndented = false, TypeInfoResolver = new SM.DefaultJsonTypeInfoResolver() }),
        new("config-read",
            () => new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } },
            () => new S.JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new SS.JsonStringEnumConverter() } }),
        new("config-write",
            () => new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } },
            () => new S.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = S.JsonNamingPolicy.CamelCase, Converters = { new SS.JsonStringEnumConverter() } }),
        new("hub",
            () => new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping },
            () => new S.JsonSerializerOptions { PropertyNamingPolicy = S.JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, Encoder = SW.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
    };

    public static OptionSet Get(string name) => All.First(o => o.Name == name);

    static JsonSerializerOptions Reflector() => new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
        WriteIndented = true,
        TypeInfoResolver = JsonTypeInfoResolver.Combine(new DefaultJsonTypeInfoResolver { Modifiers = { ObsoleteFilter.Feeder } }),
        Converters =
        {
            new FeederIntAsObject(),
            new FeederDoubleAsObject(),
            new FeederEnumAsObject(),
            new FeederElementConverter(),
            new FeederNodeConverter(),
            new FeederWrapperConverter(),
            new FeederPointConverter(),
        },
    };

    static S.JsonSerializerOptions ReflectorReference() => new()
    {
        DefaultIgnoreCondition = SS.JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = SS.ReferenceHandler.IgnoreCycles,
        NumberHandling = SS.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
        WriteIndented = true,
        TypeInfoResolver = SM.JsonTypeInfoResolver.Combine(new SM.DefaultJsonTypeInfoResolver { Modifiers = { ObsoleteFilter.Reference } }),
        Converters =
        {
            new ReferenceIntAsObject(),
            new ReferenceDoubleAsObject(),
            new ReferenceEnumAsObject(),
            new ReferenceElementConverter(),
            new ReferenceNodeConverter(),
            new ReferenceWrapperConverter(),
            new ReferencePointConverter(),
        },
    };
}
