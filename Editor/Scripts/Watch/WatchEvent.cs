#nullable enable
using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Feeder.MCP.Editor.Watch
{
    // one line of events.jsonl; only non-default fields are written, JsonUtility reads the rest as defaults
    [Serializable]
    internal sealed class WatchEvent
    {
        public const string Play = "play";
        public const string Key = "key";
        public const string Click = "click";
        public const string Drag = "drag";
        public const string Pos = "pos";
        public const string Player = "player";
        public const string Scene = "scene";
        public const string Log = "log";
        public const string LogCount = "logn";
        public const string Marker = "marker";
        public const string TimeScale = "timescale";
        public const string Frame = "frame";
        public const string End = "end";

        public const int FlagGrounded = 1;
        public const int FlagMoveInput = 2;

        public double t;
        public string k = string.Empty;
        public string s = string.Empty;
        public string d = string.Empty;
        public Vector3 p;
        public Vector2 a;
        public Vector2 b;
        public float r;
        public float v;
        public int n;
        public string f = string.Empty;

        public string ToJson()
        {
            var sb = new StringBuilder(96);
            sb.Append("{\"t\":").Append(Num(t)).Append(",\"k\":");
            Str(sb, k);
            if (s.Length > 0) { sb.Append(",\"s\":"); Str(sb, s); }
            if (d.Length > 0) { sb.Append(",\"d\":"); Str(sb, d); }
            if (p != Vector3.zero) sb.Append(",\"p\":{\"x\":").Append(Num(p.x)).Append(",\"y\":").Append(Num(p.y)).Append(",\"z\":").Append(Num(p.z)).Append('}');
            if (a != Vector2.zero) sb.Append(",\"a\":{\"x\":").Append(Num(a.x)).Append(",\"y\":").Append(Num(a.y)).Append('}');
            if (b != Vector2.zero) sb.Append(",\"b\":{\"x\":").Append(Num(b.x)).Append(",\"y\":").Append(Num(b.y)).Append('}');
            if (r != 0f) sb.Append(",\"r\":").Append(Num(r));
            if (v != 0f) sb.Append(",\"v\":").Append(Num(v));
            if (n != 0) sb.Append(",\"n\":").Append(n.ToString(CultureInfo.InvariantCulture));
            if (f.Length > 0) { sb.Append(",\"f\":"); Str(sb, f); }
            return sb.Append('}').ToString();
        }

        public static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        static void Str(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
