#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;

namespace Feeder.Json.Nodes
{
    public sealed class JsonObject : JsonNode, IDictionary<string, JsonNode?>
    {
        const int IndexThreshold = 9;

        JsonElement? element;
        List<KeyValuePair<string, JsonNode?>>? items;
        Dictionary<string, JsonNode?>? index;

        public JsonObject(JsonNodeOptions? options = null) : base(options)
        {
        }

        internal JsonObject(JsonElement element, JsonNodeOptions? options) : base(options)
        {
            this.element = element;
        }

        public int Count => Items.Count;

        bool ICollection<KeyValuePair<string, JsonNode?>>.IsReadOnly => false;

        ICollection<string> IDictionary<string, JsonNode?>.Keys
        {
            get
            {
                var keys = new List<string>(Items.Count);
                foreach (KeyValuePair<string, JsonNode?> item in Items)
                    keys.Add(item.Key);
                return keys;
            }
        }

        ICollection<JsonNode?> IDictionary<string, JsonNode?>.Values
        {
            get
            {
                var values = new List<JsonNode?>(Items.Count);
                foreach (KeyValuePair<string, JsonNode?> item in Items)
                    values.Add(item.Value);
                return values;
            }
        }

        // a parsed object keeps its element until first touched; a duplicate name fails here and leaves the element in place
        List<KeyValuePair<string, JsonNode?>> Items
        {
            get
            {
                if (items == null)
                {
                    var list = new List<KeyValuePair<string, JsonNode?>>();
                    if (element is JsonElement source)
                    {
                        var names = new HashSet<string>(Comparer);
                        foreach (JsonProperty property in source.EnumerateObject())
                        {
                            if (!names.Add(property.Name))
                                throw DuplicateKey(property.Name);
                            JsonNode? node = FromElement(property.Value, Options);
                            node?.AssignParent(this);
                            list.Add(new KeyValuePair<string, JsonNode?>(property.Name, node));
                        }
                    }
                    index = list.Count >= IndexThreshold ? BuildIndex(list) : null;
                    items = list;
                    element = null;
                }
                return items;
            }
        }

        StringComparer Comparer => Options?.PropertyNameCaseInsensitive == true ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        public static JsonObject? Create(JsonElement element, JsonNodeOptions? options = null)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null:
                    return null;
                case JsonValueKind.Object:
                    return new JsonObject(element, options);
                default:
                    throw new InvalidOperationException("The element must be of type 'Object'.");
            }
        }

        public void Add(string propertyName, JsonNode? value)
        {
            if (propertyName == null)
                throw new ArgumentNullException(nameof(propertyName));
            if (TryFind(propertyName, out _))
                throw DuplicateKey(propertyName);
            Append(propertyName, value);
            value?.AssignParent(this);
        }

        public void Add(KeyValuePair<string, JsonNode?> property) => Add(property.Key, property.Value);

        public void Clear()
        {
            if (items == null)
            {
                element = null;
                return;
            }
            foreach (KeyValuePair<string, JsonNode?> item in items)
                item.Value?.DetachParent();
            items.Clear();
            index = null;
        }

        public bool ContainsKey(string propertyName) => TryFind(propertyName ?? throw new ArgumentNullException(nameof(propertyName)), out _);

        public bool Remove(string propertyName)
        {
            int at = Position(propertyName ?? throw new ArgumentNullException(nameof(propertyName)));
            if (at < 0)
                return false;
            JsonNode? removed = items![at].Value;
            items.RemoveAt(at);
            index?.Remove(propertyName);
            removed?.DetachParent();
            return true;
        }

        public bool TryGetPropertyValue(string propertyName, out JsonNode? jsonNode) =>
            TryFind(propertyName ?? throw new ArgumentNullException(nameof(propertyName)), out jsonNode);

        public IEnumerator<KeyValuePair<string, JsonNode?>> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        bool IDictionary<string, JsonNode?>.TryGetValue(string key, out JsonNode? value) => TryGetPropertyValue(key, out value);

        bool ICollection<KeyValuePair<string, JsonNode?>>.Contains(KeyValuePair<string, JsonNode?> item) =>
            TryFind(item.Key, out JsonNode? value) && ReferenceEquals(value, item.Value);

        void ICollection<KeyValuePair<string, JsonNode?>>.CopyTo(KeyValuePair<string, JsonNode?>[] array, int arrayIndex) => Items.CopyTo(array, arrayIndex);

        bool ICollection<KeyValuePair<string, JsonNode?>>.Remove(KeyValuePair<string, JsonNode?> item) => Remove(item.Key);

        public override void WriteTo(Utf8JsonWriter writer, JsonSerializerOptions? options = null)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (element is JsonElement source)
            {
                source.WriteTo(writer);
                return;
            }
            writer.WriteStartObject();
            foreach (KeyValuePair<string, JsonNode?> item in Items)
            {
                writer.WritePropertyName(item.Key);
                if (item.Value == null)
                    writer.WriteNullValue();
                else
                    item.Value.WriteTo(writer, options);
            }
            writer.WriteEndObject();
        }

        private protected override JsonValueKind GetValueKindCore() => JsonValueKind.Object;

        internal override JsonNode DeepCloneCore()
        {
            if (items == null)
                return element is JsonElement source ? new JsonObject(source.Clone(), Options) : new JsonObject(Options);
            var clone = new JsonObject(Options);
            foreach (KeyValuePair<string, JsonNode?> item in items)
                clone.Add(item.Key, item.Value?.DeepCloneCore());
            return clone;
        }

        internal JsonNode? GetItem(string propertyName)
        {
            TryGetPropertyValue(propertyName, out JsonNode? value);
            return value;
        }

        // storing the node already held under the key is a no-op; a replaced node loses its parent
        internal void SetItem(string propertyName, JsonNode? value)
        {
            if (propertyName == null)
                throw new ArgumentNullException(nameof(propertyName));
            int at = Position(propertyName);
            if (at < 0)
            {
                value?.AssignParent(this);
                Append(propertyName, value);
                return;
            }
            JsonNode? existing = items![at].Value;
            if (ReferenceEquals(existing, value))
                return;
            value?.AssignParent(this);
            existing?.DetachParent();
            items[at] = new KeyValuePair<string, JsonNode?>(propertyName, value);
            if (index != null)
                index[propertyName] = value;
        }

        bool TryFind(string propertyName, out JsonNode? value)
        {
            List<KeyValuePair<string, JsonNode?>> list = Items;
            if (index != null)
                return index.TryGetValue(propertyName, out value);
            int at = Scan(list, propertyName);
            value = at >= 0 ? list[at].Value : null;
            return at >= 0;
        }

        int Position(string propertyName)
        {
            List<KeyValuePair<string, JsonNode?>> list = Items;
            return index != null && !index.ContainsKey(propertyName) ? -1 : Scan(list, propertyName);
        }

        int Scan(List<KeyValuePair<string, JsonNode?>> list, string propertyName)
        {
            StringComparer comparer = Comparer;
            for (int i = 0; i < list.Count; i++)
            {
                if (comparer.Equals(list[i].Key, propertyName))
                    return i;
            }
            return -1;
        }

        void Append(string propertyName, JsonNode? value)
        {
            List<KeyValuePair<string, JsonNode?>> list = Items;
            list.Add(new KeyValuePair<string, JsonNode?>(propertyName, value));
            if (index != null)
                index.Add(propertyName, value);
            else if (list.Count >= IndexThreshold)
                index = BuildIndex(list);
        }

        Dictionary<string, JsonNode?> BuildIndex(List<KeyValuePair<string, JsonNode?>> list)
        {
            var lookup = new Dictionary<string, JsonNode?>(list.Count, Comparer);
            foreach (KeyValuePair<string, JsonNode?> item in list)
                lookup.Add(item.Key, item.Value);
            return lookup;
        }

        static ArgumentException DuplicateKey(string propertyName) =>
            new ArgumentException("An item with the same key has already been added. Key: " + propertyName, "key");
    }
}
