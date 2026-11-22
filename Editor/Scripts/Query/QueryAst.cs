#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Feeder.MCP.Editor.Query
{
    internal delegate object? Eval(QueryContext ctx);

    internal abstract class Node
    {
        public int Position;
        public string Text = string.Empty;

        public abstract Eval Compile();
    }

    internal sealed class LiteralNode : Node
    {
        public readonly object? Value;
        public LiteralNode(object? value) => Value = value;

        public override Eval Compile()
        {
            var value = Value;
            return _ => value;
        }
    }

    internal sealed class ListNode : Node
    {
        public readonly List<Node> Items;
        public ListNode(List<Node> items) => Items = items;

        public override Eval Compile()
        {
            var items = Items.ConvertAll(n => n.Compile()).ToArray();
            return ctx =>
            {
                var list = new List<object?>(items.Length);
                foreach (var item in items)
                    list.Add(item(ctx));
                return list;
            };
        }
    }

    internal sealed class IdentNode : Node
    {
        public readonly string Name;
        public IdentNode(string name) => Name = name;

        public override Eval Compile()
        {
            var name = Name;
            var position = Position;
            return ctx => ctx.AtSource
                ? QuerySources.Global(name, ctx, position)
                : QueryMembers.Get(ctx.Item, name, ctx, position);
        }
    }

    internal sealed class MetaNode : Node
    {
        public readonly string Name;
        public MetaNode(string name) => Name = name;

        public override Eval Compile()
        {
            var position = Position;
            switch (Name.ToLowerInvariant())
            {
                case "": return ctx => ctx.Item;
                case "index": return ctx => (long)ctx.Index;
                case "id": return ctx => QueryIds.Of(ctx.Item);
                case "path": return ctx => QueryFormat.PathOf(ctx.Item);
                case "type": return ctx => ctx.Item == null ? null : QueryTypes.ShortName(ctx.Item.GetType());
                default: throw new QueryException($"Unknown meta field '${Name}'. Known: $, $index, $id, $path, $type", position);
            }
        }
    }

    internal sealed class MemberNode : Node
    {
        public readonly Node Target;
        public readonly string Name;

        public MemberNode(Node target, string name)
        {
            Target = target;
            Name = name;
        }

        public override Eval Compile()
        {
            var target = Target.Compile();
            var name = Name;
            var position = Position;
            return ctx => QueryMembers.Get(target(ctx), name, ctx, position);
        }
    }

    internal sealed class IndexNode : Node
    {
        public readonly Node Target;
        public readonly Node Index;

        public IndexNode(Node target, Node index)
        {
            Target = target;
            Index = index;
        }

        public override Eval Compile()
        {
            var target = Target.Compile();
            var index = Index.Compile();
            var position = Position;
            return ctx => QueryMembers.GetIndex(target(ctx), index(ctx), ctx, position);
        }
    }

    internal sealed class CallNode : Node
    {
        public readonly Node? Target;
        public readonly string Name;
        public readonly List<Node> Args;

        public CallNode(Node? target, string name, List<Node> args)
        {
            Target = target;
            Name = name;
            Args = args;
        }

        public override Eval Compile()
        {
            var args = Args.ConvertAll(a => a.Compile()).ToArray();
            var name = Name;
            var position = Position;
            if (Target == null && QueryFunctions.TryGet(name, out var function))
            {
                if (args.Length < function.MinArgs || args.Length > function.MaxArgs)
                    throw new QueryException($"{name}() takes {Arity(function)} argument(s), got {args.Length}", position);
                return ctx => function.Invoke(ctx, Evaluate(args, ctx), position);
            }
            var target = Target?.Compile();
            return ctx => QueryMembers.Call(target == null ? ctx.Item : target(ctx), name, Evaluate(args, ctx), ctx, position);
        }

        static string Arity(QueryFunction f) => f.MinArgs == f.MaxArgs ? f.MinArgs.ToString() : $"{f.MinArgs}-{f.MaxArgs}";

        internal static object?[] Evaluate(Eval[] args, QueryContext ctx)
        {
            if (args.Length == 0)
                return Array.Empty<object?>();
            var values = new object?[args.Length];
            for (var i = 0; i < args.Length; i++)
                values[i] = args[i](ctx);
            return values;
        }
    }

    internal sealed class UnaryNode : Node
    {
        public readonly string Op;
        public readonly Node Operand;

        public UnaryNode(string op, Node operand)
        {
            Op = op;
            Operand = operand;
        }

        public override Eval Compile()
        {
            var operand = Operand.Compile();
            var position = Position;
            if (Op == "-")
                return ctx => QueryOps.Negate(operand(ctx), position);
            return ctx => !QueryOps.Truthy(operand(ctx));
        }
    }

    internal sealed class BinaryNode : Node
    {
        public readonly string Op;
        public readonly Node Left;
        public readonly Node Right;

        public BinaryNode(string op, Node left, Node right)
        {
            Op = op;
            Left = left;
            Right = right;
        }

        public override Eval Compile()
        {
            var l = Left.Compile();
            var r = Right.Compile();
            var position = Position;
            switch (Op)
            {
                case "and": return ctx => QueryOps.Truthy(l(ctx)) && QueryOps.Truthy(r(ctx));
                case "or": return ctx => QueryOps.Truthy(l(ctx)) || QueryOps.Truthy(r(ctx));
                case "??": return ctx => QueryOps.IsNull(l(ctx)) ? r(ctx) : l(ctx);
                case "==": return ctx => QueryOps.AreEqual(l(ctx), r(ctx));
                case "!=": return ctx => !QueryOps.AreEqual(l(ctx), r(ctx));
                case "<": return ctx => QueryOps.Compare(l(ctx), r(ctx)) is int c && c < 0;
                case "<=": return ctx => QueryOps.Compare(l(ctx), r(ctx)) is int c && c <= 0;
                case ">": return ctx => QueryOps.Compare(l(ctx), r(ctx)) is int c && c > 0;
                case ">=": return ctx => QueryOps.Compare(l(ctx), r(ctx)) is int c && c >= 0;
                case "contains": return ctx => QueryOps.Contains(l(ctx), r(ctx));
                case "in": return ctx => QueryOps.Contains(r(ctx), l(ctx));
                case "startswith": return ctx => l(ctx) is { } a && r(ctx) is { } b && QueryOps.Text(a).StartsWith(QueryOps.Text(b), StringComparison.OrdinalIgnoreCase);
                case "endswith": return ctx => l(ctx) is { } a && r(ctx) is { } b && QueryOps.Text(a).EndsWith(QueryOps.Text(b), StringComparison.OrdinalIgnoreCase);
                case "matches":
                    if (Right is LiteralNode { Value: string pattern })
                    {
                        var regex = QueryOps.MakeRegex(pattern, position);
                        return ctx => l(ctx) is { } a && regex.IsMatch(QueryOps.Text(a));
                    }
                    return ctx => l(ctx) is { } a && r(ctx) is { } b && QueryOps.MakeRegex(QueryOps.Text(b), position).IsMatch(QueryOps.Text(a));
                default: return ctx => QueryOps.Arithmetic(Op, l(ctx), r(ctx), position);
            }
        }
    }

    internal sealed class IsNode : Node
    {
        public readonly Node Operand;
        public readonly string TypeName;

        public IsNode(Node operand, string typeName)
        {
            Operand = operand;
            TypeName = typeName;
        }

        public override Eval Compile()
        {
            var operand = Operand.Compile();
            var type = QueryTypes.Resolve(TypeName, Position);
            return ctx => QueryOps.IsOfType(operand(ctx), type);
        }
    }
}
