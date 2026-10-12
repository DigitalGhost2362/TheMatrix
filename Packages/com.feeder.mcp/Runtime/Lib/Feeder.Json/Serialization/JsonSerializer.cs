#nullable enable
using System;
using System.IO;
using Feeder.Json.Nodes;
using Feeder.Json.Serialization.Metadata;

namespace Feeder.Json
{
    public static partial class JsonSerializer
    {
        public static string Serialize<TValue>(TValue value, JsonSerializerOptions? options = null) =>
            Utf8ToString(WriteToStream(value, GetTypeInfo(typeof(TValue), options)));

        public static string Serialize(object? value, Type inputType, JsonSerializerOptions? options = null) =>
            Utf8ToString(WriteToStream(value, GetTypeInfo(value, inputType, options)));

        public static void Serialize<TValue>(Stream utf8Json, TValue value, JsonSerializerOptions? options = null)
        {
            if (utf8Json == null)
                throw new ArgumentNullException(nameof(utf8Json));
            MemoryStream output = WriteToStream(value, GetTypeInfo(typeof(TValue), options));
            utf8Json.Write(output.GetBuffer(), 0, (int)output.Length);
        }

        public static void Serialize<TValue>(Utf8JsonWriter writer, TValue value, JsonSerializerOptions? options = null)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            WriteRoot(writer, value, GetTypeInfo(typeof(TValue), options));
            writer.Flush();
        }

        public static byte[] SerializeToUtf8Bytes<TValue>(TValue value, JsonSerializerOptions? options = null) =>
            WriteToStream(value, GetTypeInfo(typeof(TValue), options)).ToArray();

        public static byte[] SerializeToUtf8Bytes(object? value, Type inputType, JsonSerializerOptions? options = null) =>
            WriteToStream(value, GetTypeInfo(value, inputType, options)).ToArray();

        public static JsonDocument SerializeToDocument<TValue>(TValue value, JsonSerializerOptions? options = null) =>
            ToDocument(value, GetTypeInfo(typeof(TValue), options));

        public static JsonDocument SerializeToDocument(object? value, Type inputType, JsonSerializerOptions? options = null) =>
            ToDocument(value, GetTypeInfo(value, inputType, options));

        public static JsonElement SerializeToElement<TValue>(TValue value, JsonSerializerOptions? options = null)
        {
            JsonTypeInfo info = GetTypeInfo(typeof(TValue), options);
            return JsonDocument.ParseElement(WriteToStream(value, info).ToArray(), info.Options.GetDocumentOptions());
        }

        public static JsonNode? SerializeToNode<TValue>(TValue value, JsonSerializerOptions? options = null) =>
            ToNode(value, GetTypeInfo(typeof(TValue), options));

        public static JsonNode? SerializeToNode(object? value, Type inputType, JsonSerializerOptions? options = null) =>
            ToNode(value, GetTypeInfo(value, inputType, options));

        public static TValue? Deserialize<TValue>(string json, JsonSerializerOptions? options = null) =>
            (TValue?)Deserialize(json, typeof(TValue), options);

        public static object? Deserialize(string json, Type returnType, JsonSerializerOptions? options = null)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));
            JsonTypeInfo info = GetTypeInfo(returnType, options);
            var utf8 = new byte[JsonText.Utf8Length(json.AsSpan())];
            JsonText.ToUtf8(json.AsSpan(), utf8);
            return ReadRoot(utf8, info);
        }

        public static TValue? Deserialize<TValue>(ReadOnlySpan<byte> utf8Json, JsonSerializerOptions? options = null) =>
            (TValue?)ReadRoot(utf8Json, GetTypeInfo(typeof(TValue), options));

        public static object? Deserialize(ReadOnlySpan<byte> utf8Json, Type returnType, JsonSerializerOptions? options = null) =>
            ReadRoot(utf8Json, GetTypeInfo(returnType, options));

        public static TValue? Deserialize<TValue>(ref Utf8JsonReader reader, JsonSerializerOptions? options = null) =>
            (TValue?)ReadNested(ref reader, GetTypeInfo(typeof(TValue), options));

        public static object? Deserialize(ref Utf8JsonReader reader, Type returnType, JsonSerializerOptions? options = null) =>
            ReadNested(ref reader, GetTypeInfo(returnType, options));

        public static TValue? Deserialize<TValue>(this JsonElement element, JsonSerializerOptions? options = null) =>
            (TValue?)Deserialize(element, typeof(TValue), options);

        public static object? Deserialize(this JsonElement element, Type returnType, JsonSerializerOptions? options = null)
        {
            JsonTypeInfo info = GetTypeInfo(returnType, options);
            return ReadRoot(element.GetRawValueSpan(), info);
        }

        public static TValue? Deserialize<TValue>(this JsonDocument document, JsonSerializerOptions? options = null) =>
            (TValue?)Deserialize(document, typeof(TValue), options);

        public static object? Deserialize(this JsonDocument document, Type returnType, JsonSerializerOptions? options = null)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            return Deserialize(document.RootElement, returnType, options);
        }

        public static TValue? Deserialize<TValue>(this JsonNode? node, JsonSerializerOptions? options = null) =>
            (TValue?)Deserialize(node, typeof(TValue), options);

        public static object? Deserialize(this JsonNode? node, Type returnType, JsonSerializerOptions? options = null)
        {
            JsonTypeInfo info = GetTypeInfo(returnType, options);
            var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output, info.Options.GetWriterOptions()))
            {
                if (node == null)
                    writer.WriteNullValue();
                else
                    node.WriteTo(writer, info.Options);
            }
            return ReadRoot(new ReadOnlySpan<byte>(output.GetBuffer(), 0, (int)output.Length), info);
        }

        static JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions? options)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            return (options ?? JsonSerializerOptions.Default).GetTypeInfo(type);
        }

        static JsonTypeInfo GetTypeInfo(object? value, Type inputType, JsonSerializerOptions? options)
        {
            if (inputType == null)
                throw new ArgumentNullException(nameof(inputType));
            if (value != null && !inputType.IsInstanceOfType(value))
                throw new ArgumentException("The supplied object type '" + value.GetType() + "' is not compatible with the specified type '" + inputType + "'.", nameof(inputType));
            JsonTypeInfo info = GetTypeInfo(inputType, options);
            if (value == null && info.IsNonNullableValueType)
                throw new JsonException("The JSON value could not be converted to " + inputType + ".");
            return info;
        }

        static JsonDocument ToDocument(object? value, JsonTypeInfo info) =>
            JsonDocument.Parse(WriteToStream(value, info).ToArray(), info.Options.GetDocumentOptions());

        static JsonNode? ToNode(object? value, JsonTypeInfo info) =>
            JsonNode.FromElement(JsonDocument.ParseElement(WriteToStream(value, info).ToArray(), info.Options.GetDocumentOptions()), info.Options.GetNodeOptions());
    }
}
