#nullable enable
using System;

namespace Feeder.Json
{
    public sealed partial class JsonDocument
    {
        // a document nobody disposes, so its elements stay valid and Clone() is free, like the reference ParseValue
        internal static JsonElement ParseElement(ReadOnlyMemory<byte> utf8Json, JsonDocumentOptions options) =>
            Parse(utf8Json, options.GetReaderOptions(), false).RootElement;

        internal static JsonElement ParseElement(string json, JsonDocumentOptions options)
        {
            var bytes = new byte[JsonText.Utf8Length(json.AsSpan())];
            JsonText.ToUtf8(json.AsSpan(), bytes);
            return ParseElement(bytes, options);
        }
    }
}
