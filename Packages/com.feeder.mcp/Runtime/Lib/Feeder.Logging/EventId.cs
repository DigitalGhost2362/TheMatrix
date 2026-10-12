#nullable enable
using System;

namespace Feeder.Logging
{
    public readonly struct EventId : IEquatable<EventId>
    {
        public EventId(int id, string? name = null)
        {
            Id = id;
            Name = name;
        }

        public int Id { get; }

        public string? Name { get; }

        public static implicit operator EventId(int id) => new EventId(id);

        public static bool operator ==(EventId left, EventId right) => left.Equals(right);

        public static bool operator !=(EventId left, EventId right) => !left.Equals(right);

        // identity is the numeric id alone
        public bool Equals(EventId other) => Id == other.Id;

        public override bool Equals(object? obj) => obj is EventId other && Equals(other);

        public override int GetHashCode() => Id;

        public override string ToString() => Name ?? Id.ToString();
    }
}
