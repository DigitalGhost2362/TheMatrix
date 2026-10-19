#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Feeder.MCP.Runtime.Utils;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Feeder.MCP.Editor.Query
{
    internal static class QueryOps
    {
        static readonly Dictionary<string, Regex> regexCache = new Dictionary<string, Regex>();

        public static bool IsNull(object? value) => value == null || (value is Object obj && obj == null);

        public static bool Truthy(object? value) => value switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            Object obj => obj != null,
            ICollection c => c.Count > 0,
            _ when IsNumeric(value) => ToDouble(value) != 0,
            _ => true,
        };

        public static bool IsNumeric(object? value) => value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;
        public static bool IsIntegral(object? value) => value is sbyte or byte or short or ushort or int or uint or long or ulong;
        public static double ToDouble(object value) => Convert.ToDouble(value, CultureInfo.InvariantCulture);

        public static string Text(object? value) => value as string ?? QueryFormat.Cell(value);

        public static bool AreEqual(object? a, object? b)
        {
            var aNull = IsNull(a);
            var bNull = IsNull(b);
            if (aNull || bNull)
                return aNull && bNull;
            if (IsNumeric(a) && IsNumeric(b))
                return a is float || b is float
                    ? (float)ToDouble(a!) == (float)ToDouble(b!)
                    : ToDouble(a!) == ToDouble(b!);
            if (a is Enum || b is Enum)
                return EnumEquals(a!, b!);
            if (a is string sa && b is string sb)
                return string.Equals(sa, sb, StringComparison.Ordinal);
            if (a!.GetType() == b!.GetType())
                return SameTypeEquals(a, b);
            if (a is Object || b is Object)
                return ReferenceEquals(a, b);
            if (QueryConvert.TryTo(b, a.GetType(), out var converted, out _, strictObjects: true))
                return SameTypeEquals(a, converted!);
            if (QueryConvert.TryTo(a, b.GetType(), out converted, out _, strictObjects: true))
                return SameTypeEquals(converted!, b);
            return false;
        }

        // unity structs compare with their own == (approximate for vectors, quaternions and colors)
        static bool SameTypeEquals(object a, object b)
        {
            var type = a.GetType();
            if (type.IsValueType && !type.IsPrimitive)
            {
                var op = type.GetMethod("op_Equality", BindingFlags.Public | BindingFlags.Static, null, new[] { type, type }, null);
                if (op != null)
                    return (bool)op.Invoke(null, new[] { a, b });
            }
            return a.Equals(b);
        }

        static bool EnumEquals(object a, object b)
        {
            var e = a as Enum ?? (Enum)b;
            var other = ReferenceEquals(e, a) ? b : a;
            if (other is string s)
                return string.Equals(e.ToString(), s, StringComparison.OrdinalIgnoreCase)
                    || (QueryConvert.TryTo(s, e.GetType(), out var parsed, out _) && Equals(parsed, e));
            if (IsNumeric(other))
                return Convert.ToInt64(e, CultureInfo.InvariantCulture) == Convert.ToInt64(other, CultureInfo.InvariantCulture);
            return Equals(a, b);
        }

        public static int? Compare(object? a, object? b)
        {
            if (IsNull(a) || IsNull(b))
                return null;
            if (IsNumeric(a) && IsNumeric(b))
                return ToDouble(a!).CompareTo(ToDouble(b!));
            if (a is Enum && (IsNumeric(b) || b is Enum))
                return Convert.ToInt64(a, CultureInfo.InvariantCulture).CompareTo(Convert.ToInt64(b, CultureInfo.InvariantCulture));
            if (a is string sa && b is string sb)
                return string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
            if (a is IComparable ca && a!.GetType() == b!.GetType())
                return ca.CompareTo(b);
            if (a is IComparable cb && QueryConvert.TryTo(b, a!.GetType(), out var converted, out _))
                return cb.CompareTo(converted);
            return null;
        }

        public static int SortCompare(object? a, object? b)
        {
            var aNull = IsNull(a);
            var bNull = IsNull(b);
            if (aNull || bNull)
                return aNull == bNull ? 0 : aNull ? 1 : -1;
            return Compare(a, b) ?? string.Compare(Text(a), Text(b), StringComparison.OrdinalIgnoreCase);
        }

        public static bool Contains(object? container, object? item)
        {
            if (IsNull(container))
                return false;
            if (container is string s)
                return item != null && s.IndexOf(Text(item), StringComparison.OrdinalIgnoreCase) >= 0;
            if (container is IEnumerable list)
            {
                foreach (var element in list)
                    if (AreEqual(element, item))
                        return true;
            }
            return false;
        }

        public static Regex MakeRegex(string pattern, int position)
        {
            lock (regexCache)
            {
                if (regexCache.TryGetValue(pattern, out var cached))
                    return cached;
                try
                {
                    var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
                    if (regexCache.Count > 256)
                        regexCache.Clear();
                    regexCache[pattern] = regex;
                    return regex;
                }
                catch (ArgumentException e)
                {
                    throw new QueryException($"Invalid regex '{pattern}': {e.Message}", position);
                }
            }
        }

        public static bool IsOfType(object? value, Type type)
        {
            if (IsNull(value))
                return false;
            if (type.IsInstanceOfType(value))
                return true;
            return value is GameObject go && typeof(Component).IsAssignableFrom(type) && go.GetComponent(type) != null;
        }

        public static object? Negate(object? value, int position)
        {
            if (value == null)
                return null;
            if (IsIntegral(value))
                return -Convert.ToInt64(value, CultureInfo.InvariantCulture);
            if (IsNumeric(value))
                return -ToDouble(value);
            var op = value.GetType().GetMethod("op_UnaryNegation", BindingFlags.Public | BindingFlags.Static, null, new[] { value.GetType() }, null);
            if (op != null)
                return op.Invoke(null, new[] { value });
            throw new QueryException($"Cannot negate a {QueryTypes.ShortName(value.GetType())}", position);
        }

        public static object? Arithmetic(string op, object? a, object? b, int position)
        {
            if (op == "+" && (a is string || b is string))
                return Text(a) + Text(b);
            if (a == null || b == null)
                return null;
            if (IsNumeric(a) && IsNumeric(b))
            {
                if (IsIntegral(a) && IsIntegral(b) && op != "/")
                {
                    var x = Convert.ToInt64(a, CultureInfo.InvariantCulture);
                    var y = Convert.ToInt64(b, CultureInfo.InvariantCulture);
                    return op switch
                    {
                        "+" => x + y,
                        "-" => x - y,
                        "*" => x * y,
                        "%" => y == 0 ? throw new QueryException("Division by zero", position) : x % y,
                        _ => throw new QueryException($"Unknown operator '{op}'", position),
                    };
                }
                var dx = ToDouble(a);
                var dy = ToDouble(b);
                return op switch
                {
                    "+" => dx + dy,
                    "-" => dx - dy,
                    "*" => dx * dy,
                    "/" => dy == 0 ? throw new QueryException("Division by zero", position) : dx / dy,
                    "%" => dy == 0 ? throw new QueryException("Division by zero", position) : dx % dy,
                    _ => throw new QueryException($"Unknown operator '{op}'", position),
                };
            }
            var name = op switch
            {
                "+" => "op_Addition",
                "-" => "op_Subtraction",
                "*" => "op_Multiply",
                "/" => "op_Division",
                "%" => "op_Modulus",
                _ => throw new QueryException($"Unknown operator '{op}'", position),
            };
            if (TryOperator(name, a, b, out var result))
                return result;
            throw new QueryException($"Operator '{op}' is not defined for {QueryTypes.ShortName(a.GetType())} and {QueryTypes.ShortName(b.GetType())}", position);
        }

        static bool TryOperator(string name, object a, object b, out object? result)
        {
            foreach (var owner in new[] { a.GetType(), b.GetType() }.Distinct())
            {
                foreach (var method in owner.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.Name != name)
                        continue;
                    var ps = method.GetParameters();
                    if (ps.Length != 2)
                        continue;
                    if (QueryConvert.TryTo(a, ps[0].ParameterType, out var x, out _, strictObjects: true)
                        && QueryConvert.TryTo(b, ps[1].ParameterType, out var y, out _, strictObjects: true)
                        && (IsSameShape(a, ps[0].ParameterType) && IsSameShape(b, ps[1].ParameterType)))
                    {
                        result = method.Invoke(null, new[] { x, y });
                        return true;
                    }
                }
            }
            result = null;
            return false;
        }

        // keeps Vector3 * 2 on (Vector3, float) instead of converting the number into a vector
        static bool IsSameShape(object value, Type parameter) => IsNumeric(value) == (parameter.IsPrimitive && parameter != typeof(bool));
    }

    internal static class QueryConvert
    {
        public static object? To(object? value, Type target, int position)
            => TryTo(value, target, out var result, out var error) ? result : throw new QueryException(error!, position);

        public static bool TryTo(object? value, Type target, out object? result, out string? error, bool strictObjects = false)
        {
            error = null;
            result = null;
            var underlying = Nullable.GetUnderlyingType(target);
            if (value == null || (value is Object nullObj && nullObj == null))
            {
                if (!target.IsValueType || underlying != null)
                    return true;
                error = $"Cannot assign null to {QueryTypes.ShortName(target)}";
                return false;
            }
            if (underlying != null)
                target = underlying;
            if (target.IsInstanceOfType(value))
            {
                result = value;
                return true;
            }
            try
            {
                if (TryConvertCore(value, target, strictObjects, out result))
                    return true;
            }
            catch (Exception e) when (e is FormatException or OverflowException or InvalidCastException or ArgumentException)
            {
                error = $"Cannot convert {Describe(value)} to {QueryTypes.ShortName(target)}: {e.Message}";
                return false;
            }
            error = $"Cannot convert {Describe(value)} to {QueryTypes.ShortName(target)}";
            return false;
        }

        static string Describe(object value) => value is string s ? $"\"{s}\"" : $"{QueryTypes.ShortName(value.GetType())} {QueryFormat.Cell(value)}";

        static bool TryConvertCore(object value, Type target, bool strictObjects, out object? result)
        {
            result = null;
            var inv = CultureInfo.InvariantCulture;

            if (target == typeof(string))
            {
                if (strictObjects && !(QueryOps.IsNumeric(value) || value is bool || value is Enum))
                    return false;
                result = QueryOps.Text(value);
                return true;
            }
            if (target.IsEnum)
                return TryEnum(value, target, out result);
            if (target == typeof(Type))
            {
                if (value is not string typeName || !QueryTypes.TryResolve(typeName, out var type, out _))
                    return false;
                result = type;
                return true;
            }
            if (target == typeof(bool))
            {
                if (value is string bs && bool.TryParse(bs, out var parsed)) { result = parsed; return true; }
                if (QueryOps.IsNumeric(value)) { result = QueryOps.ToDouble(value) != 0; return true; }
                return false;
            }
            if (target.IsPrimitive || target == typeof(decimal))
            {
                if (value is string ns)
                {
                    if (!double.TryParse(ns, NumberStyles.Float, inv, out var d))
                        return false;
                    value = d;
                }
                if (value is Enum)
                    value = Convert.ToInt64(value, inv);
                if (value is bool flag)
                    value = flag ? 1 : 0;
                if (!QueryOps.IsNumeric(value))
                    return false;
                if (IsIntegerType(target) && !QueryOps.IsIntegral(value))
                {
                    var d = QueryOps.ToDouble(value);
                    if (Math.Abs(d - Math.Round(d)) > 1e-9)
                        throw new FormatException($"{d.ToString(inv)} is not a whole number");
                    value = Math.Round(d);
                }
                result = Convert.ChangeType(value, target, inv);
                return true;
            }
            if (TryUnityStruct(value, target, out result))
                return true;
            if (typeof(Object).IsAssignableFrom(target))
                return !strictObjects && TryUnityObject(value, target, out result);
            if (target.IsArray && value is IEnumerable items && value is not string)
                return TryArray(items, target.GetElementType()!, out result);
            if (target.IsGenericType && value is IEnumerable listItems && value is not string)
            {
                var def = target.GetGenericTypeDefinition();
                if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IEnumerable<>) || def == typeof(IReadOnlyList<>) || def == typeof(ICollection<>))
                {
                    var element = target.GetGenericArguments()[0];
                    var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element));
                    foreach (var item in listItems)
                    {
                        if (!TryTo(item, element, out var converted, out _))
                            return false;
                        list.Add(converted);
                    }
                    result = list;
                    return true;
                }
            }
            return TryUserOperator(value, target, out result);
        }

        static bool IsIntegerType(Type t) => t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
            || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte);

        static bool TryEnum(object value, Type target, out object? result)
        {
            result = null;
            if (value is string s)
            {
                var parts = s.Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
                long bits = 0;
                foreach (var part in parts)
                {
                    var name = Enum.GetNames(target).FirstOrDefault(n => string.Equals(n, part, StringComparison.OrdinalIgnoreCase));
                    if (name == null)
                        throw new ArgumentException($"'{part}' is not a {target.Name}; values: {string.Join(", ", Enum.GetNames(target).Take(30))}");
                    bits |= Convert.ToInt64(Enum.Parse(target, name), CultureInfo.InvariantCulture);
                }
                if (parts.Length == 0)
                    return false;
                result = Enum.ToObject(target, bits);
                return true;
            }
            if (QueryOps.IsIntegral(value) || value is Enum)
            {
                result = Enum.ToObject(target, Convert.ToInt64(value, CultureInfo.InvariantCulture));
                return true;
            }
            return false;
        }

        static bool TryArray(IEnumerable items, Type element, out object? result)
        {
            var converted = new List<object?>();
            foreach (var item in items)
            {
                if (!TryTo(item, element, out var c, out _))
                {
                    result = null;
                    return false;
                }
                converted.Add(c);
            }
            var array = Array.CreateInstance(element, converted.Count);
            for (var i = 0; i < converted.Count; i++)
                array.SetValue(converted[i], i);
            result = array;
            return true;
        }

        static float[]? Floats(object value)
        {
            switch (value)
            {
                case Vector2 v2: return new[] { v2.x, v2.y };
                case Vector3 v3: return new[] { v3.x, v3.y, v3.z };
                case Vector4 v4: return new[] { v4.x, v4.y, v4.z, v4.w };
                case Vector2Int i2: return new float[] { i2.x, i2.y };
                case Vector3Int i3: return new float[] { i3.x, i3.y, i3.z };
                case Color c: return new[] { c.r, c.g, c.b, c.a };
                case Color32 c32: return new[] { c32.r / 255f, c32.g / 255f, c32.b / 255f, c32.a / 255f };
                case Quaternion q: return new[] { q.x, q.y, q.z, q.w };
                case string: return null;
                case IEnumerable list:
                    var floats = new List<float>();
                    foreach (var item in list)
                    {
                        if (!QueryOps.IsNumeric(item))
                            return null;
                        floats.Add((float)QueryOps.ToDouble(item!));
                    }
                    return floats.ToArray();
                default: return null;
            }
        }

        static bool TryUnityStruct(object value, Type target, out object? result)
        {
            result = null;
            if (target == typeof(Color) || target == typeof(Color32))
            {
                Color color;
                if (value is string html)
                {
                    if (!ColorUtility.TryParseHtmlString(html, out color))
                        throw new FormatException($"'{html}' is not a color; use '#RRGGBB', '#RRGGBBAA' or a name like 'red'");
                }
                else
                {
                    var f = Floats(value);
                    if (f == null || f.Length < 3 || f.Length > 4)
                        return false;
                    color = new Color(f[0], f[1], f[2], f.Length == 4 ? f[3] : 1f);
                }
                result = target == typeof(Color32) ? (object)(Color32)color : color;
                return true;
            }
            if (target == typeof(Quaternion))
            {
                var f = Floats(value);
                if (f == null)
                    return false;
                if (f.Length == 3) { result = Quaternion.Euler(f[0], f[1], f[2]); return true; }
                if (f.Length == 4) { result = new Quaternion(f[0], f[1], f[2], f[3]); return true; }
                return false;
            }
            if (target == typeof(LayerMask))
            {
                if (value is string layers)
                {
                    result = (LayerMask)LayerMask.GetMask(layers.Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray());
                    return true;
                }
                if (QueryOps.IsIntegral(value)) { result = (LayerMask)Convert.ToInt32(value, CultureInfo.InvariantCulture); return true; }
                return false;
            }
            var size = target == typeof(Vector2) || target == typeof(Vector2Int) ? 2
                : target == typeof(Vector3) || target == typeof(Vector3Int) ? 3
                : target == typeof(Vector4) ? 4 : 0;
            if (size == 0)
                return false;
            var v = Floats(value);
            if (v == null || v.Length < 2 || v.Length > 4)
                return false;
            float At(int i) => i < v.Length ? v[i] : 0f;
            if (target == typeof(Vector2Int) || target == typeof(Vector3Int))
            {
                for (var i = 0; i < v.Length; i++)
                    if (Math.Abs(v[i] - Mathf.Round(v[i])) > 1e-6f)
                        throw new FormatException($"{QueryFormat.Cell(value)} has non-whole components");
                result = target == typeof(Vector2Int)
                    ? (object)new Vector2Int(Mathf.RoundToInt(At(0)), Mathf.RoundToInt(At(1)))
                    : new Vector3Int(Mathf.RoundToInt(At(0)), Mathf.RoundToInt(At(1)), Mathf.RoundToInt(At(2)));
                return true;
            }
            result = size switch
            {
                2 => new Vector2(At(0), At(1)),
                3 => new Vector3(At(0), At(1), At(2)),
                _ => (object)new Vector4(At(0), At(1), At(2), At(3)),
            };
            return true;
        }

        static bool TryUnityObject(object value, Type target, out object? result)
        {
            result = null;
            Object? source = value as Object;
            if (source == null && value is string path)
            {
                source = path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal)
                    ? AssetDatabase.LoadAssetAtPath(path, target) ?? AssetDatabase.LoadMainAssetAtPath(path)
                    : GameObjectUtils.FindByPath(path);
                if (source == null)
                    throw new ArgumentException($"nothing found at '{path}' (asset paths start with Assets/, otherwise a hierarchy path)");
            }
            if (source == null && QueryOps.IsIntegral(value))
                source = QueryIds.Resolve(value);
            if (source == null)
                return false;
            if (target.IsInstanceOfType(source))
            {
                result = source;
                return true;
            }
            var go = source as GameObject ?? (source as Component)?.gameObject;
            if (go != null)
            {
                if (target == typeof(GameObject)) { result = go; return true; }
                if (typeof(Component).IsAssignableFrom(target))
                {
                    var component = go.GetComponent(target);
                    if (component == null)
                        throw new ArgumentException($"'{go.name}' has no {target.Name}");
                    result = component;
                    return true;
                }
            }
            return false;
        }

        static bool TryUserOperator(object value, Type target, out object? result)
        {
            result = null;
            foreach (var owner in new[] { target, value.GetType() })
            {
                foreach (var method in owner.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if ((method.Name != "op_Implicit" && method.Name != "op_Explicit") || method.ReturnType != target)
                        continue;
                    var parameter = method.GetParameters()[0].ParameterType;
                    if (parameter.IsInstanceOfType(value))
                    {
                        result = method.Invoke(null, new[] { value });
                        return true;
                    }
                    if (QueryOps.IsNumeric(value) && (parameter.IsPrimitive || parameter == typeof(decimal)) && parameter != typeof(bool))
                    {
                        result = method.Invoke(null, new[] { Convert.ChangeType(value, parameter, CultureInfo.InvariantCulture) });
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
