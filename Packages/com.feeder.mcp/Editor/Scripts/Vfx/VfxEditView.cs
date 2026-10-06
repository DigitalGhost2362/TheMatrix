#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MinMaxCurve = UnityEngine.ParticleSystem.MinMaxCurve;
using MinMaxGradient = UnityEngine.ParticleSystem.MinMaxGradient;

namespace Feeder.MCP.Editor.Vfx
{
    sealed class VfxEditView
    {
        const float BarHeight = 28f;
        const float PaneHeader = 22f;
        const float RowHeight = 22f;
        const float ParamHeight = 24f;
        const float GroupHeight = 22f;
        const float SoloHeight = 260f;
        const float HintHeight = 52f;
        const float ControlsHeight = 28f;
        const float ScrollbarWidth = 15f;
        const string EmittersGroup = "Emitter";

        static readonly string[] SpeedLabels = { "0.25×", "0.5×", "1×" };
        static readonly float[] SpeedValues = { 0.25f, 0.5f, 1f };
        static readonly List<ParamGroup> EmitterGroups = VfxParams.Groups(VfxParams.Emitter);
        static readonly List<ParamGroup> RootGroups = VfxParams.Groups(VfxParams.Root);
        static readonly Color BadgeFill = new Color(0.12f, 0.25f, 0.42f);

        static GUIStyle? centeredMini;

        readonly HashSet<string> collapsedNodes = new HashSet<string>();
        readonly HashSet<string> collapsedGroups = new HashSet<string>();
        GUIStyle? wrapped;
        Vector2 hierarchyScroll;
        Vector2 middleScroll;

        public Action? OnBack;
        public Action? OnSave;
        public Action? OnRevert;
        public Action? OnPlay;
        public Action<string>? OnToast;

        static GUIStyle CenteredMini => centeredMini ??= new GUIStyle(VfxUi.Mini) { alignment = TextAnchor.MiddleCenter };

        GUIStyle Wrapped => wrapped ??= new GUIStyle(EditorStyles.wordWrappedMiniLabel) { alignment = TextAnchor.MiddleCenter };

        static string Number(float value, string format = "0.00") => value.ToString(format, CultureInfo.InvariantCulture);

        public void Draw(Rect area, VfxEditSession s)
        {
            var bar = new Rect(area.x, area.y, area.width, BarHeight);
            DrawBar(bar, s);
            var top = bar.yMax;
            if (s.Blocked != null)
            {
                var warning = new Rect(area.x, top, area.width, 28f);
                EditorGUI.HelpBox(warning, s.Blocked, MessageType.Warning);
                top = warning.yMax;
            }
            var body = new Rect(area.x, top, area.width, area.yMax - top);
            var previewWidth = Mathf.Clamp(body.width * 0.34f, 250f, 380f);
            var hierarchyWidth = Mathf.Clamp(body.width * 0.2f, 160f, 200f);
            var hierarchy = new Rect(body.x, body.y, hierarchyWidth, body.height);
            var preview = new Rect(body.xMax - previewWidth, body.y, previewWidth, body.height);
            var middle = new Rect(hierarchy.xMax, body.y, preview.x - hierarchy.xMax, body.height);
            if (Event.current.type == EventType.Repaint) s.Preview.Simulate(s.Clock);
            DrawHierarchy(hierarchy, s);
            DrawMiddle(middle, s);
            DrawPreview(preview, s);
            VfxUi.RightLine(hierarchy);
            VfxUi.RightLine(middle);
        }

        #region Bar

        void DrawBar(Rect bar, VfxEditSession s)
        {
            VfxUi.Fill(bar, VfxUi.Bar);
            VfxUi.BottomLine(bar);
            var x = bar.x + 6f;
            if (GUI.Button(new Rect(x, bar.y + 4f, 84f, 20f), "◂ Thư viện", EditorStyles.miniButton)) OnBack?.Invoke();
            x += 94f;
            var folder = s.PrefabPath.Substring(0, s.PrefabPath.LastIndexOf('/')) + " /";
            var folderWidth = VfxUi.Mini.CalcSize(VfxUi.Content(folder)).x;
            VfxUi.Label(new Rect(x, bar.y + 4f, folderWidth, 20f), folder, VfxUi.Mini, true);
            x += folderWidth + 4f;
            VfxUi.Label(new Rect(x, bar.y + 4f, 260f, 20f), s.Name, VfxUi.Title);

            var right = bar.xMax - 6f;
            var ping = new Rect(right - 50f, bar.y + 4f, 50f, 20f);
            if (GUI.Button(ping, "Ping", EditorStyles.miniButton)) EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(s.PrefabPath));
            var save = new Rect(ping.x - 56f, ping.y, 50f, 20f);
            var previousColor = GUI.backgroundColor;
            GUI.backgroundColor = VfxUi.Primary;
            using (new EditorGUI.DisabledScope(s.Changes == 0 || s.Blocked != null))
                if (GUI.Button(save, "Lưu", EditorStyles.miniButton)) OnSave?.Invoke();
            GUI.backgroundColor = previousColor;
            var undo = new Rect(save.x - 80f, ping.y, 74f, 20f);
            using (new EditorGUI.DisabledScope(s.Changes == 0))
                if (GUI.Button(undo, "Hoàn tác", EditorStyles.miniButton)) OnRevert?.Invoke();
            DrawBadge(new Rect(undo.x - 166f, ping.y, 160f, 20f), s);
        }

        static void DrawBadge(Rect r, VfxEditSession s)
        {
            if (s.Changes == 0)
            {
                VfxUi.Label(r, $"v{s.Version} · đã lưu", VfxUi.MiniRight, true);
                return;
            }
            var text = $"{s.Changes} thay đổi chưa lưu";
            var width = VfxUi.Mini.CalcSize(VfxUi.Content(text)).x + 14f;
            var pill = new Rect(r.xMax - width, r.y + 2f, width, 16f);
            VfxUi.Fill(pill, BadgeFill);
            VfxUi.Outline(pill, VfxUi.Accent);
            var previous = GUI.contentColor;
            GUI.contentColor = new Color(0.81f, 0.89f, 1f);
            GUI.Label(pill, VfxUi.Content(text), CenteredMini);
            GUI.contentColor = previous;
        }

        #endregion

        #region Hierarchy

        void DrawHierarchy(Rect r, VfxEditSession s)
        {
            DrawPaneHeader(new Rect(r.x, r.y, r.width, PaneHeader), "Hierarchy");
            var list = new Rect(r.x, r.y + PaneHeader, r.width, r.height - PaneHeader);
            var visible = VisibleNodes(s);
            var view = new Rect(0f, 0f, list.width - ScrollbarWidth, visible.Count * RowHeight);
            if (view.height <= list.height) view.width = list.width;
            hierarchyScroll = GUI.BeginScrollView(list, hierarchyScroll, view);
            for (var i = 0; i < visible.Count; i++) DrawNode(new Rect(0f, i * RowHeight, view.width, RowHeight), visible[i], s);
            GUI.EndScrollView();
        }

        List<VfxEditSession.Node> VisibleNodes(VfxEditSession s)
        {
            var visible = new List<VfxEditSession.Node>();
            var hiddenBelow = int.MaxValue;
            foreach (var node in s.Nodes)
            {
                if (node.Depth > hiddenBelow) continue;
                hiddenBelow = int.MaxValue;
                visible.Add(node);
                if (collapsedNodes.Contains(node.Path)) hiddenBelow = node.Depth;
            }
            return visible;
        }

        void DrawNode(Rect row, VfxEditSession.Node node, VfxEditSession s)
        {
            var isRoot = node.Depth == 0;
            var selectable = isRoot || node.System != null;
            VfxUi.Row(row, selectable && s.Selected == node.Path);
            var x = 6f + node.Depth * 14f;
            if (node.HasChildren)
            {
                var expanded = !collapsedNodes.Contains(node.Path);
                if (EditorGUI.Foldout(new Rect(x, row.y + 3f, 14f, 16f), expanded, GUIContent.none) != expanded) Toggle(collapsedNodes, node.Path);
            }
            x += 14f;
            if (node.System != null)
            {
                var shown = !s.Hidden.Contains(node.Path);
                if (GUI.Toggle(new Rect(x, row.y + 3f, 16f, 16f), shown, GUIContent.none) != shown) Toggle(s.Hidden, node.Path);
                x += 18f;
            }
            DrawNodeIcon(new Rect(x, row.y + 3f, 16f, 16f), node);
            VfxUi.Label(new Rect(x + 20f, row.y, row.width - x - 24f, RowHeight), isRoot ? s.Name : node.Transform.name, VfxUi.Name, !selectable);
            if (selectable && VfxUi.Click(row, out _)) s.Selected = node.Path;
            if (!isRoot && VfxUi.ContextClick(row)) ShowNodeMenu(node, s);
        }

        static void DrawNodeIcon(Rect r, VfxEditSession.Node node)
        {
            var icon = VfxUi.Icon(node.Depth == 0 ? "Prefab Icon" : node.System != null ? "ParticleSystem Icon" : "GameObject Icon");
            if (icon == null) return;
            var previous = GUI.color;
            if (node.System != null) GUI.color = StartTint(node.System);
            else if (node.Depth > 0) GUI.color = previous * VfxUi.Dim;
            GUI.DrawTexture(r, icon, ScaleMode.ScaleToFit);
            GUI.color = previous;
        }

        static Color StartTint(ParticleSystem ps)
        {
            var color = ps.main.startColor;
            return color.mode == ParticleSystemGradientMode.Color ? new Color(color.color.r, color.color.g, color.color.b, 1f) : Color.white;
        }

        static void Toggle(HashSet<string> set, string key)
        {
            if (!set.Remove(key)) set.Add(key);
        }

        void ShowNodeMenu(VfxEditSession.Node node, VfxEditSession s)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Copy Path"), false, () => CopyPath(node, s));
            menu.ShowAsContext();
        }

        void CopyPath(VfxEditSession.Node node, VfxEditSession s)
        {
            var path = s.PathOf(node);
            EditorGUIUtility.systemCopyBuffer = path;
            OnToast?.Invoke("Đã copy: " + path);
        }

        #endregion

        #region Middle

        void DrawMiddle(Rect r, VfxEditSession s)
        {
            var node = s.Nodes.FirstOrDefault(n => n.Path == s.Selected) ?? s.Nodes[0];
            var isRoot = node.Depth == 0;
            DrawMiddleHeader(new Rect(r.x, r.y, r.width, PaneHeader), node, s);
            var area = new Rect(r.x, r.y + PaneHeader, r.width, r.height - PaneHeader);
            var height = isRoot ? RootHeight(s) : EmitterHeight();
            var view = new Rect(0f, 0f, area.width - ScrollbarWidth, height);
            if (height <= area.height) view.width = area.width;
            middleScroll = GUI.BeginScrollView(area, middleScroll, view);
            var y = 0f;
            if (isRoot) DrawRootBody(view.width, s, ref y);
            else DrawEmitterBody(view.width, node, s, ref y);
            GUI.EndScrollView();
        }

        void DrawMiddleHeader(Rect head, VfxEditSession.Node node, VfxEditSession s)
        {
            DrawPaneHeader(head, "");
            var icon = VfxUi.Icon(node.Depth == 0 ? "Prefab Icon" : "ParticleSystem Icon");
            if (icon != null) GUI.DrawTexture(new Rect(head.x + 8f, head.y + 3f, 16f, 16f), icon, ScaleMode.ScaleToFit);
            VfxUi.Label(new Rect(head.x + 28f, head.y, head.width - 120f, head.height), node.Depth == 0 ? s.Name : node.Transform.name, VfxUi.Title);
            VfxUi.Label(new Rect(head.xMax - 100f, head.y, 92f, head.height), node.Depth == 0 ? "root" : "ParticleSystem", VfxUi.MiniRight, true);
        }

        float RootHeight(VfxEditSession s)
        {
            var emitters = s.Nodes.Count(n => n.System != null);
            var height = HintHeight + GroupHeight + GroupHeight;
            if (!collapsedGroups.Contains(RootGroups[0].Name)) height += RootGroups[0].Count * ParamHeight;
            if (!collapsedGroups.Contains(EmittersGroup)) height += emitters * ParamHeight;
            return height;
        }

        float EmitterHeight()
        {
            var height = SoloHeight;
            foreach (var group in EmitterGroups) height += GroupHeight + (collapsedGroups.Contains(group.Name) ? 0 : group.Count * ParamHeight);
            return height;
        }

        void DrawRootBody(float width, VfxEditSession s, ref float y)
        {
            VfxUi.Label(new Rect(14f, y, width - 28f, HintHeight), "Chọn 1 emitter con trong Hierarchy để xem preview riêng và chỉnh thông số của nó.", Wrapped, true);
            y += HintHeight;
            DrawParams(width, s, s.Root, VfxParams.Root, RootGroups[0], ref y);
            var emitters = s.Nodes.Where(n => n.System != null).ToList();
            var open = DrawGroupHeader(new Rect(0f, y, width, GroupHeight), EmittersGroup, $"{EmittersGroup} ({emitters.Count})");
            y += GroupHeight;
            if (!open) return;
            foreach (var node in emitters)
            {
                DrawSummary(new Rect(0f, y, width, ParamHeight), node, s);
                y += ParamHeight;
            }
        }

        static void DrawSummary(Rect row, VfxEditSession.Node node, VfxEditSession s)
        {
            VfxUi.Row(row, false);
            var swatch = new Rect(row.x + 10f, row.y + 8f, 8f, 8f);
            VfxUi.Fill(swatch, StartTint(node.System!));
            var nameWidth = Mathf.Clamp(row.width * 0.32f, 80f, 130f);
            VfxUi.Label(new Rect(row.x + 24f, row.y, nameWidth, row.height), node.Transform.name, VfxUi.Name);
            VfxUi.Label(new Rect(row.x + 28f + nameWidth, row.y, row.width - 28f - nameWidth - 50f, row.height), Describe(node.System!), VfxUi.Mini, true);
            VfxUi.Label(new Rect(row.xMax - 50f, row.y, 44f, row.height), s.Hidden.Contains(node.Path) ? "tắt" : "bật", VfxUi.MiniRight, true);
        }

        static string Describe(ParticleSystem ps)
        {
            var emission = ps.emission;
            var count = emission.burstCount > 0 ? Curve(emission.GetBurst(0).count, "0.#") : Curve(emission.rateOverTime, "0.#") + "/s";
            var material = ps.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            return $"{count} hạt · {Curve(ps.main.startLifetime, "0.##")}s · {VfxMaterials.Describe(material)}";
        }

        static string Curve(MinMaxCurve curve, string format)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return Number(curve.constant, format);
                case ParticleSystemCurveMode.TwoConstants:
                    return Number(curve.constantMin, format) + "–" + Number(curve.constantMax, format);
                default:
                    return "curve";
            }
        }

        void DrawEmitterBody(float width, VfxEditSession.Node node, VfxEditSession s, ref float y)
        {
            var solo = node.System!;
            var preview = new Rect(0f, y, width, SoloHeight);
            if (preview.yMax > middleScroll.y) s.Preview.Draw(preview, VfxEditSession.SoloSlot, ps => ps == solo);
            VfxUi.Label(new Rect(preview.x + 6f, preview.y + 4f, preview.width - 12f, 16f), $"{node.Transform.name} · riêng", VfxUi.Mini, true);
            y += SoloHeight;
            foreach (var group in EmitterGroups) DrawParams(width, s, solo, VfxParams.Emitter, group, ref y);
        }

        void DrawParams(float width, VfxEditSession s, Component target, Param[] list, ParamGroup group, ref float y)
        {
            var open = DrawGroupHeader(new Rect(0f, y, width, GroupHeight), group.Name);
            y += GroupHeight;
            if (!open) return;
            for (var i = group.Start; i < group.Start + group.Count; i++)
            {
                if (DrawParam(new Rect(0f, y, width, ParamHeight), list[i], target, s.IsChanged(target, i))) s.Touch();
                y += ParamHeight;
            }
        }

        bool DrawGroupHeader(Rect r, string name, string? label = null)
        {
            VfxUi.Fill(r, VfxUi.GroupBar);
            VfxUi.BottomLine(r);
            var open = !collapsedGroups.Contains(name);
            var next = EditorGUI.Foldout(new Rect(r.x + 4f, r.y + 3f, r.width - 8f, 16f), open, label ?? name, true);
            if (next != open) Toggle(collapsedGroups, name);
            return open;
        }

        static void DrawPaneHeader(Rect head, string title)
        {
            VfxUi.Fill(head, VfxUi.Header);
            VfxUi.BottomLine(head);
            if (title.Length > 0) VfxUi.Label(new Rect(head.x + 8f, head.y, head.width - 16f, head.height), title, VfxUi.Title);
        }

        #endregion

        #region Param

        bool DrawParam(Rect row, Param p, Component target, bool changed)
        {
            if (changed) VfxUi.Fill(new Rect(row.x, row.y, 3f, row.height), VfxUi.Accent);
            var reason = p.Locked?.Invoke(target);
            var label = new Rect(row.x + 9f, row.y, Mathf.Clamp(row.width * 0.36f, 84f, 130f), row.height);
            var value = new Rect(row.xMax - 64f, row.y, 58f, row.height);
            var control = new Rect(label.xMax + 6f, row.y + 3f, value.x - label.xMax - 12f, row.height - 6f);
            VfxUi.Label(label, p.Label, VfxUi.Name, reason != null);
            var text = "";
            bool edited;
            using (new EditorGUI.DisabledScope(reason != null))
            {
                EditorGUI.BeginChangeCheck();
                switch (p.Kind)
                {
                    case ParamKind.Curve: text = DrawCurve(control, p, target); break;
                    case ParamKind.Float: text = DrawFloat(control, p, target); break;
                    case ParamKind.Color: DrawColor(control, p, target); break;
                    case ParamKind.Gradient: text = DrawGradient(control, p, target); break;
                    case ParamKind.Enum: DrawEnum(control, p, target); break;
                    case ParamKind.Material: DrawMaterial(control, value, p, target); break;
                    default: throw new ArgumentOutOfRangeException(nameof(p), p.Kind, "unknown parameter kind");
                }
                edited = EditorGUI.EndChangeCheck();
            }
            VfxUi.Label(value, reason ?? text, VfxUi.MiniRight, true);
            return edited;
        }

        static float Snap(float value, float step) => Mathf.Round(value / step) * step;

        static string Format(float value, Param p) =>
            p.Step >= 1f ? Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture) : Number(value) + p.Unit;

        static string DrawCurve(Rect r, Param p, Component target)
        {
            var curve = (MinMaxCurve)p.Get(target)!;
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                {
                    EditorGUI.BeginChangeCheck();
                    var next = GUI.HorizontalSlider(r, curve.constant, p.Min, p.Max);
                    if (EditorGUI.EndChangeCheck())
                    {
                        curve.constant = Snap(next, p.Step);
                        p.Set(target, curve);
                    }
                    return Format(curve.constant, p);
                }
                case ParticleSystemCurveMode.TwoConstants:
                {
                    float low = curve.constantMin, high = curve.constantMax;
                    EditorGUI.BeginChangeCheck();
                    EditorGUI.MinMaxSlider(r, ref low, ref high, p.Min, p.Max);
                    if (EditorGUI.EndChangeCheck())
                    {
                        curve.constantMin = Snap(low, p.Step);
                        curve.constantMax = Snap(high, p.Step);
                        p.Set(target, curve);
                    }
                    return Number(curve.constantMin, p.Step >= 1f ? "0" : "0.##") + "–" + Number(curve.constantMax, p.Step >= 1f ? "0" : "0.##");
                }
                default:
                    return "curve";
            }
        }

        static string DrawFloat(Rect r, Param p, Component target)
        {
            var value = (float)p.Get(target)!;
            EditorGUI.BeginChangeCheck();
            var next = GUI.HorizontalSlider(r, value, p.Min, p.Max);
            if (EditorGUI.EndChangeCheck())
            {
                value = Snap(next, p.Step);
                p.Set(target, value);
            }
            return Format(value, p);
        }

        static void DrawColor(Rect r, Param p, Component target)
        {
            var color = (Color)p.Get(target)!;
            EditorGUI.BeginChangeCheck();
            var next = EditorGUI.ColorField(r, GUIContent.none, color, false, false, false);
            if (EditorGUI.EndChangeCheck()) p.Set(target, next);
        }

        static string DrawGradient(Rect r, Param p, Component target)
        {
            var gradient = (MinMaxGradient)p.Get(target)!;
            switch (gradient.mode)
            {
                case ParticleSystemGradientMode.Color:
                {
                    EditorGUI.BeginChangeCheck();
                    var next = EditorGUI.ColorField(r, GUIContent.none, gradient.color, false, false, true);
                    if (EditorGUI.EndChangeCheck())
                    {
                        gradient.color = new Color(next.r, next.g, next.b, gradient.color.a);
                        p.Set(target, gradient);
                    }
                    return "";
                }
                case ParticleSystemGradientMode.TwoColors:
                {
                    var half = new Rect(r.x, r.y, r.width / 2f - 2f, r.height);
                    var other = new Rect(half.xMax + 4f, r.y, half.width, r.height);
                    EditorGUI.BeginChangeCheck();
                    var low = EditorGUI.ColorField(half, GUIContent.none, gradient.colorMin, false, false, true);
                    var high = EditorGUI.ColorField(other, GUIContent.none, gradient.colorMax, false, false, true);
                    if (EditorGUI.EndChangeCheck())
                    {
                        gradient.colorMin = new Color(low.r, low.g, low.b, gradient.colorMin.a);
                        gradient.colorMax = new Color(high.r, high.g, high.b, gradient.colorMax.a);
                        p.Set(target, gradient);
                    }
                    return "";
                }
                default:
                    VfxUi.Fill(r, gradient.Evaluate(0.5f));
                    return "gradient";
            }
        }

        static void DrawEnum(Rect r, Param p, Component target)
        {
            var current = (Enum)p.Get(target)!;
            EditorGUI.BeginChangeCheck();
            var next = EditorGUI.EnumPopup(r, current);
            if (EditorGUI.EndChangeCheck()) p.Set(target, next);
        }

        static void DrawMaterial(Rect r, Rect value, Param p, Component target)
        {
            var material = (Material?)p.Get(target);
            EditorGUI.BeginChangeCheck();
            var next = (Material?)EditorGUI.ObjectField(r, material, typeof(Material), false);
            if (EditorGUI.EndChangeCheck()) p.Set(target, next);
            var texture = VfxMaterials.MainTexture(next);
            if (texture == null) return;
            var box = new Rect(value.xMax - 22f, value.y + 1f, 22f, 22f);
            VfxUi.Fill(box, VfxUi.Backdrop);
            GUI.DrawTexture(box, texture, ScaleMode.ScaleToFit, true);
        }

        #endregion

        #region Preview

        void DrawPreview(Rect r, VfxEditSession s)
        {
            var head = new Rect(r.x, r.y, r.width, PaneHeader);
            DrawPaneHeader(head, "Preview tổng thể");
            VfxUi.Label(new Rect(head.x, head.y, head.width - 8f, head.height), $"t={Number(s.Clock)}s / {Number(s.Loop, "0.0")}s", VfxUi.MiniRight, true);
            var view = new Rect(r.x, head.yMax, r.width, r.height - PaneHeader - ControlsHeight);
            s.Preview.Draw(view, VfxEditSession.OverallSlot, s.IsShown);
            VfxUi.Label(new Rect(view.x + 6f, view.y + 4f, view.width - 12f, 16f), $"{s.Name} · tất cả emitter đang bật", VfxUi.Mini, true);
            DrawControls(new Rect(r.x, view.yMax, r.width, ControlsHeight), s);
        }

        void DrawControls(Rect r, VfxEditSession s)
        {
            VfxUi.Fill(r, VfxUi.Header);
            var play = new Rect(r.x + 6f, r.y + 4f, 26f, 20f);
            if (GUI.Button(play, new GUIContent(VfxUi.Icon(s.Playing ? "PauseButton" : "PlayButton")), EditorStyles.miniButton))
            {
                s.Playing = !s.Playing;
                if (s.Playing) OnPlay?.Invoke();
            }
            var restart = new Rect(play.xMax + 4f, play.y, 26f, 20f);
            if (GUI.Button(restart, new GUIContent(VfxUi.Icon("Refresh")), EditorStyles.miniButton)) s.Clock = 0f;
            var speeds = new Rect(r.xMax - 126f, play.y, 120f, 20f);
            var scrub = new Rect(restart.xMax + 8f, play.y + 2f, speeds.x - restart.xMax - 16f, 16f);
            EditorGUI.BeginChangeCheck();
            var fraction = GUI.HorizontalSlider(scrub, s.Clock / s.Loop, 0f, 1f);
            if (EditorGUI.EndChangeCheck()) s.Scrub(fraction);
            EditorGUI.BeginChangeCheck();
            var picked = GUI.Toolbar(speeds, Array.IndexOf(SpeedValues, s.Speed), SpeedLabels, EditorStyles.miniButton);
            if (EditorGUI.EndChangeCheck()) s.Speed = SpeedValues[picked];
        }

        #endregion
    }
}
