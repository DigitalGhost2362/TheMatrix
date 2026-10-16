#nullable enable
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Vfx
{
    // the preview pane laid out like the inspector's detached Preview window: title + toolbar, timeline row, image, time label
    static class VfxPreviewView
    {
        const float ButtonWidth = 30f;
        const float ColorWidth = 50f;
        const float SpeedWidth = 150f;
        const float SpeedSnap = 0.03f;
        const float MinSpeed = 0.1f;
        const float MaxSpeed = 2f;

        static readonly GUIContent Eye = EditorGUIUtility.TrIconContent("animationvisibilitytoggleon", "Toggles the enhanced preview on and off");
        static readonly GUIContent SkyboxIcon = EditorGUIUtility.TrIconContent("SceneViewFx", "Toggles skybox visibility");
        static readonly GUIContent LightsIcon = EditorGUIUtility.TrIconContent("SceneViewLighting", "Toggles scene lighting");
        static readonly GUIContent LockIcon = EditorGUIUtility.TrIconContent("SceneViewCamera", "Restore current camera angle when switching effects");
        static readonly GUIContent ResetIcon = EditorGUIUtility.TrIconContent("Refresh", "Reset camera angle");
        static readonly GUIContent RotateXIcon = EditorGUIUtility.TrIconContent("preAudioLoopOff", "Rotate camera along X-axis");
        static readonly GUIContent RotateYIcon = EditorGUIUtility.TrIconContent("RotateTool", "Rotate camera along Y-axis");
        static readonly GUIContent MeshIcon = EditorGUIUtility.TrIconContent("PreMatCube", "Toggles mesh renderers visibility on and off");
        static readonly GUIContent SpriteIcon = EditorGUIUtility.TrIconContent("SpriteRenderer Icon", "Toggles sprite renderers visibility on and off");
        static readonly GUIContent ParticlesIcon = EditorGUIUtility.TrIconContent("Particle Effect", "Toggles particles visibility on and off");
        static readonly GUIContent ForceOffIcon = EditorGUIUtility.TrIconContent("ParticleSystemForceField Icon", "Force particle rendering");
        static readonly GUIContent ForceOnIcon = EditorGUIUtility.TrIconContent("ParticleSystem Icon", "Force particle rendering");
        static readonly GUIContent SpeedSample = new GUIContent("xxxxxx");

        static GUIStyle? speedLight;
        static GUIStyle? speedDark;

        static GUIStyle SpeedLabel => EditorGUIUtility.isProSkin
            ? speedLight ??= new GUIStyle(EditorStyles.whiteLabel) { fontSize = 12, normal = { textColor = new Color(0.76f, 0.76f, 0.76f) } }
            : speedDark ??= new GUIStyle(EditorStyles.whiteLabel) { fontSize = 12, normal = { textColor = new Color(0.1f, 0.1f, 0.1f) } };

        public static void Draw(Rect r, VfxEditSession s)
        {
            var preview = s.Preview;
            var header = new Rect(r.x, r.y, r.width, VfxTimeline.Height);
            GUILayout.BeginArea(header);
            GUILayout.BeginHorizontal(GUIContent.none, EditorStyles.toolbar, GUILayout.Height(VfxTimeline.Height));
            GUILayout.Label(s.Name, "ToolbarBoldLabel");
            GUILayout.FlexibleSpace();
            DrawToolbar(preview);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            var body = new Rect(r.x, header.yMax, r.width, r.yMax - header.yMax);
            if (Event.current.type == EventType.Repaint) ((GUIStyle)"preBackground").Draw(body, false, false, false, false);
            var color = GUI.color;
            GUI.color = Color.white;
            preview.HandleCamera(body, VfxEditSession.OverallSlot);
            var image = Snap4(body);
            var timeline = preview.TimelineShown;
            if (timeline)
            {
                image = Snap4(new Rect(body.x, body.y + VfxTimeline.Height, body.width, body.height - VfxTimeline.Height));
                DrawTimelineRow(body, preview.Timeline);
            }
            preview.Draw(image, VfxEditSession.OverallSlot, s.IsShown, null);
            if (timeline) EditorGUI.DropShadowLabel(new Rect(body.x, body.yMax - 24f, body.width, 20f), preview.Timeline.Label);
            GUI.color = color;
        }

        public static void DrawSolo(Rect r, VfxEditSession s, ParticleSystem solo)
        {
            if (Event.current.type == EventType.Repaint) ((GUIStyle)"preBackground").Draw(r, false, false, false, false);
            var color = GUI.color;
            GUI.color = Color.white;
            s.Preview.HandleCamera(r, VfxEditSession.SoloSlot);
            s.Preview.Draw(Snap4(r), VfxEditSession.SoloSlot, ps => ps == solo, solo);
            GUI.color = color;
        }

        static void DrawToolbar(VfxLivePreview preview)
        {
            var width = GUILayout.Width(ButtonWidth);
            var view = preview.Views[VfxEditSession.OverallSlot];
            var enabled = VfxPreviewSettings.Enabled;
            if (Toggled(enabled, Eye, width))
            {
                VfxPreviewSettings.Enabled = !enabled;
                preview.ApplyLights();
                Debug.Log($"[VFX Studio] Preview {(enabled ? "Disabled" : "Enabled")}.");
            }
            if (!enabled) return;
            var skybox = VfxPreviewSettings.Skybox;
            if (!skybox)
            {
                EditorGUI.BeginChangeCheck();
                var background = EditorGUILayout.ColorField(VfxPreviewSettings.Background, GUILayout.Width(ColorWidth));
                if (EditorGUI.EndChangeCheck()) VfxPreviewSettings.Background = background;
            }
            if (Toggled(!skybox, SkyboxIcon, width)) VfxPreviewSettings.Skybox = !skybox;
            var lights = VfxPreviewSettings.Lights;
            if (Toggled(!lights, LightsIcon, width))
            {
                VfxPreviewSettings.Lights = !lights;
                preview.ApplyLights();
            }
            var locked = VfxPreviewSettings.CameraLocked;
            if (Toggled(locked, LockIcon, width)) preview.Lock(view, !locked);
            if (GUILayout.Button(ResetIcon, EditorStyles.toolbarButton, width)) view.Reset();
            if (GUILayout.Button(RotateXIcon, EditorStyles.toolbarButton, width)) preview.RotateX(view);
            if (GUILayout.Button(RotateYIcon, EditorStyles.toolbarButton, width)) preview.RotateY(view);
            var several = preview.Kinds > 1;
            if (preview.HasMeshes && several && Toggled(preview.ShowMeshes, MeshIcon, width)) preview.ShowMeshes = !preview.ShowMeshes;
            if (preview.HasSprites && several && Toggled(preview.ShowSprites, SpriteIcon, width)) preview.ShowSprites = !preview.ShowSprites;
            if (!preview.HasParticles) return;
            if (several && Toggled(preview.ShowParticles, ParticlesIcon, width)) preview.ToggleParticles();
            if (Toggled(preview.ForceRender, preview.ForceRender ? ForceOnIcon : ForceOffIcon, width)) preview.ForceRender = !preview.ForceRender;
        }

        static bool Toggled(bool value, GUIContent content, GUILayoutOption width)
        {
            EditorGUI.BeginChangeCheck();
            GUILayout.Toggle(value, content, EditorStyles.toolbarButton, width);
            return EditorGUI.EndChangeCheck();
        }

        static void DrawTimelineRow(Rect body, VfxTimeline timeline)
        {
            GUI.Box(body, GUIContent.none, EditorStyles.toolbar);
            var bar = new Rect(body.x, body.y, body.width - SpeedWidth, 20f);
            var slider = new Rect(body.xMax - SpeedWidth + 4f, body.y + 1f, 0f, 18f);
            var labelSize = EditorStyles.toolbarTextField.CalcSize(SpeedSample);
            var label = new Rect(body.xMax - labelSize.x, body.y, labelSize.x, body.height);
            slider.xMax = label.xMin;
            timeline.Draw(bar);
            timeline.Speed = Snap(GUI.HorizontalSlider(slider, timeline.Speed, MinSpeed, MaxSpeed, "preSlider", "preSliderThumb"));
            label.y += 1.75f;
            GUI.Label(label, timeline.Speed.ToString("f2", CultureInfo.InvariantCulture.NumberFormat) + "x", SpeedLabel);
        }

        static Rect Snap4(Rect r) => new Rect(r.x, r.y, (int)System.Math.Round(r.width / 4f) * 4, (int)System.Math.Round(r.height / 4f) * 4);

        static float Snap(float speed)
        {
            for (var stop = 0.25f; stop < MaxSpeed; stop += 0.25f)
                if (speed > stop - SpeedSnap && speed < stop + SpeedSnap) return stop;
            return speed;
        }
    }
}
