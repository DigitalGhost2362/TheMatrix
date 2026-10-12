#nullable enable
using System;

namespace Feeder.Json.Serialization
{
    public abstract class JsonConverter
    {
        internal JsonConverter()
        {
        }

        public abstract bool CanConvert(Type typeToConvert);

        // the converter's T; null when one instance serves several types
        internal virtual Type? TypeToConvert => null;

        internal virtual Type? ElementType => null;

        internal virtual ConverterStrategy Strategy => ConverterStrategy.Value;

        internal virtual bool HandleNullOnRead => false;

        // user converters get the reference serializer's read/write consistency checks
        internal virtual bool IsUserConverter => false;

        internal abstract object? ReadAsObject(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options);

        internal abstract void WriteAsObject(Utf8JsonWriter writer, object value, JsonSerializerOptions options);
    }

    internal enum ConverterStrategy : byte
    {
        Value,
        Object,
        Enumerable,
        Dictionary,
        Nullable,
        Polymorphic,
    }
}
