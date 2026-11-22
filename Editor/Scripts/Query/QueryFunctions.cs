#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Feeder.MCP.Editor.Query
{
    internal sealed class QueryFunction
    {
        public readonly int MinArgs;
        public readonly int MaxArgs;
        public readonly string Usage;
        readonly Func<QueryContext, object?[], int, object?> _body;

        public QueryFunction(int minArgs, int maxArgs, string usage, Func<QueryContext, object?[], int, object?> body)
        {
            MinArgs = minArgs;
            MaxArgs = maxArgs;
            Usage = usage;
            _body = body;
        }

        public object? Invoke(QueryContext ctx, object?[] args, int position) => _body(ctx, args, position);
    }

    internal static class QueryFunctions
    {
        static readonly Dictionary<string, QueryFunction> table = new Dictionary<string, QueryFunction>(StringComparer.OrdinalIgnoreCase)
        {
            ["vec"] = new QueryFunction(2, 4, "vec(x, y[, z[, w]]) -> Vector2/3/4", (_, a, p) => Vector(a, p)),
            ["color"] = new QueryFunction(1, 4, "color(r, g, b[, a]) or color(\"#ff8800\" | \"red\")", (_, a, p) => a.Length == 1
                ? QueryConvert.To(a[0], typeof(Color), p)
                : QueryConvert.To(a.ToList(), typeof(Color), p)),
            ["euler"] = new QueryFunction(3, 3, "euler(x, y, z) -> Quaternion from degrees", (_, a, p) => Quaternion.Euler(F(a[0], p), F(a[1], p), F(a[2], p))),
            ["len"] = new QueryFunction(1, 1, "len(text or list)", (_, a, p) => Length(a[0])),
            ["count"] = new QueryFunction(1, 1, "count(list)", (_, a, p) => Length(a[0])),
            ["first"] = new QueryFunction(1, 1, "first(list)", (_, a, p) => a[0] is IEnumerable e and not string ? e.Cast<object?>().FirstOrDefault() : a[0]),
            ["last"] = new QueryFunction(1, 1, "last(list)", (_, a, p) => a[0] is IEnumerable e and not string ? e.Cast<object?>().LastOrDefault() : a[0]),
            ["lower"] = new QueryFunction(1, 1, "lower(text)", (_, a, p) => a[0] == null ? null : QueryOps.Text(a[0]).ToLowerInvariant()),
            ["upper"] = new QueryFunction(1, 1, "upper(text)", (_, a, p) => a[0] == null ? null : QueryOps.Text(a[0]).ToUpperInvariant()),
            ["trim"] = new QueryFunction(1, 1, "trim(text)", (_, a, p) => a[0] == null ? null : QueryOps.Text(a[0]).Trim()),
            ["str"] = new QueryFunction(1, 1, "str(value) -> text", (_, a, p) => a[0] == null ? null : QueryOps.Text(a[0])),
            ["num"] = new QueryFunction(1, 1, "num(value) -> number", (_, a, p) => a[0] == null ? null : QueryConvert.To(a[0], typeof(double), p)),
            ["abs"] = new QueryFunction(1, 1, "abs(x)", (_, a, p) => QueryOps.IsIntegral(a[0]) ? (object)Math.Abs(Convert.ToInt64(a[0])) : Math.Abs(D(a[0], p))),
            ["round"] = new QueryFunction(1, 2, "round(x[, digits])", (_, a, p) => Math.Round(D(a[0], p), a.Length > 1 ? (int)D(a[1], p) : 0, MidpointRounding.AwayFromZero)),
            ["floor"] = new QueryFunction(1, 1, "floor(x)", (_, a, p) => Math.Floor(D(a[0], p))),
            ["ceil"] = new QueryFunction(1, 1, "ceil(x)", (_, a, p) => Math.Ceiling(D(a[0], p))),
            ["sqrt"] = new QueryFunction(1, 1, "sqrt(x)", (_, a, p) => Math.Sqrt(D(a[0], p))),
            ["min"] = new QueryFunction(2, 2, "min(a, b)", (_, a, p) => QueryOps.SortCompare(a[0], a[1]) <= 0 ? a[0] : a[1]),
            ["max"] = new QueryFunction(2, 2, "max(a, b)", (_, a, p) => QueryOps.SortCompare(a[0], a[1]) >= 0 ? a[0] : a[1]),
            ["clamp"] = new QueryFunction(3, 3, "clamp(x, lo, hi)", (_, a, p) => Math.Min(Math.Max(D(a[0], p), D(a[1], p)), D(a[2], p))),
            ["distance"] = new QueryFunction(2, 2, "distance(a, b) between positions, transforms or objects", (_, a, p) => (double)Vector3.Distance(Position(a[0], p), Position(a[1], p))),
            ["iif"] = new QueryFunction(3, 3, "iif(condition, then, else)", (_, a, p) => QueryOps.Truthy(a[0]) ? a[1] : a[2]),
            ["has"] = new QueryFunction(1, 2, "has(\"Type\") or has(x, \"Type\"): object has that component", (c, a, p) => HasComponent(a.Length == 1 ? c.Item : a[0], a[^1], p)),
            ["comp"] = new QueryFunction(1, 2, "comp(\"Type\") or comp(x, \"Type\"): the component of that type", (c, a, p) => Component(a.Length == 1 ? c.Item : a[0], a[^1], p)),
            ["path"] = new QueryFunction(0, 1, "path([x]) hierarchy or asset path", (c, a, p) => QueryFormat.PathOf(a.Length == 0 ? c.Item : a[0])),
            ["id"] = new QueryFunction(0, 1, "id([x]) instance id", (c, a, p) => QueryIds.Of(a.Length == 0 ? c.Item : a[0])),
            ["type"] = new QueryFunction(0, 1, "type([x]) type name", (c, a, p) => (a.Length == 0 ? c.Item : a[0])?.GetType() is { } t ? QueryTypes.ShortName(t) : null),
            ["obj"] = new QueryFunction(1, 1, "obj(id) object by instance id", (_, a, p) => QueryIds.Resolve(a[0])),
            ["layer"] = new QueryFunction(1, 1, "layer(\"Name\") -> layer index", (_, a, p) => (long)LayerMask.NameToLayer(QueryOps.Text(a[0]))),
            ["layername"] = new QueryFunction(1, 1, "layername(index)", (_, a, p) => LayerMask.LayerToName((int)D(a[0], p))),
            ["asset"] = new QueryFunction(1, 1, "asset(\"Assets/...\") one asset", (_, a, p) => QuerySources.Asset(QueryOps.Text(a[0]), p)),
            ["assets"] = new QueryFunction(1, 2, "assets(\"t:Material\"[, \"Assets/Folder\"]) assets matching a search filter", (c, a, p) => QuerySources.Assets(QueryOps.Text(a[0]), a.Length > 1 ? QueryOps.Text(a[1]) : null, c)),
            ["find"] = new QueryFunction(1, 1, "find(\"Path/To/Object\" or \"UniqueName\" or id) one scene object", (_, a, p) => QuerySources.Find(a[0], p)),
            ["components"] = new QueryFunction(0, 1, "components([\"Type\"]) every component in the open scenes", (c, a, p) => QuerySources.Components(a.Length == 0 ? null : QueryTypes.Resolve(QueryOps.Text(a[0]), p))),
            ["new"] = new QueryFunction(1, 3, "new(\"Name\"[, \"Cube\"[, parent]]) creates a GameObject (rql-mutate only)", (c, a, p) => QuerySources.New(c, QueryOps.Text(a[0]), a.Length > 1 ? a[1] as string : null, a.Length > 2 ? a[2] : null, p)),
        };

        public static IEnumerable<string> Names => table.Keys;
        public static IEnumerable<KeyValuePair<string, QueryFunction>> All => table;

        public static bool TryGet(string name, out QueryFunction function) => table.TryGetValue(name, out function!);

        static double D(object? value, int position)
        {
            if (value == null || !QueryOps.IsNumeric(value))
                throw new QueryException($"Expected a number, got {QueryFormat.Cell(value)}", position);
            return QueryOps.ToDouble(value);
        }

        static float F(object? value, int position) => (float)D(value, position);

        static object Vector(object?[] a, int position) => a.Length switch
        {
            2 => new Vector2(F(a[0], position), F(a[1], position)),
            3 => new Vector3(F(a[0], position), F(a[1], position), F(a[2], position)),
            _ => new Vector4(F(a[0], position), F(a[1], position), F(a[2], position), F(a[3], position)),
        };

        static object? Length(object? value) => value switch
        {
            null => 0L,
            string s => (long)s.Length,
            ICollection c => (long)c.Count,
            IEnumerable e => (long)e.Cast<object?>().Count(),
            _ => 1L,
        };

        static Vector3 Position(object? value, int position) => value switch
        {
            Vector3 v => v,
            Vector2 v => v,
            Transform t => t.position,
            GameObject go => go.transform.position,
            UnityEngine.Component c => c.transform.position,
            _ => (Vector3)QueryConvert.To(value, typeof(Vector3), position)!,
        };

        static GameObject? OwnerOf(object? value) => value switch
        {
            GameObject go => go,
            UnityEngine.Component c => c.gameObject,
            _ => null,
        };

        static object HasComponent(object? target, object? typeName, int position)
        {
            var go = OwnerOf(target);
            return go != null && go.GetComponent(QueryTypes.Resolve(QueryOps.Text(typeName), position)) != null;
        }

        static object? Component(object? target, object? typeName, int position)
        {
            var go = OwnerOf(target);
            return go == null ? null : go.GetComponent(QueryTypes.Resolve(QueryOps.Text(typeName), position));
        }
    }
}
