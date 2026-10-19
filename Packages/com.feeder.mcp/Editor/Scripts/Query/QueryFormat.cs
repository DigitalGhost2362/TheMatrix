#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Feeder.MCP.Runtime.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Feeder.MCP.Editor.Query
{
    internal static class QueryFormat
    {
        const int MaxCell = 160;
        const int MaxListItems = 8;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        #region Cells

        public static string Cell(object? value) => Clip(Raw(value));

        static string Clip(string text)
        {
            text = text.Replace("\r", string.Empty).Replace("\n", "\\n").Replace("|", "\\|");
            return text.Length > MaxCell ? text.Substring(0, MaxCell - 1) + "…" : text;
        }

        static string Raw(object? value)
        {
            switch (value)
            {
                case null: return "null";
                case string s: return s;
                case bool b: return b ? "true" : "false";
                case float f: return Number(f);
                case double d: return Number(d);
                case decimal m: return m.ToString(Inv);
                case Enum e: return e.ToString();
                case Vector2 v: return $"({Number(v.x)}, {Number(v.y)})";
                case Vector3 v: return $"({Number(v.x)}, {Number(v.y)}, {Number(v.z)})";
                case Vector4 v: return $"({Number(v.x)}, {Number(v.y)}, {Number(v.z)}, {Number(v.w)})";
                case Vector2Int v: return $"({v.x}, {v.y})";
                case Vector3Int v: return $"({v.x}, {v.y}, {v.z})";
                case Quaternion q: return $"({Number(q.x)}, {Number(q.y)}, {Number(q.z)}, {Number(q.w)})";
                case Color c: return ColorText(c);
                case Color32 c32: return "#" + ColorUtility.ToHtmlStringRGBA(c32);
                case Rect r: return $"(x {Number(r.x)}, y {Number(r.y)}, w {Number(r.width)}, h {Number(r.height)})";
                case Bounds bounds: return $"(center {Raw(bounds.center)}, size {Raw(bounds.size)})";
                case LayerMask mask: return mask.value.ToString(Inv);
                case DateTime time: return time.ToLocalTime().ToString("HH:mm:ss.fff", Inv);
                case Type type: return QueryTypes.ShortName(type);
                case Scene scene: return scene.IsValid() ? scene.name : "invalid scene";
                case Row row: return "{" + row + "}";
                case Object obj: return obj == null ? "null" : $"{obj.name} ({obj.GetType().Name} #{QueryIds.Of(obj)})";
                case IEnumerable list:
                {
                    var items = new List<string>();
                    var count = 0;
                    foreach (var item in list)
                    {
                        if (count++ < MaxListItems)
                            items.Add(Raw(item));
                    }
                    var text = string.Join(", ", items);
                    return count > MaxListItems ? $"[{text}, … ({count} total)]" : $"[{text}]";
                }
                default:
                    return QueryOps.IsNumeric(value) ? Convert.ToString(value, Inv)! : value.ToString() ?? string.Empty;
            }
        }

        static string Number(double d) => double.IsNaN(d) || double.IsInfinity(d) ? d.ToString(Inv) : Math.Round(d, 4).ToString("0.####", Inv);

        static string ColorText(Color c)
            => c.r is >= 0 and <= 1 && c.g is >= 0 and <= 1 && c.b is >= 0 and <= 1 && c.a is >= 0 and <= 1
                ? "#" + ColorUtility.ToHtmlStringRGBA(c)
                : $"({Number(c.r)}, {Number(c.g)}, {Number(c.b)}, {Number(c.a)})";

        public static string? PathOf(object? item) => item switch
        {
            GameObject go when go != null => go.GetPath(),
            Component c when c != null => c.gameObject.GetPath(),
            Object obj when obj != null => AssetDatabase.GetAssetPath(obj) is { Length: > 0 } path ? path : null,
            Scene scene => scene.path,
            _ => null,
        };

        #endregion

        #region Rows

        static string Category(object? item) => item switch
        {
            Row => "row",
            GameObject => "gameobject",
            Component => "component",
            Object => "object",
            Scene => "scene",
            _ => "value",
        };

        public static Row ToRow(object? item)
        {
            switch (item)
            {
                case Row row:
                    return row;
                case GameObject go:
                    return new Row(new[] { "id", "path", "active", "components" }, new object?[]
                    {
                        QueryIds.Of(go), go.GetPath(), go.activeInHierarchy,
                        string.Join(", ", go.GetComponents<Component>().Where(c => c != null && c is not Transform).Select(c => c.GetType().Name)),
                    });
                case Component c:
                    return new Row(new[] { "id", "type", "path", "enabled" }, new object?[]
                    {
                        QueryIds.Of(c), c.GetType().Name, c.gameObject.GetPath(), EnabledOf(c),
                    });
                case Object obj:
                    return new Row(new[] { "id", "type", "name", "path" }, new object?[]
                    {
                        QueryIds.Of(obj), obj.GetType().Name, obj.name, PathOf(obj),
                    });
                case Scene scene:
                    return new Row(new[] { "name", "path", "loaded", "dirty", "active", "roots" }, new object?[]
                    {
                        scene.name, scene.path, scene.isLoaded, scene.isDirty, scene == SceneManager.GetActiveScene(), scene.rootCount,
                    });
                default:
                    return new Row(new[] { "value" }, new[] { item });
            }
        }

        static object? EnabledOf(Component c) => c switch
        {
            Behaviour b => b.enabled,
            Renderer r => r.enabled,
            Collider col => col.enabled,
            _ => "-",
        };

        public static string Table(IReadOnlyList<object?> items, int limit, int total)
        {
            if (items.Count == 0)
                return "0 rows";
            var sb = new StringBuilder();
            var shown = Math.Min(limit, items.Count);
            sb.Append(total == 1 ? "1 row" : $"{total} rows");
            if (shown < total)
                sb.Append($" (showing {shown}; add '| skip {shown} | take {limit}' for more)");
            sb.Append('\n');
            var mixed = items.Take(shown).Select(Category).Distinct().Count() > 1;
            string[]? header = null;
            for (var i = 0; i < shown; i++)
            {
                var row = mixed ? new Row(new[] { "type", "value" }, new object?[] { items[i]?.GetType().Name ?? "null", items[i] }) : ToRow(items[i]);
                if (header == null || !header.SequenceEqual(row.Columns))
                {
                    header = row.Columns;
                    sb.Append(string.Join(" | ", header)).Append('\n');
                }
                sb.Append(string.Join(" | ", row.Values.Select(Cell))).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        #endregion

        #region Json

        public static string Json(IReadOnlyList<object?> items, int limit, int total)
        {
            var sb = new StringBuilder();
            sb.Append("{\"total\":").Append(total).Append(",\"items\":[");
            var shown = Math.Min(limit, items.Count);
            for (var i = 0; i < shown; i++)
            {
                if (i > 0)
                    sb.Append(',');
                WriteJson(sb, ToRow(items[i]), 0);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static void WriteJson(StringBuilder sb, object? value, int depth)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case string s:
                    WriteString(sb, s);
                    return;
                case Enum e:
                    WriteString(sb, e.ToString());
                    return;
                case Row row:
                    sb.Append('{');
                    for (var i = 0; i < row.Columns.Length; i++)
                    {
                        if (i > 0)
                            sb.Append(',');
                        WriteString(sb, row.Columns[i]);
                        sb.Append(':');
                        WriteJson(sb, row.Values[i], depth + 1);
                    }
                    sb.Append('}');
                    return;
                case Vector2 v: WriteNumbers(sb, v.x, v.y); return;
                case Vector3 v: WriteNumbers(sb, v.x, v.y, v.z); return;
                case Vector4 v: WriteNumbers(sb, v.x, v.y, v.z, v.w); return;
                case Quaternion q: WriteNumbers(sb, q.x, q.y, q.z, q.w); return;
                case Vector2Int v: WriteNumbers(sb, v.x, v.y); return;
                case Vector3Int v: WriteNumbers(sb, v.x, v.y, v.z); return;
                case Object obj:
                    if (obj == null)
                    {
                        sb.Append("null");
                        return;
                    }
                    sb.Append("{\"id\":");
                    WriteJson(sb, QueryIds.Of(obj), depth + 1);
                    sb.Append(",\"name\":");
                    WriteString(sb, obj.name);
                    sb.Append(",\"type\":");
                    WriteString(sb, obj.GetType().Name);
                    if (PathOf(obj) is { } path)
                    {
                        sb.Append(",\"path\":");
                        WriteString(sb, path);
                    }
                    sb.Append('}');
                    return;
                case IEnumerable list when depth < 4 && value is not Color:
                {
                    sb.Append('[');
                    var count = 0;
                    foreach (var item in list)
                    {
                        if (count >= 100)
                            break;
                        if (count++ > 0)
                            sb.Append(',');
                        WriteJson(sb, item, depth + 1);
                    }
                    sb.Append(']');
                    return;
                }
            }
            if (QueryOps.IsNumeric(value))
            {
                var d = QueryOps.ToDouble(value);
                sb.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : Convert.ToString(value, Inv));
                return;
            }
            WriteString(sb, Raw(value));
        }

        static void WriteNumbers(StringBuilder sb, params float[] values)
        {
            sb.Append('[');
            for (var i = 0; i < values.Length; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append(Math.Round(values[i], 5).ToString("0.#####", Inv));
            }
            sb.Append(']');
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        #endregion
    }
}
