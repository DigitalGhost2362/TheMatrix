#nullable enable
using System;
using System.IO;

namespace Feeder.Json.Nodes
{
    public abstract partial class JsonNode
    {
        JsonNode? parent;

        private protected JsonNode(JsonNodeOptions? options)
        {
            Options = options;
        }

        internal JsonNodeOptions? Options { get; }

        public JsonNode? this[int index]
        {
            get => AsArray().GetItem(index);
            set => AsArray().SetItem(index, value);
        }

        public JsonNode? this[string propertyName]
        {
            get => AsObject().GetItem(propertyName);
            set => AsObject().SetItem(propertyName, value);
        }

        public JsonArray AsArray() => this as JsonArray ?? throw WrongType(nameof(JsonArray));

        public JsonObject AsObject() => this as JsonObject ?? throw WrongType(nameof(JsonObject));

        public virtual T GetValue<T>() => throw WrongType(nameof(JsonValue));

        public JsonValueKind GetValueKind() => GetValueKindCore();

        public JsonNode DeepClone() => DeepCloneCore();

        public abstract void WriteTo(Utf8JsonWriter writer, JsonSerializerOptions? options = null);

        public string ToJsonString(JsonSerializerOptions? options = null)
        {
            var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output, options?.GetWriterOptions() ?? default))
                WriteTo(writer, options);
            return JsonSerializer.Utf8ToString(output);
        }

        // a string value prints unquoted; everything else prints as indented JSON
        public override string ToString()
        {
            if (this is JsonValue value && value.TryGetString(out string? text))
                return text!;
            var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true }))
                WriteTo(writer);
            return JsonSerializer.Utf8ToString(output);
        }

        public static JsonNode? Parse(string json, JsonNodeOptions? nodeOptions = null, JsonDocumentOptions documentOptions = default)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));
            return FromElement(JsonDocument.ParseElement(json, documentOptions), nodeOptions);
        }

        public static JsonNode? Parse(ref Utf8JsonReader reader, JsonNodeOptions? nodeOptions = null) =>
            FromElement(JsonSerializer.ParseElement(ref reader), nodeOptions);

        private protected abstract JsonValueKind GetValueKindCore();

        internal abstract JsonNode DeepCloneCore();

        // objects and arrays stay backed by the element until first touched
        internal static JsonNode? FromElement(JsonElement element, JsonNodeOptions? options)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null:
                    return null;
                case JsonValueKind.Object:
                    return new JsonObject(element, options);
                case JsonValueKind.Array:
                    return new JsonArray(element, options);
                default:
                    return new JsonValueOfElement(element, options);
            }
        }

        internal void AssignParent(JsonNode newParent)
        {
            if (parent != null)
                throw new InvalidOperationException("The node already has a parent.");
            for (JsonNode? current = newParent; current != null; current = current.parent)
            {
                if (current == this)
                    throw new InvalidOperationException("A node cycle was detected.");
            }
            parent = newParent;
        }

        internal void DetachParent() => parent = null;

        static InvalidOperationException WrongType(string typeName) => new InvalidOperationException("The node must be of type '" + typeName + "'.");
    }
}
