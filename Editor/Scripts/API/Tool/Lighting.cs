#nullable enable
using System;
using System.Globalization;
using Feeder.ToolHost;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_Lighting
    {
    }

    internal static class RenderingParse
    {
        public static string Color(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);

        public static bool TryColor(string? text, out Color color)
        {
            color = default;
            var s = (text ?? string.Empty).Trim();
            if (s.Length == 0)
                return false;
            if (ColorUtility.TryParseHtmlString(s, out color))
                return true;
            var parts = s.Trim('(', ')').Split(',');
            if (parts.Length < 3 || parts.Length > 4)
                return false;
            var v = new float[4] { 0, 0, 0, 1 };
            for (var i = 0; i < parts.Length; i++)
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]))
                    return false;
            color = new Color(v[0], v[1], v[2], v[3]);
            return true;
        }

        public static Color RequireColor(string name, string text)
            => TryColor(text, out var c) ? c : throw new ArgumentException($"{name}: '{text}' is not a color. Use '#RRGGBB', '#RRGGBBAA', a name like 'red', or 'r,g,b[,a]' with 0-1 floats.");

        public static T RequireEnum<T>(string name, string text) where T : struct, Enum
            => Enum.TryParse<T>(text?.Trim(), true, out var v) && Enum.IsDefined(typeof(T), v)
                ? v
                : throw new ArgumentException($"{name}: '{text}' is not valid. Allowed: {string.Join(", ", Enum.GetNames(typeof(T)))}.");

        public static float[] Floats(string text, int count)
        {
            var parts = text.Trim().Trim('(', ')', '[', ']').Split(',');
            if (parts.Length != count)
                throw new ArgumentException($"Expected {count} comma separated numbers, got '{text}'.");
            var r = new float[count];
            for (var i = 0; i < count; i++)
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out r[i]))
                    throw new ArgumentException($"'{parts[i].Trim()}' is not a number.");
            return r;
        }

        public static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
