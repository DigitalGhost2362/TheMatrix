#nullable enable
using System;

namespace Feeder.Json
{
    public readonly partial struct JsonElement
    {
        internal ReadOnlySpan<byte> GetRawValueSpan() => Parent.GetRawValue(idx);
    }
}
