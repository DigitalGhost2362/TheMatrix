#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Feeder.MCP.Editor.Query
{
    internal sealed class QueryContext
    {
        public object? Item;
        public int Index;
        public bool AtSource;
        public readonly bool CanWrite;
        public readonly MutationSession? Mutation;
        readonly List<string> _warnings = new List<string>();
        readonly HashSet<string> _warningSet = new HashSet<string>();

        public QueryContext(MutationSession? mutation)
        {
            Mutation = mutation;
            CanWrite = mutation != null;
        }

        public IReadOnlyList<string> Warnings => _warnings;

        public void Warn(string message)
        {
            if (_warnings.Count < 20 && _warningSet.Add(message))
                _warnings.Add(message);
        }
    }

    // a projected record: select, group and logs produce these; columns read like members
    public sealed class Row
    {
        public readonly string[] Columns;
        public readonly object?[] Values;

        public Row(string[] columns, object?[] values)
        {
            Columns = columns;
            Values = values;
        }

        public bool TryGet(string name, out object? value)
        {
            for (var i = 0; i < Columns.Length; i++)
            {
                if (string.Equals(Columns[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    value = Values[i];
                    return true;
                }
            }
            value = null;
            return false;
        }

        public override string ToString() => string.Join(", ", Columns.Select((c, i) => $"{c}={QueryFormat.Cell(Values[i])}"));
    }

    internal static class QueryIds
    {
#pragma warning disable CS0618
        public static object? Of(object? item)
        {
            if (item is not Object obj || obj == null)
                return null;
#if UNITY_6000_5_OR_NEWER
            return obj.GetEntityId().ToULong().ToString();
#else
            return (long)obj.GetInstanceID();
#endif
        }

        public static Object? Resolve(object? id)
        {
            if (id == null)
                return null;
            var text = id is string s ? s : Convert.ToString(id, System.Globalization.CultureInfo.InvariantCulture);
#if UNITY_6000_5_OR_NEWER
            if (ulong.TryParse(text, out var raw))
            {
                var found = EditorUtility.EntityIdToObject(EntityId.FromULong(raw));
                if (found != null)
                    return found;
            }
            return int.TryParse(text, out var legacy)
                ? EditorUtility.EntityIdToObject(Feeder.MCP.Runtime.Utils.EntityIdUtils.FromLegacyInstanceId(legacy))
                : null;
#elif UNITY_6000_3_OR_NEWER
            return int.TryParse(text, out var instanceId) ? EditorUtility.EntityIdToObject((EntityId)instanceId) : null;
#else
            return int.TryParse(text, out var instanceId) ? EditorUtility.InstanceIDToObject(instanceId) : null;
#endif
        }
#pragma warning restore CS0618
    }

    internal static class QueryTypes
    {
        static readonly Dictionary<string, Type?> cache = new Dictionary<string, Type?>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Type> aliases = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["string"] = typeof(string), ["int"] = typeof(int), ["long"] = typeof(long), ["float"] = typeof(float),
            ["double"] = typeof(double), ["bool"] = typeof(bool), ["object"] = typeof(object),
        };

        public static Type Resolve(string name, int position)
            => TryResolve(name, out var type, out var error) ? type! : throw new QueryException(error!, position);

        public static bool TryResolve(string name, out Type? type, out string? error)
        {
            error = null;
            if (aliases.TryGetValue(name, out type))
                return true;
            lock (cache)
            {
                if (cache.TryGetValue(name, out type))
                {
                    if (type != null)
                        return true;
                    error = $"Unknown type '{name}'";
                    return false;
                }
            }
            var candidates = FindCandidates(name);
            if (candidates.Count == 1)
                type = candidates[0];
            else if (candidates.Count > 1)
            {
                var exactCase = candidates.Where(t => t.Name == name).ToList();
                if (exactCase.Count > 0)
                    candidates = exactCase;
                var unityObjects = candidates.Where(t => typeof(Object).IsAssignableFrom(t)).ToList();
                type = candidates.Count == 1 ? candidates[0]
                    : candidates.FirstOrDefault(t => t.Namespace == "UnityEngine")
                    ?? (unityObjects.Count == 1 ? unityObjects[0] : null);
                if (type == null)
                {
                    error = $"Type name '{name}' is ambiguous: {string.Join(", ", candidates.Take(6).Select(t => t.FullName))}. Use the full name.";
                    return false;
                }
            }
            lock (cache)
                cache[name] = type;
            if (type == null)
                error = $"Unknown type '{name}'. Use a component or class name such as Light, BoxCollider, UnityEngine.UI.Image";
            return type != null;
        }

        static List<Type> FindCandidates(string name)
        {
            var dotted = name.Contains('.');
            var result = new List<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                    continue;
                if (dotted)
                {
                    var exact = assembly.GetType(name, false, true);
                    if (exact != null)
                        result.Add(exact);
                    continue;
                }
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }
                foreach (var t in types)
                    if (t.IsPublic && !t.IsGenericTypeDefinition && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                        result.Add(t);
            }
            return result;
        }

        public static string ShortName(Type type)
        {
            if (!type.IsGenericType)
                return type.Name;
            var name = type.Name;
            var tick = name.IndexOf('`');
            return (tick > 0 ? name.Substring(0, tick) : name) + "<" + string.Join(", ", type.GetGenericArguments().Select(ShortName)) + ">";
        }
    }
}
