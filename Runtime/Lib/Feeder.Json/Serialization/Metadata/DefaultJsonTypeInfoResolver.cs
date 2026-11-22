#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Feeder.Json.Serialization.Metadata
{
    public class DefaultJsonTypeInfoResolver : IJsonTypeInfoResolver
    {
        internal static readonly DefaultJsonTypeInfoResolver Shared = new DefaultJsonTypeInfoResolver();

        public IList<Action<JsonTypeInfo>> Modifiers { get; } = new List<Action<JsonTypeInfo>>();

        public virtual JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            var info = new JsonTypeInfo(type, GetConverter(type, options), options);
            if (info.Kind == JsonTypeInfoKind.Object)
                info.PopulateProperties();
            foreach (Action<JsonTypeInfo> modifier in Modifiers)
                modifier(info);
            return info;
        }

        // options list first, then [JsonConverter] on the type, then the built-in converter
        internal static JsonConverter GetConverter(Type type, JsonSerializerOptions options)
        {
            foreach (JsonConverter converter in options.Converters)
            {
                if (converter.CanConvert(type))
                    return CheckCompatible(converter, type);
            }
            JsonConverterAttribute? attribute = type.GetCustomAttribute<JsonConverterAttribute>(false);
            return attribute != null ? FromAttribute(attribute, type, type) : BuiltInConverters.For(type);
        }

        internal static JsonConverter FromAttribute(JsonConverterAttribute attribute, Type type, MemberInfo owner)
        {
            Type? converterType = attribute.ConverterType;
            if (converterType == null || !typeof(JsonConverter).IsAssignableFrom(converterType) || converterType.GetConstructor(Type.EmptyTypes) == null)
                throw new InvalidOperationException("The converter specified on '" + owner + "' does not derive from JsonConverter or does not have a public parameterless constructor.");
            var converter = (JsonConverter)Activator.CreateInstance(converterType)!;
            if (converter.CanConvert(type))
                return CheckCompatible(converter, type);
            Type? underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null && converter.CanConvert(underlying))
                return new NullableConverter(underlying, converter);
            throw new InvalidOperationException("The converter specified on '" + owner + "' is not compatible with the type '" + type + "'.");
        }

        static JsonConverter CheckCompatible(JsonConverter converter, Type type)
        {
            Type? declared = converter.TypeToConvert;
            if (declared != null && !declared.IsAssignableFrom(type) && !type.IsAssignableFrom(declared))
                throw new InvalidOperationException("The converter '" + converter.GetType() + "' is not compatible with the type '" + type + "'.");
            return converter;
        }
    }
}
