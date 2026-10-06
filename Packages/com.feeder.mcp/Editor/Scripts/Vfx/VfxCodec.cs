#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Vfx
{
    // compact text encodings shared by vfx-read and vfx-apply:
    //   curve     "t:v,t:v"            keys, auto tangents; "t:v:in:out" keeps explicit tangents
    //   minmax    1.5 | [a,b] | "curve" | {"c":"curve","m":2} | {"min":"curve","max":"curve","m":2}
    //   color     "#RRGGBB" | "#RRGGBBAA" | "#RRGGBB*3" (hdr intensity)
    //   gradient  "#FFF@0,#F80@0.5|1@0,0@1" (color keys | alpha keys), suffix "~fixed" for fixed mode
    //   minmaxGr  "#hex" | ["#a","#b"] | "gradient" | ["grad","grad"] | {"rand":"gradient"}
    public static class VfxCodec
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Num(float v)
        {
            if (float.IsInfinity(v)) return v > 0 ? "inf" : "-inf";
            return Math.Round(v, 3).ToString("0.###", Inv);
        }

        public static JsonNode NumNode(float v)
        {
            if (float.IsInfinity(v) || float.IsNaN(v)) return JsonValue.Create(float.IsNaN(v) ? "0" : Num(v))!;
            var r = Math.Round((double)v, 3);
            if (Math.Abs(r - Math.Round(r)) < 1e-9 && Math.Abs(r) < 1e7) return JsonValue.Create((long)Math.Round(r));
            return JsonValue.Create(decimal.Parse(r.ToString("0.###", Inv), Inv));
        }

        public static float ParseFloat(string s)
        {
            s = s.Trim();
            if (s == "inf") return float.PositiveInfinity;
            if (s == "-inf") return float.NegativeInfinity;
            return float.Parse(s, NumberStyles.Float, Inv);
        }

        public static float ToFloat(JsonNode? n, string field)
        {
            if (n is JsonValue v)
            {
                if (v.TryGetValue<double>(out var d)) return (float)d;
                if (v.TryGetValue<decimal>(out var m)) return (float)m;
                if (v.TryGetValue<long>(out var l)) return l;
                if (v.TryGetValue<int>(out var i)) return i;
                if (v.TryGetValue<float>(out var f)) return f;
                if (v.TryGetValue<string>(out var s)) return ParseFloat(s);
            }
            throw new FormatException($"'{field}' expects a number, got {n?.ToJsonString() ?? "null"}");
        }

        #region Color

        public static string EncodeColor(Color c)
        {
            var max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            var intensity = 1f;
            if (max > 1f)
            {
                intensity = max;
                c = new Color(c.r / max, c.g / max, c.b / max, c.a);
            }
            var hex = c.a >= 0.999f ? ColorUtility.ToHtmlStringRGB(c) : ColorUtility.ToHtmlStringRGBA(c);
            return intensity > 1f ? $"#{hex}*{Num(intensity)}" : "#" + hex;
        }

        public static Color DecodeColor(string s, string field)
        {
            s = s.Trim();
            var intensity = 1f;
            var star = s.IndexOf('*');
            if (star >= 0)
            {
                intensity = ParseFloat(s.Substring(star + 1));
                s = s.Substring(0, star);
            }
            if (!s.StartsWith("#")) s = "#" + s;
            if (!ColorUtility.TryParseHtmlString(s, out var c))
                throw new FormatException($"'{field}' has invalid color '{s}'");
            return new Color(c.r * intensity, c.g * intensity, c.b * intensity, c.a);
        }

        #endregion

        #region Curve

        public static string EncodeCurve(AnimationCurve curve)
        {
            var sb = new StringBuilder();
            var keys = curve.keys;
            for (var i = 0; i < keys.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var k = keys[i];
                sb.Append(Num(k.time)).Append(':').Append(Num(k.value));
                var auto = AnimationUtility.GetKeyLeftTangentMode(curve, i) is AnimationUtility.TangentMode.ClampedAuto or AnimationUtility.TangentMode.Auto
                           && AnimationUtility.GetKeyRightTangentMode(curve, i) is AnimationUtility.TangentMode.ClampedAuto or AnimationUtility.TangentMode.Auto;
                if (!auto) sb.Append(':').Append(Num(k.inTangent)).Append(':').Append(Num(k.outTangent));
            }
            return sb.ToString();
        }

        public static AnimationCurve DecodeCurve(string s, string field)
        {
            var curve = new AnimationCurve();
            var explicitTangents = new List<bool>();
            foreach (var part in s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = part.Split(':');
                if (p.Length != 2 && p.Length != 4)
                    throw new FormatException($"'{field}' curve key '{part}' must be t:v or t:v:in:out");
                var key = new Keyframe(ParseFloat(p[0]), ParseFloat(p[1]));
                if (p.Length == 4)
                {
                    key.inTangent = ParseFloat(p[2]);
                    key.outTangent = ParseFloat(p[3]);
                }
                curve.AddKey(key);
                explicitTangents.Add(p.Length == 4);
            }
            if (curve.length == 0) throw new FormatException($"'{field}' curve has no keys");
            for (var i = 0; i < curve.length; i++)
            {
                var mode = explicitTangents.Count > i && explicitTangents[i] ? AnimationUtility.TangentMode.Free : AnimationUtility.TangentMode.ClampedAuto;
                AnimationUtility.SetKeyLeftTangentMode(curve, i, mode);
                AnimationUtility.SetKeyRightTangentMode(curve, i, mode);
            }
            return curve;
        }

        public static bool LooksLikeCurve(string s) => s.Contains(':') && !s.Contains('#');

        #endregion

        #region MinMaxCurve

        public static JsonNode EncodeMinMax(ParticleSystem.MinMaxCurve c)
        {
            switch (c.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return NumNode(c.constant);
                case ParticleSystemCurveMode.TwoConstants:
                    return new JsonArray(NumNode(c.constantMin), NumNode(c.constantMax));
                case ParticleSystemCurveMode.Curve:
                {
                    var curve = EncodeCurve(c.curve);
                    if (Mathf.Approximately(c.curveMultiplier, 1f)) return JsonValue.Create(curve)!;
                    return new JsonObject { ["c"] = curve, ["m"] = NumNode(c.curveMultiplier) };
                }
                case ParticleSystemCurveMode.TwoCurves:
                    return new JsonObject
                    {
                        ["min"] = EncodeCurve(c.curveMin),
                        ["max"] = EncodeCurve(c.curveMax),
                        ["m"] = NumNode(c.curveMultiplier)
                    };
                default:
                    throw new ArgumentOutOfRangeException(nameof(c.mode), c.mode, "unknown curve mode");
            }
        }

        public static ParticleSystem.MinMaxCurve DecodeMinMax(JsonNode? n, string field)
        {
            switch (n)
            {
                case JsonArray arr when arr.Count == 2:
                    return new ParticleSystem.MinMaxCurve(ToFloat(arr[0], field), ToFloat(arr[1], field));
                case JsonObject o when o.ContainsKey("min"):
                {
                    var m = o.ContainsKey("m") ? ToFloat(o["m"], field) : 1f;
                    return new ParticleSystem.MinMaxCurve(m, DecodeCurve((string)o["min"]!, field), DecodeCurve((string)o["max"]!, field));
                }
                case JsonObject o when o.ContainsKey("c"):
                {
                    var m = o.ContainsKey("m") ? ToFloat(o["m"], field) : 1f;
                    return new ParticleSystem.MinMaxCurve(m, DecodeCurve((string)o["c"]!, field));
                }
                case JsonValue v when v.TryGetValue<string>(out var s) && LooksLikeCurve(s):
                    return new ParticleSystem.MinMaxCurve(1f, DecodeCurve(s, field));
                default:
                    return new ParticleSystem.MinMaxCurve(ToFloat(n, field));
            }
        }

        #endregion

        #region Gradient

        public static string EncodeGradient(Gradient g)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(",", g.colorKeys.Select(k => EncodeColor(new Color(k.color.r, k.color.g, k.color.b, 1f)) + "@" + Num(k.time))));
            var alpha = g.alphaKeys;
            var opaque = alpha.All(a => a.alpha >= 0.999f);
            if (!opaque) sb.Append('|').Append(string.Join(",", alpha.Select(a => Num(a.alpha) + "@" + Num(a.time))));
            if (g.mode == GradientMode.Fixed) sb.Append("~fixed");
            return sb.ToString();
        }

        public static Gradient DecodeGradient(string s, string field)
        {
            var g = new Gradient();
            if (s.EndsWith("~fixed"))
            {
                g.mode = GradientMode.Fixed;
                s = s.Substring(0, s.Length - 6);
            }
            var halves = s.Split('|');
            var colorKeys = halves[0].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(part =>
            {
                var at = part.LastIndexOf('@');
                if (at < 0) throw new FormatException($"'{field}' gradient color key '{part}' must be #hex@t");
                return new GradientColorKey(DecodeColor(part.Substring(0, at), field), ParseFloat(part.Substring(at + 1)));
            }).ToArray();
            var alphaKeys = halves.Length > 1
                ? halves[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(part =>
                {
                    var at = part.LastIndexOf('@');
                    if (at < 0) throw new FormatException($"'{field}' gradient alpha key '{part}' must be a@t");
                    return new GradientAlphaKey(ParseFloat(part.Substring(0, at)), ParseFloat(part.Substring(at + 1)));
                }).ToArray()
                : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };
            if (colorKeys.Length == 0) throw new FormatException($"'{field}' gradient has no color keys");
            if (colorKeys.Length > 8 || alphaKeys.Length > 8) throw new FormatException($"'{field}' gradient allows at most 8 color and 8 alpha keys");
            g.SetKeys(colorKeys, alphaKeys);
            return g;
        }

        static bool LooksLikeGradient(string s) => s.Contains('@');

        public static JsonNode EncodeMinMaxGradient(ParticleSystem.MinMaxGradient g)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return JsonValue.Create(EncodeColor(g.color))!;
                case ParticleSystemGradientMode.TwoColors:
                    return new JsonArray(EncodeColor(g.colorMin), EncodeColor(g.colorMax));
                case ParticleSystemGradientMode.Gradient:
                    return JsonValue.Create(EncodeGradient(g.gradient))!;
                case ParticleSystemGradientMode.TwoGradients:
                    return new JsonArray(EncodeGradient(g.gradientMin), EncodeGradient(g.gradientMax));
                case ParticleSystemGradientMode.RandomColor:
                    return new JsonObject { ["rand"] = EncodeGradient(g.gradient) };
                default:
                    throw new ArgumentOutOfRangeException(nameof(g.mode), g.mode, "unknown gradient mode");
            }
        }

        public static ParticleSystem.MinMaxGradient DecodeMinMaxGradient(JsonNode? n, string field)
        {
            switch (n)
            {
                case JsonArray arr when arr.Count == 2:
                {
                    var a = (string)arr[0]!;
                    var b = (string)arr[1]!;
                    return LooksLikeGradient(a)
                        ? new ParticleSystem.MinMaxGradient(DecodeGradient(a, field), DecodeGradient(b, field))
                        : new ParticleSystem.MinMaxGradient(DecodeColor(a, field), DecodeColor(b, field));
                }
                case JsonObject o when o.ContainsKey("rand"):
                    return new ParticleSystem.MinMaxGradient(DecodeGradient((string)o["rand"]!, field)) { mode = ParticleSystemGradientMode.RandomColor };
                case JsonValue v when v.TryGetValue<string>(out var s):
                    return LooksLikeGradient(s)
                        ? new ParticleSystem.MinMaxGradient(DecodeGradient(s, field))
                        : new ParticleSystem.MinMaxGradient(DecodeColor(s, field));
                default:
                    throw new FormatException($"'{field}' expects a color/gradient, got {n?.ToJsonString() ?? "null"}");
            }
        }

        #endregion

        #region Generic

        // returns null when the type is not representable; callers skip those members
        public static JsonNode? Encode(object? value, Type type)
        {
            if (value == null) return null;
            if (type == typeof(float)) return NumNode((float)value);
            if (type == typeof(int)) return JsonValue.Create((int)value);
            if (type == typeof(uint)) return JsonValue.Create((uint)value);
            if (type == typeof(bool)) return JsonValue.Create((bool)value);
            if (type == typeof(string)) return JsonValue.Create((string)value);
            if (type.IsEnum) return JsonValue.Create(value.ToString().Replace(", ", "|"));
            if (type == typeof(Vector2)) { var v = (Vector2)value; return new JsonArray(NumNode(v.x), NumNode(v.y)); }
            if (type == typeof(Vector3)) { var v = (Vector3)value; return new JsonArray(NumNode(v.x), NumNode(v.y), NumNode(v.z)); }
            if (type == typeof(Color)) return JsonValue.Create(EncodeColor((Color)value));
            if (type == typeof(LayerMask)) return JsonValue.Create(((LayerMask)value).value);
            if (type == typeof(ParticleSystem.MinMaxCurve)) return EncodeMinMax((ParticleSystem.MinMaxCurve)value);
            if (type == typeof(ParticleSystem.MinMaxGradient)) return EncodeMinMaxGradient((ParticleSystem.MinMaxGradient)value);
            if (type == typeof(Gradient)) return JsonValue.Create(EncodeGradient((Gradient)value));
            if (type == typeof(AnimationCurve)) return JsonValue.Create(EncodeCurve((AnimationCurve)value));
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                var obj = (UnityEngine.Object)value;
                if (obj == null) return null;
                var path = AssetDatabase.GetAssetPath(obj);
                return string.IsNullOrEmpty(path) ? null : JsonValue.Create(path);
            }
            return null;
        }

        public static bool IsSupported(Type type) =>
            type == typeof(float) || type == typeof(int) || type == typeof(uint) || type == typeof(bool) || type == typeof(string) || type.IsEnum
            || type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Color) || type == typeof(LayerMask)
            || type == typeof(ParticleSystem.MinMaxCurve) || type == typeof(ParticleSystem.MinMaxGradient)
            || type == typeof(Gradient) || type == typeof(AnimationCurve)
            || typeof(Mesh).IsAssignableFrom(type) || typeof(Material).IsAssignableFrom(type) || typeof(Texture).IsAssignableFrom(type)
            || typeof(Sprite).IsAssignableFrom(type);

        public static object? Decode(JsonNode? n, Type type, string field)
        {
            if (type == typeof(float)) return ToFloat(n, field);
            if (type == typeof(int)) return (int)Math.Round(ToFloat(n, field));
            if (type == typeof(uint)) return (uint)Math.Round(ToFloat(n, field));
            if (type == typeof(bool))
            {
                if (n is JsonValue bv && bv.TryGetValue<bool>(out var b)) return b;
                throw new FormatException($"'{field}' expects true/false, got {n?.ToJsonString() ?? "null"}");
            }
            if (type == typeof(string)) return (string?)n;
            if (type.IsEnum)
            {
                var names = Enum.GetNames(type);
                long bits = 0;
                foreach (var part in ((string?)n ?? "").Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var match = names.FirstOrDefault(x => string.Equals(x, part.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (match == null) throw new FormatException($"'{field}' unknown value '{part.Trim()}'. Valid: {string.Join("|", names)}");
                    bits |= Convert.ToInt64(Enum.Parse(type, match));
                }
                return Enum.ToObject(type, bits);
            }
            if (type == typeof(Vector2))
            {
                var a = n as JsonArray ?? throw new FormatException($"'{field}' expects [x,y]");
                return new Vector2(ToFloat(a[0], field), ToFloat(a[1], field));
            }
            if (type == typeof(Vector3))
            {
                if (n is JsonValue) { var f = ToFloat(n, field); return new Vector3(f, f, f); }
                var a = n as JsonArray ?? throw new FormatException($"'{field}' expects [x,y,z]");
                return new Vector3(ToFloat(a[0], field), ToFloat(a[1], field), ToFloat(a[2], field));
            }
            if (type == typeof(Color)) return DecodeColor((string?)n ?? "", field);
            if (type == typeof(LayerMask)) return (LayerMask)(int)ToFloat(n, field);
            if (type == typeof(ParticleSystem.MinMaxCurve)) return DecodeMinMax(n, field);
            if (type == typeof(ParticleSystem.MinMaxGradient)) return DecodeMinMaxGradient(n, field);
            if (type == typeof(Gradient)) return DecodeGradient((string?)n ?? "", field);
            if (type == typeof(AnimationCurve)) return DecodeCurve((string?)n ?? "", field);
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                if (n == null) return null;
                var path = (string?)n ?? "";
                var asset = AssetDatabase.LoadAssetAtPath(path, type);
                if (asset == null) throw new FormatException($"'{field}' asset not found at '{path}' as {type.Name}");
                return asset;
            }
            throw new FormatException($"'{field}' has unsupported type {type.Name}");
        }

        #endregion
    }
}
