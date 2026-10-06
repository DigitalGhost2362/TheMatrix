#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Feeder.MCP.Editor.Vfx
{
    // one simulation per repaint feeds both the overall pane and the single-emitter pane
    sealed class VfxLivePreview : IDisposable
    {
        const float Fov = 30f;
        const int Seed = 7919;
        static readonly Color Axis = new Color(1f, 1f, 1f, 0.07f);

        readonly PreviewRenderUtility utility = new PreviewRenderUtility();
        readonly RenderTexture?[] targets = new RenderTexture?[2];
        ParticleSystem[] systems = Array.Empty<ParticleSystem>();
        ParticleSystem[] tops = Array.Empty<ParticleSystem>();
        ParticleSystemRenderer?[] renderers = Array.Empty<ParticleSystemRenderer?>();
        bool[] rendererOn = Array.Empty<bool>();
        Vector3 center;
        float radius = 1f;

        public GameObject? Instance { get; private set; }

        public IReadOnlyList<ParticleSystem> Systems => systems;

        public VfxLivePreview()
        {
            var camera = utility.camera;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = VfxUi.Backdrop;
            camera.nearClipPlane = 0.01f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
        }

        public void Rebuild(GameObject prefab)
        {
            if (Instance != null) Object.DestroyImmediate(Instance);
            Instance = utility.InstantiatePrefabInScene(prefab);
            systems = Instance.GetComponentsInChildren<ParticleSystem>(true);
            tops = systems.Where(ps => !HasSystemAncestor(ps)).ToArray();
            renderers = systems.Select(ps => ps.GetComponent<ParticleSystemRenderer>()).ToArray();
            rendererOn = renderers.Select(r => r != null && r.enabled).ToArray();
            for (var i = 0; i < systems.Length; i++)
            {
                systems[i].useAutoRandomSeed = false;
                systems[i].randomSeed = (uint)(Seed + i);
            }
        }

        public void Frame(Vector3 frameCenter, float frameRadius)
        {
            center = frameCenter;
            radius = Mathf.Max(0.1f, frameRadius);
        }

        public void Simulate(float time)
        {
            foreach (var ps in tops) ps.Simulate(time, true, true);
        }

        public void Draw(Rect rect, int slot, Func<ParticleSystem, bool> visible)
        {
            if (Instance == null || Event.current.type != EventType.Repaint || rect.width < 8f || rect.height < 8f) return;
            var scale = EditorGUIUtility.pixelsPerPoint;
            var width = Mathf.RoundToInt(rect.width * scale);
            var height = Mathf.RoundToInt(rect.height * scale);
            var target = targets[slot];
            if (target == null || target.width != width || target.height != height)
            {
                if (target != null) Object.DestroyImmediate(target);
                target = targets[slot] = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
            }
            for (var i = 0; i < systems.Length; i++)
                if (renderers[i] != null) renderers[i]!.enabled = rendererOn[i] && visible(systems[i]);
            var camera = utility.camera;
            var aspect = width / (float)height;
            PlaceCamera(camera, aspect);
            var previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = null;
            RenderTexture.active = previous;
            for (var i = 0; i < systems.Length; i++)
                if (renderers[i] != null) renderers[i]!.enabled = rendererOn[i];
            GUI.DrawTexture(rect, target, ScaleMode.StretchToFill, false);
            DrawAxes(rect, camera, aspect);
        }

        public void Dispose()
        {
            foreach (var target in targets)
                if (target != null) Object.DestroyImmediate(target);
            Array.Clear(targets, 0, targets.Length);
            if (Instance != null) Object.DestroyImmediate(Instance);
            utility.Cleanup();
        }

        static bool HasSystemAncestor(ParticleSystem ps)
        {
            for (var t = ps.transform.parent; t != null; t = t.parent)
                if (t.GetComponent<ParticleSystem>() != null) return true;
            return false;
        }

        void PlaceCamera(Camera camera, float aspect)
        {
            var half = Fov * 0.5f * Mathf.Deg2Rad;
            camera.fieldOfView = aspect >= 1f ? Fov : 2f * Mathf.Atan(Mathf.Tan(half) / aspect) * Mathf.Rad2Deg;
            camera.farClipPlane = radius * 20f + 100f;
            camera.transform.position = center - Vector3.forward * (radius / Mathf.Sin(half));
            camera.transform.rotation = Quaternion.identity;
        }

        static void DrawAxes(Rect rect, Camera camera, float aspect)
        {
            var offset = -camera.transform.position;
            if (offset.z <= 0.01f) return;
            var tan = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var x = Mathf.Round(rect.center.x + offset.x / (offset.z * tan * aspect) * rect.width * 0.5f);
            var y = Mathf.Round(rect.center.y - offset.y / (offset.z * tan) * rect.height * 0.5f);
            if (y >= rect.y && y < rect.yMax) EditorGUI.DrawRect(new Rect(rect.x, y, rect.width, 1f), Axis);
            if (x >= rect.x && x < rect.xMax) EditorGUI.DrawRect(new Rect(x, rect.y, 1f, rect.height), Axis);
        }
    }
}
