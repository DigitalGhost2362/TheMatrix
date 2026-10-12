using System;

namespace Feeder.Json.Serialization
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class JsonIncludeAttribute : Attribute
    {
    }
}
