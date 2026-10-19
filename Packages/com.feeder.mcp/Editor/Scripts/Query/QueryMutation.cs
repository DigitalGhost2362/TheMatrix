#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Feeder.MCP.Editor.Query
{
    public sealed class Receipt
    {
        public string Target = string.Empty;
        public string Action = string.Empty;
        public string? Old;
        public string? New;
    }

    internal sealed class MutationSession
    {
        public readonly bool DryRun;
        public readonly int MaxTargets;
        public readonly List<Receipt> Receipts = new List<Receipt>();
        public readonly HashSet<Object> Touched = new HashSet<Object>();
        public readonly HashSet<UnityEngine.SceneManagement.Scene> Scenes = new HashSet<UnityEngine.SceneManagement.Scene>();

        public MutationSession(bool dryRun, int maxTargets)
        {
            DryRun = dryRun;
            MaxTargets = maxTargets;
        }

        public void Record(object? target, string action, object? old, object? now) => RecordLabeled(Label(target), action, old, now);

        // labels are taken before the change, so a rename or a reparent shows where the target was
        public void RecordLabeled(string label, string action, object? old, object? now) => Receipts.Add(new Receipt
        {
            Target = label,
            Action = action,
            Old = old == null ? null : QueryFormat.Cell(old),
            New = now == null ? null : QueryFormat.Cell(now),
        });

        public static string Label(object? target) => target switch
        {
            GameObject go when go != null => QueryFormat.PathOf(go) ?? go.name,
            Component c when c != null => $"{QueryFormat.PathOf(c)} ({c.GetType().Name})",
            Object obj when obj != null => QueryFormat.PathOf(obj) ?? obj.name,
            _ => QueryFormat.Cell(target),
        };

        public List<object?> Targets(IEnumerable<object?> input, string keyword, QueryContext ctx)
        {
            var targets = input.Where(t => !QueryOps.IsNull(t)).ToList();
            if (targets.Count > MaxTargets)
                throw new QueryException($"'{keyword}' would touch {targets.Count} targets, more than maxTargets ({MaxTargets}). Narrow it with 'where' or raise maxTargets. Nothing was changed.");
            if (targets.Count == 0)
                ctx.Warn($"'{keyword}' had no targets: nothing matched");
            return targets;
        }

        public void Touch(Object obj)
        {
            if (obj == null)
                return;
            if (PrefabUtility.IsPartOfPrefabInstance(obj))
                PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
            EditorUtility.SetDirty(obj);
            Touched.Add(obj);
            NoteScene(obj);
        }

        public void NoteScene(Object obj)
        {
            var go = obj as GameObject ?? (obj as Component)?.gameObject;
            if (go != null && go.scene.IsValid())
                Scenes.Add(go.scene);
        }

        public static void RecordUndo(IEnumerable<Object> targets, string name)
        {
            var list = targets.Where(t => t != null).Distinct().ToArray();
            if (list.Length > 0)
                Undo.RecordObjects(list, name);
        }

        public static QueryException Refuse(string keyword, List<string> errors)
            => new QueryException($"Nothing was changed: '{keyword}' failed on {errors.Count} target(s):\n- " + string.Join("\n- ", errors.Take(10))
                + (errors.Count > 10 ? $"\n- … and {errors.Count - 10} more" : string.Empty));
    }

    // a member path on the left of 'set': walks holders, then writes the leaf and copies value-type holders back up
    internal sealed class QueryPath
    {
        abstract class Segment
        {
            public int Position;
        }

        sealed class MemberSegment : Segment
        {
            public string Name = string.Empty;
        }

        sealed class IndexSegment : Segment
        {
            public Eval Index = null!;
        }

        sealed class Step
        {
            public Func<object?> Read = null!;
            public Action<object?>? Write;
            public Type ValueType = typeof(object);
            public bool ReturnsCopy;
            public Object? UndoOwner;
            public string Name = string.Empty;
        }

        readonly List<Segment> _segments;
        public readonly string Text;

        QueryPath(List<Segment> segments, string text)
        {
            _segments = segments;
            Text = text;
        }

        public static QueryPath From(Node node, int position)
        {
            var segments = new List<Segment>();
            Collect(node, segments);
            if (segments.Count == 0)
                throw new QueryException("'set' needs a member to assign, such as name, transform.position.x or sharedMaterial[\"_Color\"]", position);
            return new QueryPath(segments, node.Text);
        }

        static void Collect(Node node, List<Segment> segments)
        {
            switch (node)
            {
                case IdentNode ident:
                    segments.Add(new MemberSegment { Name = ident.Name, Position = ident.Position });
                    return;
                case MetaNode { Name: "" }:
                    return;
                case MemberNode member:
                    Collect(member.Target, segments);
                    segments.Add(new MemberSegment { Name = member.Name, Position = member.Position });
                    return;
                case IndexNode index:
                    Collect(index.Target, segments);
                    segments.Add(new IndexSegment { Index = index.Index.Compile(), Position = index.Position });
                    return;
                default:
                    throw new QueryException("'set' can only assign to a member path such as name, transform.position.x or sharedMaterial[\"_Color\"]", node.Position);
            }
        }

        public (object? Old, object? New) Plan(object root, object? value, QueryContext ctx)
        {
            var walk = Walk(root, ctx);
            var leaf = walk.Steps[walk.Steps.Count - 1];
            if (leaf.Write == null)
                throw new QueryException($"'{Text}' is read-only on {QueryTypes.ShortName(walk.Holders[walk.Holders.Count - 1].GetType())}");
            object? old;
            try { old = leaf.Read(); }
            catch (Exception) { old = null; }
            var converted = QueryConvert.To(value, leaf.ValueType, _segments[_segments.Count - 1].Position);
            Receivers(walk);
            return (old, converted);
        }

        public void Apply(object root, object? value, QueryContext ctx, MutationSession session)
        {
            var walk = Walk(root, ctx);
            var receivers = Receivers(walk);
            var undo = new List<Object>();
            foreach (var index in receivers)
            {
                if (walk.Holders[index] is Object holder)
                    undo.Add(holder);
                if (walk.Steps[index].UndoOwner is { } owner)
                    undo.Add(owner);
            }
            for (var i = receivers.Min(); i >= 0; i--)
            {
                if (walk.Holders[i] is Object nearest)
                {
                    undo.Add(nearest);
                    break;
                }
            }
            MutationSession.RecordUndo(undo, "Matrix query: set " + Text);
            var steps = walk.Steps;
            steps[steps.Count - 1].Write!(value);
            for (var i = steps.Count - 2; i >= 0; i--)
            {
                if (!receivers.Contains(i))
                    break;
                steps[i].Write!(walk.Holders[i + 1]);
            }
            foreach (var obj in undo)
                session.Touch(obj);
        }

        // indexes of the holders that receive a write: the leaf holder, then each value-type or copied holder up the chain
        static List<int> Receivers((List<object> Holders, List<Step> Steps) walk)
        {
            var leafIndex = walk.Steps.Count - 1;
            var receivers = new List<int> { leafIndex };
            for (var i = leafIndex - 1; i >= 0; i--)
            {
                var child = walk.Holders[i + 1];
                if (!child.GetType().IsValueType && !walk.Steps[i].ReturnsCopy)
                    break;
                if (walk.Steps[i].Write == null)
                    throw new QueryException($"'{walk.Steps[i].Name}' hands out a copy and cannot be written back, so changing part of it has no effect");
                receivers.Add(i);
            }
            return receivers;
        }

        (List<object> Holders, List<Step> Steps) Walk(object root, QueryContext ctx)
        {
            ctx.Item = root;
            var holders = new List<object> { root };
            var steps = new List<Step>();
            var current = root;
            for (var i = 0; i < _segments.Count; i++)
            {
                var step = Bind(_segments[i], current, ctx);
                steps.Add(step);
                if (i == _segments.Count - 1)
                    break;
                object? next;
                try { next = step.Read(); }
                catch (Exception e) { throw new QueryException($"reading '{step.Name}' failed: {QueryMembers.Unwrap(e).Message}", _segments[i].Position); }
                if (QueryOps.IsNull(next))
                    throw new QueryException($"'{step.Name}' is null on {QueryTypes.ShortName(current.GetType())}", _segments[i].Position);
                holders.Add(next!);
                current = next!;
            }
            return (holders, steps);
        }

        static Step Bind(Segment segment, object holder, QueryContext ctx)
        {
            if (segment is MemberSegment member)
            {
                if (holder is Row)
                    throw new QueryException("rows made by select or group cannot be changed; set members on the objects themselves", segment.Position);
                var accessor = QueryMembers.Find(holder.GetType(), member.Name)
                    ?? throw new QueryException($"'{member.Name}' is not a member of {QueryTypes.ShortName(holder.GetType())}", segment.Position);
                return new Step
                {
                    Name = accessor.Name,
                    Read = () => accessor.Get(holder),
                    Write = accessor.Set == null ? null : v => accessor.Set(holder, v),
                    ValueType = accessor.ValueType,
                    ReturnsCopy = accessor.ReturnsCopy,
                    UndoOwner = accessor.UndoOwner?.Invoke(holder),
                };
            }
            var key = ((IndexSegment)segment).Index(ctx);
            if (holder is Material material && key is string property)
            {
                return new Step
                {
                    Name = $"[\"{property}\"]",
                    Read = () => QueryMembers.GetIndex(material, property, ctx, segment.Position),
                    Write = v => QueryMembers.WriteShaderProperty(material, property, v),
                    ValueType = QueryMembers.ShaderPropertyValueType(material, property, segment.Position),
                };
            }
            if (holder is IList list && QueryOps.IsIntegral(key))
            {
                var i = Convert.ToInt32(key);
                if (i < 0)
                    i += list.Count;
                if (i < 0 || i >= list.Count)
                    throw new QueryException($"index {key} is out of range (count {list.Count})", segment.Position);
                var elementType = holder.GetType().IsArray ? holder.GetType().GetElementType()!
                    : holder.GetType().GetInterfaces().Concat(new[] { holder.GetType() })
                        .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IList<>))?.GetGenericArguments()[0] ?? typeof(object);
                return new Step
                {
                    Name = $"[{i}]",
                    Read = () => list[i],
                    Write = v => list[i] = v,
                    ValueType = elementType,
                    ReturnsCopy = elementType.IsValueType,
                };
            }
            if (holder is IDictionary dictionary && key != null)
            {
                var args = holder.GetType().IsGenericType ? holder.GetType().GetGenericArguments() : Type.EmptyTypes;
                var keyValue = args.Length == 2 ? QueryConvert.To(key, args[0], segment.Position) : key;
                return new Step
                {
                    Name = $"[{QueryFormat.Cell(key)}]",
                    Read = () => dictionary.Contains(keyValue!) ? dictionary[keyValue!] : null,
                    Write = v => dictionary[keyValue!] = v,
                    ValueType = args.Length == 2 ? args[1] : typeof(object),
                };
            }
            throw new QueryException($"cannot assign by index on {QueryTypes.ShortName(holder.GetType())}", segment.Position);
        }
    }

    internal sealed class SetStage : Stage
    {
        readonly List<(QueryPath Path, Eval Value, string Text)> _assignments;
        public override bool IsMutation => true;

        public SetStage(List<(QueryPath, Eval, string)> assignments) => _assignments = assignments;

        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx)
        {
            var session = ctx.Mutation!;
            var targets = session.Targets(input, "set", ctx);
            var plans = new List<(object Target, QueryPath Path, object? Old, object? New)>();
            var errors = new List<string>();
            for (var t = 0; t < targets.Count; t++)
            {
                var target = targets[t]!;
                foreach (var (path, value, _) in _assignments)
                {
                    try
                    {
                        var v = Evaluate(value, target, t, ctx);
                        var (old, converted) = path.Plan(target, v, ctx);
                        plans.Add((target, path, old, converted));
                    }
                    catch (QueryException e)
                    {
                        errors.Add($"{MutationSession.Label(target)}: {path.Text}: {e.Message}");
                    }
                }
            }
            if (errors.Count > 0)
                throw MutationSession.Refuse("set", errors);
            var labels = plans.Select(p => MutationSession.Label(p.Target)).ToList();
            for (var i = 0; i < plans.Count; i++)
            {
                var (target, path, old, value) = plans[i];
                path.Apply(target, value, ctx, session);
                session.RecordLabeled(labels[i], "set " + path.Text, old ?? "null", value ?? "null");
            }
            return targets;
        }
    }

    internal sealed class CallStage : Stage
    {
        readonly Eval? _holder;
        readonly string _method;
        readonly Eval[] _args;
        readonly string _text;
        public override bool IsMutation => true;

        public CallStage(CallNode call)
        {
            _holder = call.Target?.Compile();
            _method = call.Name;
            _args = call.Args.ConvertAll(a => a.Compile()).ToArray();
            _text = call.Text;
        }

        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx)
        {
            var session = ctx.Mutation!;
            var targets = session.Targets(input, "call", ctx);
            var bound = new List<(object Target, object Holder, MethodInfo Method, object?[] Args)>();
            var errors = new List<string>();
            for (var t = 0; t < targets.Count; t++)
            {
                var target = targets[t]!;
                try
                {
                    var holder = _holder == null ? target : Evaluate(_holder, target, t, ctx);
                    if (QueryOps.IsNull(holder))
                        throw new QueryException("the object to call on is null");
                    ctx.Item = target;
                    var args = CallNode.Evaluate(_args, ctx);
                    var (method, converted) = QueryMembers.Bind(holder!, _method, args, Position);
                    bound.Add((target, holder!, method, converted));
                }
                catch (QueryException e)
                {
                    errors.Add($"{MutationSession.Label(target)}: {e.Message}");
                }
            }
            if (errors.Count > 0)
                throw MutationSession.Refuse("call", errors);
            foreach (var (target, holder, method, args) in bound)
            {
                var label = MutationSession.Label(target);
                if (session.DryRun)
                {
                    session.RecordLabeled(label, "call " + _text, null, "not run in a dry run");
                    continue;
                }
                var undo = new List<Object>();
                if (holder is Object h)
                    undo.Add(h);
                if (target is Object o)
                    undo.Add(o);
                MutationSession.RecordUndo(undo, "Matrix query: call " + method.Name);
                var result = QueryMembers.Invoke(holder, method, args, Position);
                foreach (var obj in undo)
                    session.Touch(obj);
                session.RecordLabeled(label, "call " + _text, null, method.ReturnType == typeof(void) ? "done" : result ?? "null");
            }
            return targets;
        }
    }

    internal sealed class AddStage : Stage
    {
        readonly Type _type;
        public override bool IsMutation => true;

        public AddStage(Type type) => _type = type;

        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx)
        {
            if (!typeof(Component).IsAssignableFrom(_type) || _type.IsAbstract || _type == typeof(Transform))
                throw new QueryException($"'add' needs a concrete component type; {_type.Name} is not one", Position);
            var session = ctx.Mutation!;
            var targets = session.Targets(input, "add", ctx);
            var owners = new List<GameObject>();
            var errors = new List<string>();
            var single = _type.IsDefined(typeof(DisallowMultipleComponent), true);
            foreach (var target in targets)
            {
                var go = OwnerOf(target);
                if (go == null)
                    errors.Add($"{MutationSession.Label(target)}: not a GameObject or component");
                else if (EditorUtility.IsPersistent(go))
                    errors.Add($"{MutationSession.Label(target)}: is a prefab asset; open it in prefab mode or edit an instance");
                else if (single && go.GetComponent(_type) != null)
                    errors.Add($"{MutationSession.Label(go)}: already has a {_type.Name} and only one is allowed");
                else if (!owners.Contains(go))
                    owners.Add(go);
            }
            if (errors.Count > 0)
                throw MutationSession.Refuse("add", errors);
            var added = new List<object?>();
            foreach (var go in owners)
            {
                var component = Undo.AddComponent(go, _type);
                if (component == null)
                    throw new QueryException($"Unity refused to add {_type.Name} to {MutationSession.Label(go)} (see the console)", Position);
                session.Touch(go);
                session.Record(go, "add " + _type.Name, null, component);
                added.Add(component);
            }
            return added;
        }
    }

    internal sealed class DestroyStage : Stage
    {
        public override bool IsMutation => true;

        public override IEnumerable<object?> Run(IEnumerable<object?> input, QueryContext ctx)
        {
            var session = ctx.Mutation!;
            var targets = session.Targets(input, "destroy", ctx);
            var errors = new List<string>();
            foreach (var target in targets)
            {
                switch (target)
                {
                    case Object obj when EditorUtility.IsPersistent(obj):
                        errors.Add($"{MutationSession.Label(target)}: is an asset; delete assets with assets-delete");
                        break;
                    case Transform:
                        errors.Add($"{MutationSession.Label(target)}: a Transform cannot be removed; destroy its GameObject instead");
                        break;
                    case Component component when RequiredBy(component) is { } other:
                        errors.Add($"{MutationSession.Label(target)}: {other.GetType().Name} requires it; remove that first");
                        break;
                    case GameObject:
                    case Component:
                        break;
                    default:
                        errors.Add($"{MutationSession.Label(target)}: only GameObjects and components can be destroyed");
                        break;
                }
            }
            if (errors.Count > 0)
                throw MutationSession.Refuse("destroy", errors);
            foreach (var target in targets)
            {
                var obj = (Object)target!;
                if (obj == null)
                    continue;
                var label = MutationSession.Label(obj);
                session.NoteScene(obj);
                Undo.DestroyObjectImmediate(obj);
                session.Receipts.Add(new Receipt { Target = label, Action = "destroy", New = "destroyed" });
            }
            return Array.Empty<object?>();
        }

        static Component? RequiredBy(Component component)
        {
            var type = component.GetType();
            foreach (var other in component.GetComponents<Component>())
            {
                if (other == null || other == component)
                    continue;
                foreach (RequireComponent require in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                {
                    var needed = new[] { require.m_Type0, require.m_Type1, require.m_Type2 };
                    if (needed.Any(n => n != null && n.IsAssignableFrom(type))
                        && component.GetComponents(type).Length < 2)
                        return other;
                }
            }
            return null;
        }
    }
}
