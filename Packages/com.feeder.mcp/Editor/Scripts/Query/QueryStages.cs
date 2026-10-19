#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Feeder.MCP.Editor.Query
{
    internal abstract class Stage
    {
        public int Position;
        public string Keyword = string.Empty;
        public virtual bool IsMutation => false;

        public abstract IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx);

        protected static object? Evaluate(Eval eval, object? item, int index, QueryContext ctx)
        {
            ctx.Item = item;
            ctx.Index = index;
            return eval(ctx);
        }

        protected static GameObject? OwnerOf(object? item) => item switch
        {
            GameObject go when go != null => go,
            Component c when c != null => c.gameObject,
            _ => null,
        };
    }

    internal sealed class WhereStage : Stage
    {
        readonly Eval _predicate;
        public WhereStage(Node predicate) => _predicate = predicate.Compile();

        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx)
        {
            var i = 0;
            foreach (var item in input)
                if (QueryOps.Truthy(Evaluate(_predicate, item, i++, ctx)))
                    yield return item;
        }
    }

    internal sealed class SelectStage : Stage
    {
        readonly Eval[] _columns;
        readonly string[] _names;

        public SelectStage(List<(Eval Eval, string Name)> columns)
        {
            _columns = columns.Select(c => c.Eval).ToArray();
            _names = columns.Select(c => c.Name).ToArray();
        }

        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx)
        {
            var i = 0;
            foreach (var item in input)
            {
                var values = new object?[_columns.Length];
                for (var c = 0; c < _columns.Length; c++)
                    values[c] = Evaluate(_columns[c], item, i, ctx);
                i++;
                yield return new Row(_names, values);
            }
        }
    }

    internal sealed class SortStage : Stage
    {
        readonly List<(Eval Key, bool Descending)> _keys;
        public SortStage(List<(Eval, bool)> keys) => _keys = keys;

        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx)
        {
            var rows = input.Select((item, i) => (item, keys: _keys.Select(k => Evaluate(k.Key, item, i, ctx)).ToArray(), i)).ToList();
            rows.Sort((a, b) =>
            {
                for (var k = 0; k < _keys.Count; k++)
                {
                    var c = QueryOps.SortCompare(a.keys[k], b.keys[k]);
                    if (c != 0)
                        return _keys[k].Descending ? -c : c;
                }
                return a.i.CompareTo(b.i);
            });
            return rows.Select(r => r.item);
        }
    }

    internal sealed class TakeStage : Stage
    {
        readonly int _count;
        public TakeStage(int count) => _count = count;
        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx) => input.Take(_count);
    }

    internal sealed class SkipStage : Stage
    {
        readonly int _count;
        public SkipStage(int count) => _count = count;
        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx) => input.Skip(_count);
    }

    internal sealed class LastStage : Stage
    {
        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx)
        {
            var list = input.ToList();
            return list.Count == 0 ? list : new List<object?> { list[list.Count - 1] };
        }
    }

    internal sealed class ReverseStage : Stage
    {
        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx) => input.Reverse();
    }

    internal sealed class CountStage : Stage
    {
        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx) => new object?[] { (long)input.Count() };
    }

    internal sealed class DistinctStage : Stage
    {
        readonly Eval? _key;
        public DistinctStage(Node? key) => _key = key?.Compile();

        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx)
        {
            var seen = new HashSet<object>(KeyComparer.Instance);
            var i = 0;
            var sawNull = false;
            foreach (var item in input)
            {
                var key = _key == null ? item : Evaluate(_key, item, i, ctx);
                i++;
                if (QueryOps.IsNull(key))
                {
                    if (sawNull)
                        continue;
                    sawNull = true;
                    yield return _key == null ? null : item;
                    continue;
                }
                if (seen.Add(key!))
                    yield return item;
            }
        }
    }

    // groups and distinct compare values the way '==' does, with numbers of any width as one key
    internal sealed class KeyComparer : IEqualityComparer<object>
    {
        public static readonly KeyComparer Instance = new KeyComparer();
        public new bool Equals(object x, object y) => QueryOps.AreEqual(x, y);
        public int GetHashCode(object obj) => QueryOps.IsNumeric(obj) ? QueryOps.ToDouble(obj).GetHashCode()
            : obj is string s ? s.ToLowerInvariant().GetHashCode()
            : obj is Enum e ? e.ToString().ToLowerInvariant().GetHashCode()
            : obj.GetHashCode();
    }

    internal sealed class GroupStage : Stage
    {
        readonly Eval _key;
        readonly string _name;

        public GroupStage(Eval key, string name)
        {
            _key = key;
            _name = name;
        }

        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx)
        {
            var groups = new Dictionary<object, long>(KeyComparer.Instance);
            var order = new List<object>();
            long nulls = 0;
            var i = 0;
            foreach (var item in input)
            {
                var key = Evaluate(_key, item, i++, ctx);
                if (QueryOps.IsNull(key))
                {
                    nulls++;
                    continue;
                }
                if (!groups.ContainsKey(key!))
                {
                    groups[key!] = 0;
                    order.Add(key!);
                }
                groups[key!]++;
            }
            var columns = new[] { _name, "count" };
            var rows = order.Select(k => new Row(columns, new[] { k, (object)groups[k] })).ToList();
            if (nulls > 0)
                rows.Add(new Row(columns, new object?[] { null, nulls }));
            return rows.OrderByDescending(r => (long)r.Values[1]!).Cast<object?>();
        }
    }

    internal sealed class AggregateStage : Stage
    {
        readonly string _function;
        readonly Eval? _value;

        public AggregateStage(string function, Node? value)
        {
            _function = function;
            _value = value?.Compile();
        }

        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx)
        {
            var values = new List<object?>();
            var i = 0;
            foreach (var item in input)
            {
                var v = _value == null ? item : Evaluate(_value, item, i, ctx);
                i++;
                if (!QueryOps.IsNull(v))
                    values.Add(v);
            }
            yield return Aggregate(values);
        }

        object? Aggregate(List<object?> values)
        {
            if (_function == "min" || _function == "max")
            {
                if (values.Count == 0)
                    return null;
                var best = values[0];
                foreach (var v in values.Skip(1))
                {
                    var c = QueryOps.SortCompare(v, best);
                    if (_function == "min" ? c < 0 : c > 0)
                        best = v;
                }
                return best;
            }
            var nonNumeric = values.FirstOrDefault(v => !QueryOps.IsNumeric(v));
            if (nonNumeric != null)
                throw new QueryException($"'{_function}' needs numbers, got {QueryFormat.Cell(nonNumeric)}", Position);
            if (_function == "sum")
                return values.All(QueryOps.IsIntegral) ? (object)values.Sum(v => Convert.ToInt64(v)) : values.Sum(v => QueryOps.ToDouble(v!));
            return values.Count == 0 ? null : (object)values.Average(v => QueryOps.ToDouble(v!));
        }
    }

    internal sealed class GetStage : Stage
    {
        readonly Eval _value;
        public GetStage(Node value) => _value = value.Compile();

        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx)
        {
            var i = 0;
            foreach (var item in input)
            {
                var value = Evaluate(_value, item, i++, ctx);
                if (value is IEnumerable list && value is not string && value is not Object && value is not Row)
                {
                    foreach (var element in list)
                        yield return element;
                }
                else
                    yield return value;
            }
        }
    }

    internal sealed class ComponentsStage : Stage
    {
        readonly string? _typeName;
        public ComponentsStage(string? typeName) => _typeName = typeName;

        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx)
        {
            var type = _typeName == null ? typeof(Component) : QueryTypes.Resolve(_typeName, Position);
            if (!typeof(Component).IsAssignableFrom(type))
                throw new QueryException($"{type.Name} is not a component type", Position);
            foreach (var item in input)
            {
                var go = OwnerOf(item);
                if (go == null)
                    continue;
                foreach (var c in go.GetComponents(type))
                    if (c != null)
                        yield return c;
            }
        }
    }

    internal sealed class ChildrenStage : Stage
    {
        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx)
        {
            foreach (var item in input)
            {
                var go = OwnerOf(item);
                if (go == null)
                    continue;
                foreach (Transform child in go.transform)
                    yield return child.gameObject;
            }
        }
    }

    internal sealed class DescendantsStage : Stage
    {
        readonly int? _maxDepth;
        public DescendantsStage(int? maxDepth) => _maxDepth = maxDepth;

        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx)
        {
            foreach (var item in input)
            {
                var go = OwnerOf(item);
                if (go == null)
                    continue;
                foreach (var d in Walk(go.transform, 1))
                    yield return d;
            }
        }

        IEnumerable<object?> Walk(Transform parent, int depth)
        {
            if (_maxDepth.HasValue && depth > _maxDepth.Value)
                yield break;
            foreach (Transform child in parent)
            {
                yield return child.gameObject;
                foreach (var d in Walk(child, depth + 1))
                    yield return d;
            }
        }
    }

    internal sealed class ParentStage : Stage
    {
        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx)
        {
            var seen = new HashSet<GameObject>();
            foreach (var item in input)
            {
                var parent = OwnerOf(item)?.transform.parent;
                if (parent != null && seen.Add(parent.gameObject))
                    yield return parent.gameObject;
            }
        }
    }

    internal sealed class GameObjectStage : Stage
    {
        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx)
        {
            var seen = new HashSet<GameObject>();
            foreach (var item in input)
            {
                var go = OwnerOf(item);
                if (go != null && seen.Add(go))
                    yield return go;
            }
        }
    }
}
