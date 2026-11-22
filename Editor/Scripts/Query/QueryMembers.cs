#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Feeder.MCP.Editor.Query
{
    internal sealed class Accessor
    {
        public string Name = string.Empty;
        public Type ValueType = typeof(object);
        public Func<object, object?> Get = null!;
        public Action<object, object?>? Set;
        // value types, and arrays/lists a property hands out, are copies: a change must be written back to the holder
        public bool ReturnsCopy;
        public Func<object, Object?>? UndoOwner;
        public MemberInfo? Member;
        public bool ComponentShortcut;
    }

    internal static class QueryMembers
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly Dictionary<(Type, string), Accessor?> accessors = new Dictionary<(Type, string), Accessor?>();
        static readonly Dictionary<(Type, string, int), MethodInfo[]> methods = new Dictionary<(Type, string, int), MethodInfo[]>();
        static readonly Regex ReadOnlyMethod = new Regex(
            "^(Get|Has|Is|Can|Contains|Compare|Equals|To|Find|StartsWith|EndsWith|IndexOf|LastIndexOf|Substring|Trim|Split|Replace|Calculate|Evaluate|Peek|Inverse|Transform|Closest|Raycast|Overlap|Normalize|Dot|Cross|Distance|Magnitude|Clamp)",
            RegexOptions.Compiled);

        #region Reading

        public static object? Get(object? target, string name, QueryContext ctx, int position)
        {
            if (QueryOps.IsNull(target))
                return null;
            switch (target)
            {
                case Row row:
                    if (row.TryGet(name, out var cell))
                        return cell;
                    ctx.Warn($"no column '{name}' (columns: {string.Join(", ", row.Columns)})");
                    return null;
                case IDictionary dictionary when dictionary.Contains(name):
                    return dictionary[name];
            }
            var accessor = Find(target!.GetType(), name);
            if (accessor == null)
            {
                ctx.Warn($"'{name}' is not a member of {QueryTypes.ShortName(target.GetType())}");
                return null;
            }
            try
            {
                var value = accessor.Get(target);
                if (accessor.ComponentShortcut && target is GameObject go && go.GetComponents(accessor.ValueType).Length > 1)
                    ctx.Warn($"'{go.name}' has {go.GetComponents(accessor.ValueType).Length} {accessor.ValueType.Name} components; '{name}' reads the first. Use '| components {accessor.ValueType.Name}' to reach each one");
                return value;
            }
            catch (Exception e)
            {
                ctx.Warn($"reading {QueryTypes.ShortName(target.GetType())}.{name} failed: {Unwrap(e).Message}");
                return null;
            }
        }

        public static object? GetIndex(object? target, object? index, QueryContext ctx, int position)
        {
            if (QueryOps.IsNull(target) || index == null)
                return null;
            if (index is string key)
            {
                switch (target)
                {
                    case Row row:
                        return row.TryGet(key, out var cell) ? cell : null;
                    case Material material:
                        return ReadShaderProperty(material, key, position);
                    case GameObject go:
                        return go.GetComponent(QueryTypes.Resolve(key, position));
                    case Component component:
                        return component.GetComponent(QueryTypes.Resolve(key, position));
                    case IDictionary dictionary:
                        return dictionary.Contains(key) ? dictionary[key] : null;
                    default:
                        return Get(target, key, ctx, position);
                }
            }
            if (!QueryOps.IsIntegral(index))
                throw new QueryException($"Index must be a whole number or a name, got {QueryFormat.Cell(index)}", position);
            var i = Convert.ToInt32(index);
            switch (target)
            {
                case string s:
                    i = i < 0 ? s.Length + i : i;
                    return i >= 0 && i < s.Length ? s[i].ToString() : null;
                case Transform t:
                    i = i < 0 ? t.childCount + i : i;
                    return i >= 0 && i < t.childCount ? t.GetChild(i) : null;
                case GameObject go:
                    i = i < 0 ? go.transform.childCount + i : i;
                    return i >= 0 && i < go.transform.childCount ? go.transform.GetChild(i).gameObject : null;
                case IList list:
                    i = i < 0 ? list.Count + i : i;
                    if (i >= 0 && i < list.Count)
                        return list[i];
                    ctx.Warn($"index {index} is out of range (count {list.Count})");
                    return null;
                case IEnumerable sequence:
                    return i >= 0 ? sequence.Cast<object?>().Skip(i).FirstOrDefault() : sequence.Cast<object?>().Reverse().Skip(-i - 1).FirstOrDefault();
            }
            throw new QueryException($"{QueryTypes.ShortName(target!.GetType())} cannot be indexed", position);
        }

        internal static Exception Unwrap(Exception e) => e is TargetInvocationException { InnerException: { } inner } ? inner : e;

        #endregion

        #region Accessors

        public static Accessor? Find(Type type, string name)
        {
            lock (accessors)
            {
                if (accessors.TryGetValue((type, name), out var cached))
                    return cached;
            }
            var accessor = Resolve(type, name);
            lock (accessors)
                accessors[(type, name)] = accessor;
            return accessor;
        }

        static Accessor? Resolve(Type type, string name)
        {
            if (type == typeof(GameObject) && GameObjectAlias(name) is { } alias)
                return alias;
            if (type == typeof(Transform) || type == typeof(RectTransform))
            {
                if (string.Equals(name, "parent", StringComparison.OrdinalIgnoreCase))
                    return ParentAccessor(t => (Transform)t);
            }
            var member = FindMember(type, name, StringComparison.Ordinal) ?? FindMember(type, name, StringComparison.OrdinalIgnoreCase);
            if (member != null)
                return FromMember(type, member);
            if (type == typeof(GameObject) && QueryTypes.TryResolve(name, out var componentType, out _) && typeof(Component).IsAssignableFrom(componentType))
                return new Accessor { Name = componentType!.Name, ValueType = componentType, ComponentShortcut = true, Get = go => ((GameObject)go).GetComponent(componentType) };
            if (typeof(Component).IsAssignableFrom(type))
            {
                var onObject = Find(typeof(GameObject), name);
                if (onObject != null)
                {
                    return new Accessor
                    {
                        Name = onObject.Name,
                        ValueType = onObject.ValueType,
                        Get = c => onObject.Get(((Component)c).gameObject),
                        Set = onObject.Set == null ? null : (c, v) => onObject.Set(((Component)c).gameObject, v),
                        ReturnsCopy = onObject.ReturnsCopy,
                        UndoOwner = c => onObject.UndoOwner?.Invoke(((Component)c).gameObject) ?? ((Component)c).gameObject,
                        Member = onObject.Member,
                    };
                }
            }
            return null;
        }

        static MemberInfo? FindMember(Type type, string name, StringComparison comparison)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var property in t.GetProperties(Instance | BindingFlags.DeclaredOnly))
                {
                    if (!string.Equals(property.Name, name, comparison) || property.GetIndexParameters().Length > 0 || IsHidden(property))
                        continue;
                    if (property.GetMethod?.IsPublic == true)
                        return property;
                }
                foreach (var field in t.GetFields(Instance | BindingFlags.DeclaredOnly))
                {
                    if (!string.Equals(field.Name, name, comparison) || IsHidden(field))
                        continue;
                    if (field.IsPublic || field.IsDefined(typeof(SerializeField), true))
                        return field;
                }
            }
            return null;
        }

        static bool IsHidden(MemberInfo member)
            => member.GetCustomAttribute<ObsoleteAttribute>() is { IsError: true } || member.Name.Contains("<");

        public static IEnumerable<MemberInfo> ListMembers(Type type)
        {
            var seen = new HashSet<string>();
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var property in t.GetProperties(Instance | BindingFlags.DeclaredOnly))
                    if (property.GetMethod?.IsPublic == true && property.GetIndexParameters().Length == 0 && !IsHidden(property) && seen.Add(property.Name))
                        yield return property;
                foreach (var field in t.GetFields(Instance | BindingFlags.DeclaredOnly))
                    if ((field.IsPublic || field.IsDefined(typeof(SerializeField), true)) && !IsHidden(field) && seen.Add(field.Name))
                        yield return field;
            }
        }

        static Accessor FromMember(Type type, MemberInfo member)
        {
            var valueType = member is PropertyInfo p ? p.PropertyType : ((FieldInfo)member).FieldType;
            var accessor = new Accessor
            {
                Name = member.Name,
                ValueType = valueType,
                Member = member,
                Get = CompileGetter(member),
                ReturnsCopy = valueType.IsValueType || (member is PropertyInfo && (valueType.IsArray || IsList(valueType))),
            };
            if (member is PropertyInfo property && property.SetMethod?.IsPublic == true)
                accessor.Set = (holder, value) => property.SetValue(holder, value);
            else if (member is FieldInfo field && !field.IsInitOnly && !field.IsLiteral)
                accessor.Set = (holder, value) => field.SetValue(holder, value);
            return accessor;
        }

        static bool IsList(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>);

        static Func<object, object?> CompileGetter(MemberInfo member)
        {
            try
            {
                var holder = Expression.Parameter(typeof(object), "holder");
                var typed = Expression.Convert(holder, member.DeclaringType!);
                var access = Expression.MakeMemberAccess(typed, member);
                var lambda = Expression.Lambda<Func<object, object?>>(Expression.Convert(access, typeof(object)), holder);
                return lambda.Compile();
            }
            catch (Exception)
            {
                if (member is PropertyInfo property)
                    return holder => property.GetValue(holder);
                var field = (FieldInfo)member;
                return holder => field.GetValue(holder);
            }
        }

        static Accessor? GameObjectAlias(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "parent": return ParentAccessor(go => ((GameObject)go).transform, asGameObject: true);
                case "active":
                case "activeself":
                    return new Accessor
                    {
                        Name = "activeSelf",
                        ValueType = typeof(bool),
                        Get = go => ((GameObject)go).activeSelf,
                        Set = (go, v) => ((GameObject)go).SetActive((bool)v!),
                    };
                case "position": return TransformAlias(name, typeof(Vector3), t => t.position, (t, v) => t.position = (Vector3)v!);
                case "localposition": return TransformAlias(name, typeof(Vector3), t => t.localPosition, (t, v) => t.localPosition = (Vector3)v!);
                case "rotation": return TransformAlias(name, typeof(Quaternion), t => t.rotation, (t, v) => t.rotation = (Quaternion)v!);
                case "localrotation": return TransformAlias(name, typeof(Quaternion), t => t.localRotation, (t, v) => t.localRotation = (Quaternion)v!);
                case "eulerangles": return TransformAlias(name, typeof(Vector3), t => t.eulerAngles, (t, v) => t.eulerAngles = (Vector3)v!);
                case "localeulerangles": return TransformAlias(name, typeof(Vector3), t => t.localEulerAngles, (t, v) => t.localEulerAngles = (Vector3)v!);
                case "localscale": return TransformAlias(name, typeof(Vector3), t => t.localScale, (t, v) => t.localScale = (Vector3)v!);
                default: return null;
            }
        }

        static Accessor TransformAlias(string name, Type type, Func<Transform, object> get, Action<Transform, object?> set) => new Accessor
        {
            Name = name,
            ValueType = type,
            ReturnsCopy = true,
            Get = go => get(((GameObject)go).transform),
            Set = (go, v) => set(((GameObject)go).transform, v),
            UndoOwner = go => ((GameObject)go).transform,
        };

        static Accessor ParentAccessor(Func<object, Transform> transformOf, bool asGameObject = false) => new Accessor
        {
            Name = "parent",
            ValueType = asGameObject ? typeof(GameObject) : typeof(Transform),
            Get = holder =>
            {
                var parent = transformOf(holder).parent;
                return asGameObject ? (object?)(parent == null ? null : parent.gameObject) : parent;
            },
            Set = (holder, value) =>
            {
                var transform = transformOf(holder);
                var parent = value as Transform ?? (value as GameObject)?.transform;
                Undo.SetTransformParent(transform, parent, "Matrix query: parent");
            },
            UndoOwner = holder => transformOf(holder),
        };

        #endregion

        #region Shader properties

        static object? ReadShaderProperty(Material material, string name, int position)
        {
            var shader = material.shader;
            var index = shader.FindPropertyIndex(name);
            if (index < 0)
                throw new QueryException($"Material '{material.name}' ({shader.name}) has no property '{name}'", position);
            return shader.GetPropertyType(index) switch
            {
                ShaderPropertyType.Color => material.GetColor(name),
                ShaderPropertyType.Vector => material.GetVector(name),
                ShaderPropertyType.Texture => material.GetTexture(name),
                ShaderPropertyType.Int => (long)material.GetInteger(name),
                _ => (double)material.GetFloat(name),
            };
        }

        internal static Type ShaderPropertyValueType(Material material, string name, int position)
        {
            var shader = material.shader;
            var index = shader.FindPropertyIndex(name);
            if (index < 0)
                throw new QueryException($"Material '{material.name}' ({shader.name}) has no property '{name}'", position);
            return shader.GetPropertyType(index) switch
            {
                ShaderPropertyType.Color => typeof(Color),
                ShaderPropertyType.Vector => typeof(Vector4),
                ShaderPropertyType.Texture => typeof(Texture),
                ShaderPropertyType.Int => typeof(int),
                _ => typeof(float),
            };
        }

        internal static void WriteShaderProperty(Material material, string name, object? value)
        {
            switch (value)
            {
                case Color c: material.SetColor(name, c); break;
                case Vector4 v: material.SetVector(name, v); break;
                case Texture t: material.SetTexture(name, t); break;
                case null: material.SetTexture(name, null); break;
                case int i: material.SetInteger(name, i); break;
                default: material.SetFloat(name, Convert.ToSingle(value)); break;
            }
        }

        #endregion

        #region Methods

        public static object? Call(object? target, string name, object?[] args, QueryContext ctx, int position)
        {
            if (QueryOps.IsNull(target))
            {
                if (ctx.AtSource || ctx.Item == null)
                    throw new QueryException($"Unknown function '{name}'. Functions: {string.Join(", ", QueryFunctions.Names)}", position);
                return null;
            }
            var (chosen, converted) = Bind(target!, name, args, position);
            if (!ctx.CanWrite && (chosen.ReturnType == typeof(void) || !ReadOnlyMethod.IsMatch(chosen.Name)))
                throw new QueryException($"{chosen.Name}() may change the project; rql-query only calls reading methods (Get*, Is*, Has*, Find*, To*...). Use rql-mutate to call it.", position);
            return Invoke(target!, chosen, converted, position);
        }

        public static bool IsReadingMethod(MethodInfo method) => method.ReturnType != typeof(void) && ReadOnlyMethod.IsMatch(method.Name);

        public static object? Invoke(object target, MethodInfo method, object?[] args, int position)
        {
            try
            {
                var result = method.Invoke(target, args);
                return method.ReturnType == typeof(void) ? null : result;
            }
            catch (Exception e)
            {
                throw new QueryException($"{QueryTypes.ShortName(target.GetType())}.{method.Name}() failed: {Unwrap(e).Message}", position);
            }
        }

        public static (MethodInfo Method, object?[] Args) Bind(object target, string name, object?[] args, int position)
        {
            var type = target.GetType();
            var candidates = FindMethods(type, name, args.Length);
            if (candidates.Length == 0)
                throw new QueryException($"{QueryTypes.ShortName(type)} has no public method {name} with {args.Length} argument(s)", position);
            MethodInfo? chosen = null;
            object?[]? converted = null;
            var bestScore = int.MaxValue;
            foreach (var method in candidates)
            {
                var parameters = method.GetParameters();
                var values = new object?[parameters.Length];
                var score = 0;
                var ok = true;
                for (var i = 0; i < parameters.Length && ok; i++)
                {
                    if (i >= args.Length)
                    {
                        values[i] = parameters[i].DefaultValue;
                        continue;
                    }
                    var parameterType = parameters[i].ParameterType;
                    if (args[i] != null && parameterType.IsInstanceOfType(args[i]))
                        values[i] = args[i];
                    else if (QueryConvert.TryTo(args[i], parameterType, out var c, out _))
                    {
                        values[i] = c;
                        score++;
                    }
                    else
                        ok = false;
                }
                if (ok && score < bestScore)
                {
                    chosen = method;
                    converted = values;
                    bestScore = score;
                }
            }
            if (chosen == null)
                throw new QueryException($"No overload of {QueryTypes.ShortName(type)}.{name} accepts ({string.Join(", ", args.Select(a => a == null ? "null" : QueryTypes.ShortName(a.GetType())))})", position);
            return (chosen, converted!);
        }

        static MethodInfo[] FindMethods(Type type, string name, int argCount)
        {
            lock (methods)
            {
                if (methods.TryGetValue((type, name, argCount), out var cached))
                    return cached;
            }
            bool Fits(MethodInfo m)
            {
                if (m.IsGenericMethodDefinition || m.IsSpecialName || m.GetCustomAttribute<ObsoleteAttribute>() is { IsError: true })
                    return false;
                var ps = m.GetParameters();
                if (ps.Any(p => p.ParameterType.IsByRef || p.IsOut))
                    return false;
                return argCount <= ps.Length && ps.Skip(argCount).All(p => p.IsOptional);
            }
            var all = type.GetMethods(BindingFlags.Instance | BindingFlags.Public);
            var found = all.Where(m => m.Name == name && Fits(m)).ToArray();
            if (found.Length == 0)
                found = all.Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) && Fits(m)).ToArray();
            lock (methods)
                methods[(type, name, argCount)] = found;
            return found;
        }

        #endregion
    }
}
