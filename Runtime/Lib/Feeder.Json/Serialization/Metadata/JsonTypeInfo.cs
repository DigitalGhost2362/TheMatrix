#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Feeder.Json.Serialization.Metadata
{
    public sealed class JsonTypeInfo
    {
        const BindingFlags DeclaredInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        readonly List<JsonPropertyInfo> properties = new List<JsonPropertyInfo>();
        JsonTypeInfo? element;
        JsonPropertyInfo[] writeOrder = Array.Empty<JsonPropertyInfo>();
        Dictionary<string, JsonPropertyInfo>? readLookup;

        internal JsonTypeInfo(Type type, JsonConverter converter, JsonSerializerOptions options)
        {
            Type = type;
            Converter = converter;
            Options = options;
            Strategy = converter.Strategy;
            IsNonNullableValueType = type.IsValueType && Nullable.GetUnderlyingType(type) == null;
            switch (Strategy)
            {
                case ConverterStrategy.Object:
                    Kind = JsonTypeInfoKind.Object;
                    break;
                case ConverterStrategy.Enumerable:
                    Kind = JsonTypeInfoKind.Enumerable;
                    break;
                case ConverterStrategy.Dictionary:
                    Kind = JsonTypeInfoKind.Dictionary;
                    break;
            }
        }

        public JsonTypeInfoKind Kind { get; }

        public IList<JsonPropertyInfo> Properties => properties;

        internal Type Type { get; }

        internal JsonConverter Converter { get; }

        internal JsonSerializerOptions Options { get; }

        internal ConverterStrategy Strategy { get; }

        internal bool IsNonNullableValueType { get; }

        internal JsonPropertyInfo[] WriteOrder => writeOrder;

        // collection element, dictionary value or nullable underlying type
        internal JsonTypeInfo ElementInfo
        {
            get
            {
                if (element == null)
                {
                    JsonConverter? custom = (Converter as NullableConverter)?.Element;
                    element = custom != null ? new JsonTypeInfo(Converter.ElementType!, custom, Options) : Options.GetTypeInfo(Converter.ElementType!);
                }
                return element;
            }
        }

        internal bool TryGetProperty(string name, out JsonPropertyInfo? property)
        {
            property = null;
            return readLookup != null && readLookup.TryGetValue(name, out property);
        }

        // runs once the resolver modifiers are done; the lookups are immutable afterwards
        internal void Configure()
        {
            if (Kind != JsonTypeInfoKind.Object)
                return;
            var write = new List<JsonPropertyInfo>(properties.Count);
            var read = new Dictionary<string, JsonPropertyInfo>(Options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (JsonPropertyInfo property in properties)
            {
                if (property.CanRead)
                    write.Add(property);
                if (property.CanWrite)
                    read[property.Name] = property;
            }
            writeOrder = write.ToArray();
            readLookup = read;
        }

        internal object CreateObject()
        {
            if (Type.IsValueType)
                return Activator.CreateInstance(Type)!;
            ConstructorInfo? constructor = Type.IsAbstract || Type.IsInterface ? null : Type.GetConstructor(Type.EmptyTypes);
            if (constructor == null)
                throw new NotSupportedException("Deserialization of interface, abstract or types without a public parameterless constructor is not supported. Type '" + Type + "'.");
            try
            {
                return constructor.Invoke(null);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

        // derived type first, its properties then its fields, like the reference resolver
        internal void PopulateProperties()
        {
            var added = new Dictionary<string, JsonPropertyInfo>(Options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            Dictionary<string, JsonPropertyInfo>? ignored = null;
            foreach (Type current in Hierarchy(Type))
            {
                foreach (PropertyInfo property in current.GetProperties(DeclaredInstance))
                {
                    if (property.GetIndexParameters().Length > 0 || IsOverriddenAndIgnored(property, ignored))
                        continue;
                    bool include = property.GetCustomAttribute<JsonIncludeAttribute>(false) != null;
                    if (property.GetMethod?.IsPublic == true || property.SetMethod?.IsPublic == true || include)
                        Add(new JsonPropertyInfo(property, property.PropertyType, include, Options), added, ref ignored);
                }
                foreach (FieldInfo field in current.GetFields(DeclaredInstance))
                {
                    bool include = field.GetCustomAttribute<JsonIncludeAttribute>(false) != null;
                    if (include || (field.IsPublic && Options.IncludeFields))
                        Add(new JsonPropertyInfo(field, field.FieldType, include, Options), added, ref ignored);
                }
            }
        }

        void Add(JsonPropertyInfo property, Dictionary<string, JsonPropertyInfo> added, ref Dictionary<string, JsonPropertyInfo>? ignored)
        {
            if (!added.TryGetValue(property.Name, out JsonPropertyInfo? other))
            {
                added.Add(property.Name, property);
                properties.Add(property);
            }
            else if (other.IsIgnored)
            {
                added[property.Name] = property;
                properties[properties.IndexOf(other)] = property;
            }
            else if (!property.IsIgnored && !property.IsHiddenBy(other)
                && !(ignored != null && ignored.TryGetValue(property.MemberName, out JsonPropertyInfo? hidden) && property.IsHiddenBy(hidden)))
            {
                throw new InvalidOperationException("The JSON property name for '" + Type + "." + property.Name + "' collides with another property.");
            }
            if (property.IsIgnored)
                (ignored ??= new Dictionary<string, JsonPropertyInfo>())[property.MemberName] = property;
        }

        static bool IsOverriddenAndIgnored(PropertyInfo property, Dictionary<string, JsonPropertyInfo>? ignored) =>
            ignored != null
            && (property.GetMethod?.IsVirtual == true || property.SetMethod?.IsVirtual == true)
            && ignored.TryGetValue(property.Name, out JsonPropertyInfo? member)
            && member.IsVirtual
            && member.PropertyType == property.PropertyType;

        static IEnumerable<Type> Hierarchy(Type type)
        {
            if (type.IsInterface)
            {
                yield return type;
                foreach (Type parent in type.GetInterfaces())
                    yield return parent;
                yield break;
            }
            for (Type? current = type; current != null && current != typeof(object) && current != typeof(ValueType); current = current.BaseType)
                yield return current;
        }
    }
}
