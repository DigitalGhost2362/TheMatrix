#nullable enable
using System;

namespace Feeder.Json.Serialization
{
    public abstract class JsonConverter<T> : JsonConverter
    {
        protected internal JsonConverter()
        {
        }

        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(T);

        public abstract T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options);

        public abstract void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options);

        internal sealed override Type? TypeToConvert => typeof(T);

        // a non-nullable struct converter also sees null tokens, like the reference default
        internal sealed override bool HandleNullOnRead => typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null;

        internal sealed override bool IsUserConverter => true;

        internal sealed override object? ReadAsObject(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Read(ref reader, typeToConvert, options);

        internal sealed override void WriteAsObject(Utf8JsonWriter writer, object value, JsonSerializerOptions options) =>
            Write(writer, (T)value, options);
    }
}
