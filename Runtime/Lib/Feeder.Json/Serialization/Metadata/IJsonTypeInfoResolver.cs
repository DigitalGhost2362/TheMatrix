#nullable enable
using System;

namespace Feeder.Json.Serialization.Metadata
{
    public interface IJsonTypeInfoResolver
    {
        JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options);
    }
}
