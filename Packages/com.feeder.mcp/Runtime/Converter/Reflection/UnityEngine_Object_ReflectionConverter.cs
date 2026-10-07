#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Feeder.Json;
using Feeder.Reflect;
using Feeder.Reflect.Model;

namespace Feeder.MCP.Reflection.Converter
{
    public class UnityEngine_Object_ReflectionConverter : UnityEngine_Object_ReflectionConverter<UnityEngine.Object> { }
    public partial class UnityEngine_Object_ReflectionConverter<T> : UnityGenericReflectionConverter<T> where T : UnityEngine.Object
    {
        public override bool AllowCascadePropertiesConversion => true;
        public override bool AllowSetValue => true;

        // Treat object-ref JSON nodes (e.g. {"instanceID": ...}) as atomic on the merge-patch
        // (Reflector.TryPatch) path so they route through SetValue and resolve to the live
        // UnityEngine.Object instead of being structurally descended. Safe because the reflection
        // layer only applies atomic delegation to non-root nodes.
        public override bool TreatJsonObjectAsAtomicValue(Type type) => true;

        protected virtual IEnumerable<string> RestrictedInValuePropertyNames(Reflector reflector, JsonElement valueJsonElement) => new[]
        {
            nameof(SerializedMember.fields),
            nameof(SerializedMember.props)
        };

        protected virtual IEnumerable<string> GetKnownSerializableFields(Reflector reflector, object? obj)
            => Enumerable.Empty<string>();

        protected virtual IEnumerable<string> GetKnownSerializableProperties(Reflector reflector, object? obj)
            => Enumerable.Empty<string>();
    }
}
