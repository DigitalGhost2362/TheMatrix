#nullable enable
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Vfx
{
    // toolbar state of the studio preview, shared by every studio window like a settings asset would be
    static class VfxPreviewSettings
    {
        const string Prefix = "Feeder.VfxStudio.Preview.";

        public const float FieldOfView = 30f;
        public const float DefaultZoom = 1f;
        public static readonly Vector2 DefaultDirection = new Vector2(120f, -20f);
        public static readonly Color Ambient = new Color(0.5882353f, 0.5882353f, 0.5882353f, 1f);
        public static readonly Vector2[] LightRotations = { new Vector2(160f, 0f), new Vector2(20f, 0f), new Vector2(90f, 0f) };
        public const float LightIntensity = 0.5f;
        static readonly Color DefaultBackground = new Color(0.30188680f, 0.29476681f, 0.30139950f, 1f);

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(Prefix + "Enabled", true);
            set => EditorPrefs.SetBool(Prefix + "Enabled", value);
        }

        public static bool Skybox
        {
            get => EditorPrefs.GetBool(Prefix + "Skybox", false);
            set => EditorPrefs.SetBool(Prefix + "Skybox", value);
        }

        public static bool Lights
        {
            get => EditorPrefs.GetBool(Prefix + "Lights", true);
            set => EditorPrefs.SetBool(Prefix + "Lights", value);
        }

        public static Color Background
        {
            get => new Color(
                EditorPrefs.GetFloat(Prefix + "Background.r", DefaultBackground.r),
                EditorPrefs.GetFloat(Prefix + "Background.g", DefaultBackground.g),
                EditorPrefs.GetFloat(Prefix + "Background.b", DefaultBackground.b),
                EditorPrefs.GetFloat(Prefix + "Background.a", DefaultBackground.a));
            set
            {
                EditorPrefs.SetFloat(Prefix + "Background.r", value.r);
                EditorPrefs.SetFloat(Prefix + "Background.g", value.g);
                EditorPrefs.SetFloat(Prefix + "Background.b", value.b);
                EditorPrefs.SetFloat(Prefix + "Background.a", value.a);
            }
        }

        // the camera lock only lives until the editor restarts
        public static bool CameraLocked
        {
            get => SessionState.GetBool(Prefix + "CameraLocked", false);
            set => SessionState.SetBool(Prefix + "CameraLocked", value);
        }

        public static Vector2 LockedDirection
        {
            get => SessionState.GetVector3(Prefix + "LockedDirection", DefaultDirection);
            set => SessionState.SetVector3(Prefix + "LockedDirection", value);
        }

        public static float LockedZoom
        {
            get => SessionState.GetFloat(Prefix + "LockedZoom", DefaultZoom);
            set => SessionState.SetFloat(Prefix + "LockedZoom", value);
        }

        public static Vector3 LockedPan
        {
            get => SessionState.GetVector3(Prefix + "LockedPan", Vector3.zero);
            set => SessionState.SetVector3(Prefix + "LockedPan", value);
        }
    }
}
