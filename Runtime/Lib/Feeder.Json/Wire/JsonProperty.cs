#nullable enable
using System;

namespace Feeder.Json
{
    public readonly struct JsonProperty
    {
        internal JsonProperty(JsonElement value)
        {
            Value = value;
        }

        public JsonElement Value { get; }

        public string Name => Value.GetPropertyName();

        public void WriteTo(Utf8JsonWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            Value.WritePropertyNameTo(writer);
            Value.WriteTo(writer);
        }

        public override string ToString() => Value.GetPropertyRawText();
    }
}
