#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;

namespace Feeder.Json.Nodes
{
    public sealed class JsonArray : JsonNode, IList<JsonNode?>
    {
        JsonElement? element;
        List<JsonNode?>? items;

        public JsonArray(JsonNodeOptions? options = null) : base(options)
        {
        }

        public JsonArray(JsonNodeOptions options, params JsonNode?[] items) : base(options)
        {
            Initialize(items);
        }

        public JsonArray(params JsonNode?[] items) : base(null)
        {
            Initialize(items);
        }

        internal JsonArray(JsonElement element, JsonNodeOptions? options) : base(options)
        {
            this.element = element;
        }

        public int Count => Items.Count;

        bool ICollection<JsonNode?>.IsReadOnly => false;

        List<JsonNode?> Items
        {
            get
            {
                if (items == null)
                {
                    var list = new List<JsonNode?>();
                    if (element is JsonElement source)
                    {
                        foreach (JsonElement item in source.EnumerateArray())
                        {
                            JsonNode? node = FromElement(item, Options);
                            node?.AssignParent(this);
                            list.Add(node);
                        }
                    }
                    items = list;
                    element = null;
                }
                return items;
            }
        }

        public static JsonArray? Create(JsonElement element, JsonNodeOptions? options = null)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null:
                    return null;
                case JsonValueKind.Array:
                    return new JsonArray(element, options);
                default:
                    throw new InvalidOperationException("The element must be of type 'Array'.");
            }
        }

        public void Add(JsonNode? item)
        {
            item?.AssignParent(this);
            Items.Add(item);
        }

        public void Add<T>(T? value)
        {
            JsonNode? node;
            if (value == null)
                node = null;
            else if (value is JsonNode existing)
                node = existing;
            else if (value is JsonElement source)
                node = FromElement(source, Options);
            else
                node = new JsonValueOf<T>(value, Options);
            Add(node);
        }

        public void Clear()
        {
            if (items == null)
            {
                element = null;
                return;
            }
            foreach (JsonNode? item in items)
                item?.DetachParent();
            items.Clear();
        }

        public bool Contains(JsonNode? item) => Items.Contains(item);

        public int IndexOf(JsonNode? item) => Items.IndexOf(item);

        public void Insert(int index, JsonNode? item)
        {
            item?.AssignParent(this);
            Items.Insert(index, item);
        }

        public bool Remove(JsonNode? item)
        {
            if (!Items.Remove(item))
                return false;
            item?.DetachParent();
            return true;
        }

        public void RemoveAt(int index)
        {
            JsonNode? item = Items[index];
            Items.RemoveAt(index);
            item?.DetachParent();
        }

        public IEnumerator<JsonNode?> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        void ICollection<JsonNode?>.CopyTo(JsonNode?[] array, int arrayIndex) => Items.CopyTo(array, arrayIndex);

        public override void WriteTo(Utf8JsonWriter writer, JsonSerializerOptions? options = null)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (element is JsonElement source)
            {
                source.WriteTo(writer);
                return;
            }
            writer.WriteStartArray();
            foreach (JsonNode? item in Items)
            {
                if (item == null)
                    writer.WriteNullValue();
                else
                    item.WriteTo(writer, options);
            }
            writer.WriteEndArray();
        }

        private protected override JsonValueKind GetValueKindCore() => JsonValueKind.Array;

        internal override JsonNode DeepCloneCore()
        {
            if (items == null)
                return element is JsonElement source ? new JsonArray(source.Clone(), Options) : new JsonArray(Options);
            var clone = new JsonArray(Options);
            foreach (JsonNode? item in items)
                clone.Add(item?.DeepCloneCore());
            return clone;
        }

        internal JsonNode? GetItem(int index) => Items[index];

        // parents are assigned in order after the copy: on a failure the earlier nodes stay attached to the discarded array, like the reference
        void Initialize(JsonNode?[] source)
        {
            var list = new List<JsonNode?>(source);
            foreach (JsonNode? item in source)
                item?.AssignParent(this);
            items = list;
        }

        internal void SetItem(int index, JsonNode? value)
        {
            value?.AssignParent(this);
            Items[index]?.DetachParent();
            Items[index] = value;
        }
    }
}
