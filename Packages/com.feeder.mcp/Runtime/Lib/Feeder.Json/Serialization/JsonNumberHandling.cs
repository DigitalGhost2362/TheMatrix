using System;

namespace Feeder.Json.Serialization
{
    [Flags]
    public enum JsonNumberHandling
    {
        Strict = 0,
        AllowReadingFromString = 1,
        AllowNamedFloatingPointLiterals = 4,
    }
}
