#nullable enable
using System;

namespace Feeder.Json.Serialization
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Interface, AllowMultiple = false)]
    public class JsonConverterAttribute : Attribute
    {
        public JsonConverterAttribute(Type converterType)
        {
            ConverterType = converterType;
        }

        public Type? ConverterType { get; }
    }
}
