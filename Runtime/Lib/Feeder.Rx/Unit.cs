#nullable enable
using System;

namespace Feeder.Rx
{
    public readonly struct Unit : IEquatable<Unit>
    {
        public static readonly Unit Default = default;

        public bool Equals(Unit other) => true;

        public override bool Equals(object? obj) => obj is Unit;

        public override int GetHashCode() => 0;
    }
}
