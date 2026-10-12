#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Feeder.Json.Nodes;

namespace Feeder.Json.Serialization
{
    // built-in converter selection, in the reference order: simple types, unsupported, nullable, enum, nodes, collections, objects
    internal static class BuiltInConverters
    {
        static readonly Dictionary<Type, PrimitiveConverter> simple = new Dictionary<Type, PrimitiveConverter>
        {
            [typeof(bool)] = new PrimitiveConverter(typeof(bool), PrimitiveCode.Boolean),
            [typeof(byte)] = new PrimitiveConverter(typeof(byte), PrimitiveCode.Byte),
            [typeof(sbyte)] = new PrimitiveConverter(typeof(sbyte), PrimitiveCode.SByte),
            [typeof(short)] = new PrimitiveConverter(typeof(short), PrimitiveCode.Int16),
            [typeof(ushort)] = new PrimitiveConverter(typeof(ushort), PrimitiveCode.UInt16),
            [typeof(int)] = new PrimitiveConverter(typeof(int), PrimitiveCode.Int32),
            [typeof(uint)] = new PrimitiveConverter(typeof(uint), PrimitiveCode.UInt32),
            [typeof(long)] = new PrimitiveConverter(typeof(long), PrimitiveCode.Int64),
            [typeof(ulong)] = new PrimitiveConverter(typeof(ulong), PrimitiveCode.UInt64),
            [typeof(float)] = new PrimitiveConverter(typeof(float), PrimitiveCode.Single),
            [typeof(double)] = new PrimitiveConverter(typeof(double), PrimitiveCode.Double),
            [typeof(decimal)] = new PrimitiveConverter(typeof(decimal), PrimitiveCode.Decimal),
            [typeof(char)] = new PrimitiveConverter(typeof(char), PrimitiveCode.Char),
            [typeof(string)] = new PrimitiveConverter(typeof(string), PrimitiveCode.String),
            [typeof(DateTime)] = new PrimitiveConverter(typeof(DateTime), PrimitiveCode.DateTime),
            [typeof(DateTimeOffset)] = new PrimitiveConverter(typeof(DateTimeOffset), PrimitiveCode.DateTimeOffset),
            [typeof(Guid)] = new PrimitiveConverter(typeof(Guid), PrimitiveCode.Guid),
            [typeof(TimeSpan)] = new PrimitiveConverter(typeof(TimeSpan), PrimitiveCode.TimeSpan),
            [typeof(Uri)] = new PrimitiveConverter(typeof(Uri), PrimitiveCode.Uri),
            [typeof(byte[])] = new PrimitiveConverter(typeof(byte[]), PrimitiveCode.Bytes),
            [typeof(JsonElement)] = new PrimitiveConverter(typeof(JsonElement), PrimitiveCode.Element),
            [typeof(JsonDocument)] = new PrimitiveConverter(typeof(JsonDocument), PrimitiveCode.Document),
            [typeof(object)] = new PrimitiveConverter(typeof(object), PrimitiveCode.Object),
        };

        internal static JsonConverter For(Type type)
        {
            if (simple.TryGetValue(type, out PrimitiveConverter? converter))
                return converter;
            if (typeof(MemberInfo).IsAssignableFrom(type) || typeof(Delegate).IsAssignableFrom(type) || type == typeof(IntPtr) || type == typeof(UIntPtr)
                || (type.IsArray && type.GetArrayRank() > 1))
                return new PrimitiveConverter(type, PrimitiveCode.Unsupported);
            Type? underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
                return new NullableConverter(underlying, null);
            if (type.IsEnum)
                return new PrimitiveConverter(type, PrimitiveCode.Enum);
            if (typeof(JsonNode).IsAssignableFrom(type))
                return new PrimitiveConverter(type, PrimitiveCode.Node);
            if (!typeof(IEnumerable).IsAssignableFrom(type))
                return new ObjectConverter(type);
            Type? map = FindGeneric(type, typeof(IDictionary<,>)) ?? FindGeneric(type, typeof(IReadOnlyDictionary<,>));
            if (map != null)
                return new DictionaryConverter(type, map.GetGenericArguments());
            Type element = type.IsArray ? type.GetElementType()! : FindGeneric(type, typeof(IEnumerable<>))?.GetGenericArguments()[0] ?? typeof(object);
            return new EnumerableConverter(type, element);
        }

        internal static Type? FindGeneric(Type type, Type definition)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == definition)
                return type;
            foreach (Type candidate in type.GetInterfaces())
            {
                if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == definition)
                    return candidate;
            }
            return null;
        }
    }

    internal sealed class NullableConverter : JsonConverter
    {
        readonly Type underlying;

        // element is set when a [JsonConverter] for the underlying type has to be wrapped
        internal NullableConverter(Type underlying, JsonConverter? element)
        {
            this.underlying = underlying;
            Element = element;
        }

        internal JsonConverter? Element { get; }

        internal override Type ElementType => underlying;

        internal override ConverterStrategy Strategy => ConverterStrategy.Nullable;

        public override bool CanConvert(Type typeToConvert) => Nullable.GetUnderlyingType(typeToConvert) == underlying;

        internal override object? ReadAsObject(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            JsonSerializer.ReadWithFreshState(ref reader, typeToConvert, options);

        internal override void WriteAsObject(Utf8JsonWriter writer, object value, JsonSerializerOptions options) =>
            JsonSerializer.WriteWithFreshState(writer, value, value.GetType(), options);
    }

    internal sealed class ObjectConverter : JsonConverter
    {
        readonly Type type;

        internal ObjectConverter(Type type)
        {
            this.type = type;
        }

        internal override ConverterStrategy Strategy => ConverterStrategy.Object;

        public override bool CanConvert(Type typeToConvert) => typeToConvert == type;

        internal override object? ReadAsObject(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            JsonSerializer.ReadWithFreshState(ref reader, type, options);

        internal override void WriteAsObject(Utf8JsonWriter writer, object value, JsonSerializerOptions options) =>
            JsonSerializer.WriteWithFreshState(writer, value, type, options);
    }

    internal enum CollectionShape : byte
    {
        Array,
        List,
        Set,
        AddToList,
        AddToCollection,
        Unsupported,
    }

    internal sealed class EnumerableConverter : JsonConverter
    {
        readonly Type type;
        readonly Type element;
        readonly Type setType;
        readonly MethodInfo? add;

        internal EnumerableConverter(Type type, Type element)
        {
            this.type = type;
            this.element = element;
            ListType = typeof(List<>).MakeGenericType(element);
            setType = typeof(HashSet<>).MakeGenericType(element);
            if (type.IsArray)
                Shape = CollectionShape.Array;
            else if (type.IsAssignableFrom(ListType))
                Shape = CollectionShape.List;
            else if (type.IsAssignableFrom(setType))
                Shape = CollectionShape.Set;
            else if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null)
                Shape = CollectionShape.Unsupported;
            else if (typeof(IList).IsAssignableFrom(type))
                Shape = CollectionShape.AddToList;
            else
            {
                add = FindGenericAdd(type, element);
                Shape = add != null ? CollectionShape.AddToCollection : CollectionShape.Unsupported;
            }
        }

        internal CollectionShape Shape { get; }

        internal Type ListType { get; }

        internal override Type ElementType => element;

        internal override ConverterStrategy Strategy => ConverterStrategy.Enumerable;

        public override bool CanConvert(Type typeToConvert) => typeToConvert == type;

        internal override object? ReadAsObject(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            JsonSerializer.ReadWithFreshState(ref reader, type, options);

        internal override void WriteAsObject(Utf8JsonWriter writer, object value, JsonSerializerOptions options) =>
            JsonSerializer.WriteWithFreshState(writer, value, type, options);

        internal object Complete(IList items)
        {
            switch (Shape)
            {
                case CollectionShape.Array:
                    Array array = Array.CreateInstance(element, items.Count);
                    items.CopyTo(array, 0);
                    return array;
                case CollectionShape.List:
                    return items;
                case CollectionShape.Set:
                    return Activator.CreateInstance(setType, items)!;
                case CollectionShape.AddToList:
                    var list = (IList)Activator.CreateInstance(type)!;
                    foreach (object? item in items)
                        list.Add(item);
                    return list;
                default:
                    object collection = Activator.CreateInstance(type)!;
                    var arguments = new object?[1];
                    foreach (object? item in items)
                    {
                        arguments[0] = item;
                        add!.Invoke(collection, arguments);
                    }
                    return collection;
            }
        }

        static MethodInfo? FindGenericAdd(Type type, Type element)
        {
            Type collection = typeof(ICollection<>).MakeGenericType(element);
            return collection.IsAssignableFrom(type) ? collection.GetMethod("Add") : null;
        }
    }

    internal sealed class DictionaryConverter : JsonConverter
    {
        readonly Type type;
        readonly Type key;
        readonly Type value;
        readonly PropertyInfo pairKey;
        readonly PropertyInfo pairValue;
        readonly PropertyInfo? indexer;

        internal DictionaryConverter(Type type, Type[] arguments)
        {
            this.type = type;
            key = arguments[0];
            value = arguments[1];
            Type pair = typeof(KeyValuePair<,>).MakeGenericType(arguments);
            pairKey = pair.GetProperty("Key")!;
            pairValue = pair.GetProperty("Value")!;
            Type concrete = typeof(Dictionary<,>).MakeGenericType(arguments);
            CreateType = type.IsAbstract || type.IsInterface
                ? (type.IsAssignableFrom(concrete) ? concrete : null)
                : (type.GetConstructor(Type.EmptyTypes) != null ? type : null);
            if (CreateType != null && !typeof(IDictionary).IsAssignableFrom(CreateType))
                indexer = typeof(IDictionary<,>).MakeGenericType(arguments).GetProperty("Item");
        }

        internal Type? CreateType { get; }

        internal bool HasStringKeys => key == typeof(string);

        internal override Type ElementType => value;

        internal override ConverterStrategy Strategy => ConverterStrategy.Dictionary;

        public override bool CanConvert(Type typeToConvert) => typeToConvert == type;

        internal override object? ReadAsObject(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            JsonSerializer.ReadWithFreshState(ref reader, type, options);

        internal override void WriteAsObject(Utf8JsonWriter writer, object value, JsonSerializerOptions options) =>
            JsonSerializer.WriteWithFreshState(writer, value, type, options);

        internal NotSupportedException UnsupportedKey() =>
            new NotSupportedException("The type '" + key + "' is not a supported dictionary key type.");

        internal void Set(object dictionary, string name, object? item)
        {
            if (indexer == null)
                ((IDictionary)dictionary)[name] = item;
            else
                indexer.SetValue(dictionary, item, new object[] { name });
        }

        internal IEnumerable<KeyValuePair<string, object?>> Entries(object dictionary)
        {
            if (dictionary is IDictionary map)
            {
                foreach (DictionaryEntry entry in map)
                    yield return new KeyValuePair<string, object?>((string)entry.Key, entry.Value);
                yield break;
            }
            foreach (object? pair in (IEnumerable)dictionary)
                yield return new KeyValuePair<string, object?>((string)pairKey.GetValue(pair)!, pairValue.GetValue(pair));
        }
    }
}
