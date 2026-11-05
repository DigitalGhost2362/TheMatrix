#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    public sealed class SerializedEntry
    {
        public string Path { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>Reads and writes SerializedProperty values as text, shared by the settings and ScriptableObject tools.</summary>
    public static class SerializedValues
    {
        public const int DefaultLimit = 200;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static int BeginUndo(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return Undo.GetCurrentGroup();
        }

        public static void EndUndo(int group) => Undo.CollapseUndoOperations(group);

        public static string Read(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.Character:
                    return p.longValue.ToString(Inv);
                case SerializedPropertyType.Boolean:
                    return p.boolValue ? "true" : "false";
                case SerializedPropertyType.Float:
                    return p.floatValue.ToString("R", Inv);
                case SerializedPropertyType.String:
                    return p.stringValue ?? string.Empty;
                case SerializedPropertyType.Color:
                    return "#" + ColorUtility.ToHtmlStringRGBA(p.colorValue);
                case SerializedPropertyType.Enum:
                    var names = p.enumNames;
                    return p.enumValueIndex >= 0 && p.enumValueIndex < names.Length ? names[p.enumValueIndex] : p.intValue.ToString(Inv);
                case SerializedPropertyType.ObjectReference:
                    var target = p.objectReferenceValue;
                    if (target == null)
                        return "null";
                    var path = AssetDatabase.GetAssetPath(target);
                    return string.IsNullOrEmpty(path) ? target.name : path;
                case SerializedPropertyType.Vector2:
                    return Join(p.vector2Value.x, p.vector2Value.y);
                case SerializedPropertyType.Vector3:
                    return Join(p.vector3Value.x, p.vector3Value.y, p.vector3Value.z);
                case SerializedPropertyType.Vector4:
                    return Join(p.vector4Value.x, p.vector4Value.y, p.vector4Value.z, p.vector4Value.w);
                case SerializedPropertyType.Quaternion:
                    var q = p.quaternionValue;
                    return Join(q.x, q.y, q.z, q.w);
                case SerializedPropertyType.Rect:
                    var r = p.rectValue;
                    return Join(r.x, r.y, r.width, r.height);
                case SerializedPropertyType.Bounds:
                    var b = p.boundsValue;
                    return Join(b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z);
                case SerializedPropertyType.Vector2Int:
                    return Join(p.vector2IntValue.x, p.vector2IntValue.y);
                case SerializedPropertyType.Vector3Int:
                    return Join(p.vector3IntValue.x, p.vector3IntValue.y, p.vector3IntValue.z);
                default:
                    if (p.isArray)
                        return $"[{p.arraySize}]";
                    return "{...}";
            }
        }

        public static bool TryWrite(SerializedProperty p, string text, out string error)
        {
            error = string.Empty;
            text ??= string.Empty;
            try
            {
                switch (p.propertyType)
                {
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.LayerMask:
                    case SerializedPropertyType.ArraySize:
                    case SerializedPropertyType.Character:
                        if (!long.TryParse(text.Trim(), NumberStyles.Integer, Inv, out var l))
                            return Fail($"'{text}' is not an integer.", out error);
                        if (p.propertyType == SerializedPropertyType.ArraySize && l < 0)
                            return Fail("An array size cannot be negative.", out error);
                        if (p.propertyType == SerializedPropertyType.Integer)
                            p.longValue = l;
                        else
                            p.intValue = (int)l;
                        return true;
                    case SerializedPropertyType.Boolean:
                        var flag = text.Trim().ToLowerInvariant();
                        if (flag == "true" || flag == "1")
                            p.boolValue = true;
                        else if (flag == "false" || flag == "0")
                            p.boolValue = false;
                        else
                            return Fail($"'{text}' is not a boolean (true or false).", out error);
                        return true;
                    case SerializedPropertyType.Float:
                        if (!float.TryParse(text.Trim(), NumberStyles.Float, Inv, out var f))
                            return Fail($"'{text}' is not a number.", out error);
                        p.floatValue = f;
                        return true;
                    case SerializedPropertyType.String:
                        p.stringValue = text;
                        return true;
                    case SerializedPropertyType.Color:
                        if (ColorUtility.TryParseHtmlString(text.Trim(), out var color))
                        {
                            p.colorValue = color;
                            return true;
                        }
                        if (!TryNumbers(text, 4, 4, out var c) && !TryNumbers(text, 3, 3, out c))
                            return Fail($"'{text}' is not a color: use '#RRGGBB', '#RRGGBBAA' or 'r,g,b[,a]' (0-1).", out error);
                        p.colorValue = new Color(c[0], c[1], c[2], c.Length > 3 ? c[3] : 1f);
                        return true;
                    case SerializedPropertyType.Enum:
                        return WriteEnum(p, text.Trim(), out error);
                    case SerializedPropertyType.ObjectReference:
                        return WriteReference(p, text.Trim(), out error);
                    case SerializedPropertyType.Vector2:
                    case SerializedPropertyType.Vector2Int:
                        if (!TryNumbers(text, 2, 2, out var v2))
                            return Fail($"'{text}' is not a vector2: use 'x,y'.", out error);
                        if (p.propertyType == SerializedPropertyType.Vector2)
                            p.vector2Value = new Vector2(v2[0], v2[1]);
                        else
                            p.vector2IntValue = new Vector2Int((int)v2[0], (int)v2[1]);
                        return true;
                    case SerializedPropertyType.Vector3:
                    case SerializedPropertyType.Vector3Int:
                        if (!TryNumbers(text, 3, 3, out var v3))
                            return Fail($"'{text}' is not a vector3: use 'x,y,z'.", out error);
                        if (p.propertyType == SerializedPropertyType.Vector3)
                            p.vector3Value = new Vector3(v3[0], v3[1], v3[2]);
                        else
                            p.vector3IntValue = new Vector3Int((int)v3[0], (int)v3[1], (int)v3[2]);
                        return true;
                    case SerializedPropertyType.Vector4:
                        if (!TryNumbers(text, 4, 4, out var v4))
                            return Fail($"'{text}' is not a vector4: use 'x,y,z,w'.", out error);
                        p.vector4Value = new Vector4(v4[0], v4[1], v4[2], v4[3]);
                        return true;
                    case SerializedPropertyType.Quaternion:
                        if (TryNumbers(text, 4, 4, out var qv))
                            p.quaternionValue = new Quaternion(qv[0], qv[1], qv[2], qv[3]);
                        else if (TryNumbers(text, 3, 3, out var euler))
                            p.quaternionValue = Quaternion.Euler(euler[0], euler[1], euler[2]);
                        else
                            return Fail($"'{text}' is not a rotation: use 'x,y,z' (euler degrees) or 'x,y,z,w'.", out error);
                        return true;
                    case SerializedPropertyType.Rect:
                        if (!TryNumbers(text, 4, 4, out var rv))
                            return Fail($"'{text}' is not a rect: use 'x,y,width,height'.", out error);
                        p.rectValue = new Rect(rv[0], rv[1], rv[2], rv[3]);
                        return true;
                    case SerializedPropertyType.Bounds:
                        if (!TryNumbers(text, 6, 6, out var bv))
                            return Fail($"'{text}' is not a bounds: use 'cx,cy,cz,sx,sy,sz'.", out error);
                        p.boundsValue = new Bounds(new Vector3(bv[0], bv[1], bv[2]), new Vector3(bv[3], bv[4], bv[5]));
                        return true;
                    default:
                        if (p.isArray)
                            return Fail($"'{p.propertyPath}' is an array: resize it with '{p.propertyPath}.Array.size' and set elements as '{p.propertyPath}.Array.data[0]'.", out error);
                        return Fail($"'{p.propertyPath}' is a {p.propertyType} struct: set its fields one by one, for example '{p.propertyPath}.<field>'.", out error);
                }
            }
            catch (Exception e)
            {
                return Fail(e.Message, out error);
            }
        }

        /// <summary>Applies 'path=value' entries; returns one line per entry, throws nothing for a bad entry.</summary>
        public static List<string> Apply(SerializedObject so, IEnumerable<string>? entries, out int failed)
        {
            failed = 0;
            var lines = new List<string>();
            foreach (var entry in entries ?? Enumerable.Empty<string>())
            {
                var cut = (entry ?? string.Empty).IndexOf('=');
                if (cut <= 0)
                {
                    failed++;
                    lines.Add($"'{entry}': expected 'propertyPath=value'.");
                    continue;
                }
                var path = entry!.Substring(0, cut).Trim();
                var value = entry.Substring(cut + 1);
                var property = so.FindProperty(path);
                if (property == null)
                {
                    failed++;
                    lines.Add($"{path}: property not found.");
                    continue;
                }
                if (!TryWrite(property, value, out var error))
                {
                    failed++;
                    lines.Add($"{path}: {error}");
                    continue;
                }
                // an array resize must be applied before its elements can be found
                so.ApplyModifiedProperties();
                so.Update();
                lines.Add($"{path} = {Read(so.FindProperty(path) ?? property)}");
            }
            return lines;
        }

        /// <summary>Lists the visible properties under a path (or the whole object), up to maxDepth levels and limit entries.</summary>
        public static List<SerializedEntry> Dump(SerializedObject so, string? under, int maxDepth, int limit, out bool truncated)
        {
            truncated = false;
            var list = new List<SerializedEntry>();
            SerializedProperty cursor;
            var baseDepth = -1;
            SerializedProperty? end = null;
            if (string.IsNullOrWhiteSpace(under))
            {
                cursor = so.GetIterator();
            }
            else
            {
                var found = so.FindProperty(under!.Trim());
                if (found == null)
                    throw new ArgumentException($"Property '{under}' not found.");
                baseDepth = found.depth;
                cursor = found.Copy();
                end = found.GetEndProperty();
                if (found.propertyType != SerializedPropertyType.Generic && !found.isArray)
                {
                    list.Add(Entry(found));
                    return list;
                }
            }

            var enter = true;
            while (cursor.NextVisible(enter))
            {
                enter = true;
                if (end != null && SerializedProperty.EqualContents(cursor, end))
                    break;
                var relative = cursor.depth - baseDepth - 1;
                if (relative > maxDepth)
                {
                    enter = false;
                    continue;
                }
                if (cursor.propertyPath == "m_Script")
                    continue;
                if (list.Count >= limit)
                {
                    truncated = true;
                    break;
                }
                list.Add(Entry(cursor));
                // never walk inside an array that is deeper than allowed
                if (relative >= maxDepth)
                    enter = false;
            }
            return list;
        }

        public static SerializedEntry Entry(SerializedProperty p) => new SerializedEntry
        {
            Path = p.propertyPath,
            Type = p.isArray && p.propertyType != SerializedPropertyType.String ? "Array" : p.propertyType.ToString(),
            Value = Read(p),
        };

        static bool WriteEnum(SerializedProperty p, string text, out string error)
        {
            error = string.Empty;
            var names = p.enumNames;
            for (var i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], text, StringComparison.OrdinalIgnoreCase))
                {
                    p.enumValueIndex = i;
                    return true;
                }
            }
            if (int.TryParse(text, NumberStyles.Integer, Inv, out var index) && index >= 0 && index < names.Length)
            {
                p.enumValueIndex = index;
                return true;
            }
            return Fail($"'{text}' is not one of: {string.Join(", ", names)}.", out error);
        }

        static bool WriteReference(SerializedProperty p, string text, out string error)
        {
            error = string.Empty;
            if (text.Length == 0 || text.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                p.objectReferenceValue = null;
                return true;
            }
            var target = LoadAsset(text);
            if (target == null)
                return Fail($"No asset at '{text}'. Use a project path like 'Assets/Data/Item.asset', or 'null'.", out error);
            p.objectReferenceValue = target;
            return true;
        }

        public static UnityEngine.Object? LoadAsset(string path)
        {
            if (path.Length == 32 && path.All(Uri.IsHexDigit))
                path = AssetDatabase.GUIDToAssetPath(path);
            if (string.IsNullOrEmpty(path))
                return null;
            return AssetDatabase.LoadMainAssetAtPath(path);
        }

        static bool TryNumbers(string text, int min, int max, out float[] values)
        {
            var parts = text.Trim().Trim('(', ')', '[', ']').Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            values = new float[parts.Length];
            if (parts.Length < min || parts.Length > max)
                return false;
            for (var i = 0; i < parts.Length; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, Inv, out values[i]))
                    return false;
            return true;
        }

        static string Join(params float[] values) => string.Join(",", values.Select(v => v.ToString("R", Inv)));

        static string Join(params int[] values) => string.Join(",", values.Select(v => v.ToString(Inv)));

        static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
