#nullable enable
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Vfx
{
    static class VfxUi
    {
        public static readonly Color Backdrop = new Color(0.047f, 0.047f, 0.063f);
        public static readonly Color Accent = new Color(0.31f, 0.56f, 0.88f);
        public static readonly Color Link = new Color(0.61f, 0.76f, 1f);
        public static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);
        public static readonly Color Primary = new Color(0.5f, 0.7f, 1f);

        static readonly GUIContent Temp = new GUIContent();
        static GUIStyle? mini;
        static GUIStyle? miniRight;
        static GUIStyle? name;
        static GUIStyle? title;

        static bool Pro => EditorGUIUtility.isProSkin;

        public static Color Bar => Gray(0.235f, 0.80f);
        public static Color Header => Gray(0.20f, 0.76f);
        public static Color Line => Gray(0.137f, 0.55f);
        public static Color Hover => Gray(0.267f, 0.70f);
        public static Color GroupBar => Gray(0.243f, 0.74f);
        public static Color Selected => Pro ? new Color(0.173f, 0.365f, 0.529f) : new Color(0.227f, 0.447f, 0.69f);

        public static GUIStyle Mini => mini ??= new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip };
        public static GUIStyle MiniRight => miniRight ??= new GUIStyle(Mini) { alignment = TextAnchor.MiddleRight };
        public static GUIStyle Name => name ??= new GUIStyle(EditorStyles.label) { clipping = TextClipping.Clip };
        public static GUIStyle Title => title ??= new GUIStyle(EditorStyles.boldLabel) { clipping = TextClipping.Clip };

        static Color Gray(float pro, float light) => Pro ? new Color(pro, pro, pro) : new Color(light, light, light);

        public static GUIContent Content(string text, string tooltip = "")
        {
            Temp.text = text;
            Temp.tooltip = tooltip;
            Temp.image = null;
            return Temp;
        }

        public static Texture? Icon(string iconName) => EditorGUIUtility.IconContent(iconName).image;

        public static void Fill(Rect r, Color c) => EditorGUI.DrawRect(r, c);

        public static void BottomLine(Rect r) => EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), Line);

        public static void RightLine(Rect r) => EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), Line);

        public static void Outline(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1f, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), c);
        }

        public static void Row(Rect r, bool selected)
        {
            if (selected) Fill(r, Selected);
            else if (Event.current.type == EventType.Repaint && r.Contains(Event.current.mousePosition)) Fill(r, Hover);
        }

        public static void Label(Rect r, string text, GUIStyle style, bool dim = false)
        {
            var previous = GUI.contentColor;
            if (dim) GUI.contentColor = previous * Dim;
            GUI.Label(r, Content(text), style);
            GUI.contentColor = previous;
        }

        public static bool Click(Rect r, out int clicks)
        {
            var e = Event.current;
            clicks = 0;
            if (e.type != EventType.MouseDown || e.button != 0 || !r.Contains(e.mousePosition)) return false;
            clicks = e.clickCount;
            e.Use();
            return true;
        }

        public static bool ContextClick(Rect r)
        {
            var e = Event.current;
            if (e.type != EventType.ContextClick || !r.Contains(e.mousePosition)) return false;
            e.Use();
            return true;
        }
    }
}
