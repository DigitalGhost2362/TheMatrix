#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Feeder.MCP.Editor.Vfx
{
    // renders like the inspector object preview: orbit/zoom/pan camera framed on sampled particle bounds, played on a timeline
    sealed class VfxLivePreview : IDisposable
    {
        public sealed class View
        {
            public Vector2 Direction;
            public float Zoom;
            public Vector3 Pan;
            public Vector2 PanStep;
            public Vector3 CameraPosition;

            public void Reset()
            {
                var locked = VfxPreviewSettings.CameraLocked;
                Direction = locked ? VfxPreviewSettings.LockedDirection : VfxPreviewSettings.DefaultDirection;
                Zoom = locked ? VfxPreviewSettings.LockedZoom : VfxPreviewSettings.DefaultZoom;
                Pan = locked ? VfxPreviewSettings.LockedPan : Vector3.zero;
                PanStep = Vector2.zero;
            }
        }

        const float StartDelay = 0.25f;
        const float RestartWindow = 0.05f;
        const float MaxSampleTime = 50f;
        const int FrameSamples = 5;
        const float ParticleFrameShrink = 1.5f;
        const float DistanceFactor = 3.8f;
        const int PanHash = 892103136;
        const string CustomLightName = "VFX_STUDIO_PREVIEW_LIGHT";
        static readonly int OrbitHash = "Slider".GetHashCode();
        static readonly MethodInfo? AnimatorPreviewCopy = typeof(EditorUtility).GetMethod("InstantiateForAnimatorPreview", BindingFlags.Static | BindingFlags.NonPublic);
        static bool warnedCopy;
        static readonly Color UnityBackground = new Color(49f / 255f, 49f / 255f, 49f / 255f, 1f);
        static readonly Color UnityAmbient = new Color(0.1f, 0.1f, 0.1f, 0f);
        static readonly Color FrontLight = new Color(0.769f, 0.769f, 0.769f, 1f);
        static readonly Color BackLight = new Color(0.4f, 0.4f, 0.45f, 0f) * 0.7f;

        readonly PreviewRenderUtility utility = new PreviewRenderUtility();
        readonly RenderTexture?[] targets = new RenderTexture?[2];
        readonly List<GameObject> customLights = new List<GameObject>();
        ParticleSystem[] systems = Array.Empty<ParticleSystem>();
        ParticleSystem[] tops = Array.Empty<ParticleSystem>();
        ParticleSystemRenderer?[] renderers = Array.Empty<ParticleSystemRenderer?>();
        bool[] rendererOn = Array.Empty<bool>();
        Renderer[] meshRenderers = Array.Empty<Renderer>();
        SpriteRenderer[] spriteRenderers = Array.Empty<SpriteRenderer>();
        readonly Dictionary<ParticleSystem, Bounds?> soloFrames = new Dictionary<ParticleSystem, Bounds?>();
        Bounds? particleFrame;
        Bounds? meshFrame;
        Bounds? spriteFrame;
        Bounds unityFrame;
        double startAt;
        bool started;

        public readonly View[] Views = { new View(), new View() };
        public readonly VfxTimeline Timeline = new VfxTimeline();
        public bool ForceRender;
        public bool ShowMeshes = true;
        public bool ShowSprites = true;
        public bool ShowParticles = true;

        public GameObject? Instance { get; private set; }

        public IReadOnlyList<ParticleSystem> Systems => systems;

        public bool HasMeshes => meshRenderers.Length > 0;

        public bool HasSprites => spriteRenderers.Length > 0;

        public bool HasParticles => systems.Any(Usable);

        public int Kinds => (HasMeshes ? 1 : 0) + (HasSprites ? 1 : 0) + (HasParticles ? 1 : 0);

        public bool TimelineShown => VfxPreviewSettings.Enabled && HasParticles && ShowParticles;

        public bool Animating => TimelineShown && (!started || Timeline.Playing);

        public VfxLivePreview()
        {
            foreach (var view in Views) view.Reset();
            ApplyLights();
        }

        #region Scene

        public void Rebuild(GameObject prefab)
        {
            if (Instance != null) Object.DestroyImmediate(Instance);
            Instance = utility.InstantiatePrefabInScene(prefab);
            Instance.transform.eulerAngles = Vector3.zero;
            unityFrame = PlainFrame(prefab, Instance);
            foreach (var behaviour in Instance.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
            systems = Instance.GetComponentsInChildren<ParticleSystem>(true);
            tops = TopSystems(Instance.transform).ToArray();
            renderers = systems.Select(ps => ps.GetComponent<ParticleSystemRenderer>()).ToArray();
            rendererOn = renderers.Select(r => r != null && r.enabled).ToArray();
            meshRenderers = Instance.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer || r is SpriteRenderer || r is SpriteMask)).ToArray();
            spriteRenderers = Instance.GetComponentsInChildren<SpriteRenderer>().Where(r => r.enabled).ToArray();
            foreach (var ps in systems)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.useUnscaledTime = true;
            }
            Seed(false);
            foreach (var ps in systems) ps.Play(false);
            Timeline.StopTime = Duration(Instance.transform);
            MeasureFrames();
            var grab = Instance.GetComponentsInChildren<Renderer>(true).Where(VfxPreview.UsesGrabPass).ToArray();
            foreach (var r in grab) r.enabled = false;
            rendererOn = renderers.Select(r => r != null && r.enabled).ToArray();
            meshRenderers = meshRenderers.Except(grab).ToArray();
            Seed(true);
            Restart();
        }

        // framing is sampled with seed 0 in world space, playback keeps world space with a fresh random seed per system
        void Seed(bool random)
        {
            foreach (var ps in systems)
            {
                ps.useAutoRandomSeed = false;
                ps.randomSeed = random ? (uint)UnityEngine.Random.Range(0, int.MaxValue) : 0u;
            }
        }

        public void Restart()
        {
            foreach (var ps in tops) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            started = false;
            startAt = EditorApplication.timeSinceStartup + StartDelay;
            Timeline.CurrentTime = 0f;
            Timeline.Playing = false;
        }

        public void Resimulate()
        {
            Timeline.StopTime = Instance != null ? Duration(Instance.transform) : 1f;
            if (!started) return;
            foreach (var ps in tops) Replay(ps, Timeline.CurrentTime);
        }

        public void Tick()
        {
            if (!VfxPreviewSettings.Enabled || !HasParticles || !ShowParticles) return;
            if (!started)
            {
                if (EditorApplication.timeSinceStartup < startAt) return;
                started = true;
                foreach (var ps in tops)
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Play();
                }
                Timeline.CurrentTime = 0f;
                Timeline.Playing = true;
            }
            if (EditorApplication.isPlaying && EditorWindow.focusedWindow != null && EditorWindow.focusedWindow.GetType().Name == "GameView") Timeline.Playing = false;
            Timeline.Update();
            Step();
        }

        void Step()
        {
            var delta = Timeline.DeltaTime;
            foreach (var ps in tops)
            {
                if ((!ForceRender && Mathf.Abs(Timeline.StopTime - ps.time) < RestartWindow) || Mathf.Abs(Timeline.StopTime - Timeline.CurrentTime) < RestartWindow)
                {
                    ps.Stop();
                    ps.Play();
                    ps.time = 0f;
                }
                else if (ForceRender || delta < 0f) Replay(ps, Timeline.CurrentTime);
                else if (delta > 0f) ps.Simulate(delta, true, false, false);
            }
        }

        static void Replay(ParticleSystem ps, float time)
        {
            ps.time = 0f;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play();
            ps.Simulate(time, true, false, false);
            ps.Simulate(0f, true, false, false);
        }

        static IEnumerable<ParticleSystem> TopSystems(Transform t)
        {
            var own = t.GetComponent<ParticleSystem>();
            if (own != null)
            {
                if (t.gameObject.activeInHierarchy) yield return own;
                yield break;
            }
            foreach (Transform child in t)
                foreach (var ps in TopSystems(child))
                    yield return ps;
        }

        static float Duration(Transform t)
        {
            var longest = 0f;
            var ps = t.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                var main = ps.main;
                var delay = main.startDelay;
                longest = main.duration + delay.mode switch
                {
                    ParticleSystemCurveMode.TwoConstants => Mathf.Max(delay.constantMin, delay.constantMax),
                    ParticleSystemCurveMode.TwoCurves => Mathf.Max(delay.curveMin.keys[delay.curveMin.length - 1].time, delay.curveMax.keys[delay.curveMax.length - 1].time),
                    _ => delay.constant
                };
            }
            foreach (Transform child in t) longest = Mathf.Max(longest, Duration(child));
            return longest;
        }

        static bool HasGeometry(Renderer r) => r switch
        {
            MeshRenderer _ => r.GetComponent<MeshFilter>() is { sharedMesh: { } },
            SkinnedMeshRenderer skinned => skinned.sharedMesh != null,
            SpriteRenderer sprite => sprite.sprite != null,
            _ => true
        };

        static bool Usable(ParticleSystem ps)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            return r != null && (r.enabled || ps.emission.enabled) && ps.gameObject.activeInHierarchy;
        }

        #endregion

        #region Framing

        void MeasureFrames()
        {
            meshFrame = Valid(Union(meshRenderers.Where(HasGeometry)));
            spriteFrame = Valid(Union(spriteRenderers.Where(HasGeometry)));
            var lightsOn = systems.Select(ps => ps.lights.enabled).ToArray();
            foreach (var ps in systems)
            {
                var lights = ps.lights;
                lights.enabled = false;
            }
            var usable = systems.Where(Usable).ToArray();
            var overall = new List<Bounds>();
            var solo = usable.ToDictionary(ps => ps, _ => new List<Bounds>());
            var length = Mathf.Clamp(Timeline.StopTime, 0f, MaxSampleTime);
            for (var i = 0; i <= FrameSamples; i++)
            {
                foreach (var ps in tops)
                {
                    ps.Stop();
                    ps.Play();
                    ps.Simulate(i == 0 ? 0.1f : length / FrameSamples * i, true, false, false);
                }
                overall.Add(Union(usable.Select(ps => (Renderer)ps.GetComponent<ParticleSystemRenderer>())));
                foreach (var ps in usable) solo[ps].Add(ps.GetComponent<ParticleSystemRenderer>().bounds);
                foreach (var ps in tops) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            for (var i = 0; i < systems.Length; i++)
            {
                var lights = systems[i].lights;
                lights.enabled = lightsOn[i];
            }
            particleFrame = Valid(Average(overall));
            soloFrames.Clear();
            foreach (var pair in solo) soloFrames[pair.Key] = Valid(Average(pair.Value));
        }

        static Bounds Union(IEnumerable<Renderer> list)
        {
            var result = default(Bounds);
            foreach (var r in list)
            {
                if (result.size == Vector3.zero) result = r.bounds;
                else result.Encapsulate(r.bounds);
            }
            return result;
        }

        static Bounds? Average(List<Bounds> samples)
        {
            var size = Vector3.zero;
            var center = Vector3.zero;
            var count = 0;
            foreach (var sample in samples)
            {
                if (sample.size.magnitude <= 0.1f) continue;
                size += sample.size;
                if (center == Vector3.zero) center = sample.center;
                count++;
            }
            if (count > 0) size /= count;
            if (size.magnitude < 0.01f) return null;
            return new Bounds(center, size);
        }

        static Bounds? Valid(Bounds? b) => b.HasValue && b.Value.size.magnitude > 0.015f ? b : null;

        static Bounds? Valid(Bounds b) => Valid((Bounds?)b);

        Bounds BaseFrame(ParticleSystem? solo)
        {
            var mesh = solo == null && ShowMeshes ? meshFrame : null;
            var sprite = solo == null && ShowSprites ? spriteFrame : null;
            var particles = !ShowParticles ? null : solo == null ? particleFrame : soloFrames.TryGetValue(solo, out var own) ? own : null;
            var frame = default(Bounds);
            var shrink = 1f;
            foreach (var part in new[] { mesh, sprite, particles })
            {
                if (!part.HasValue) continue;
                if (frame.size == Vector3.zero) frame = part.Value;
                else frame.Encapsulate(part.Value);
            }
            if (particles.HasValue) shrink = ParticleFrameShrink;
            if (!Valid(frame).HasValue) frame = new Bounds(Vector3.zero, Vector3.one * 5f);
            if (frame.center.magnitude > 10f || frame.size.z / frame.size.x > 1.8f || frame.size.z / frame.size.y > 1.8f || mesh.HasValue || sprite.HasValue) shrink = 1f;
            frame.size /= shrink;
            return frame;
        }

        // the plain inspector frames the stripped, unrotated copy it builds for animator previews
        static Bounds PlainFrame(GameObject prefab, GameObject fallback)
        {
            if (AnimatorPreviewCopy == null)
            {
                if (!warnedCopy) Debug.LogWarning("[VFX Studio] EditorUtility.InstantiateForAnimatorPreview not found; the plain preview frames the live instance instead.");
                warnedCopy = true;
                return UnityFrame(fallback);
            }
            var copy = (GameObject)AnimatorPreviewCopy.Invoke(null, new object[] { prefab });
            try
            {
                return UnityFrame(copy);
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
        }

        static Bounds UnityFrame(GameObject root)
        {
            var result = default(Bounds);
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r is MeshRenderer && r.GetComponent<MeshFilter>() is not { sharedMesh: { } }) continue;
                if (r is SkinnedMeshRenderer { sharedMesh: null }) continue;
                if (r is SpriteRenderer { sprite: null }) continue;
                if (r is BillboardRenderer billboard && (billboard.billboard == null || billboard.sharedMaterial == null)) continue;
                if (result.extents == Vector3.zero) result = r.bounds;
                else result.Encapsulate(r.bounds);
            }
            return result;
        }

        #endregion

        #region Lights

        public void ApplyLights()
        {
            foreach (var go in customLights)
                if (go != null) Object.DestroyImmediate(go);
            customLights.Clear();
            var on = VfxPreviewSettings.Enabled && VfxPreviewSettings.Lights;
            var front = utility.lights[0];
            var back = utility.lights[1];
            if (VfxPreviewSettings.Enabled)
            {
                Configure(front, 1f, new Vector2(150f, 0f));
                front.renderMode = LightRenderMode.ForcePixel;
                Configure(back, 1f, new Vector2(210f, 0f));
            }
            if (!VfxPreviewSettings.Enabled)
            {
                front.renderMode = LightRenderMode.Auto;
                back.renderMode = LightRenderMode.Auto;
            }
            if (!on)
            {
                front.color = FrontLight;
                back.color = BackLight;
                return;
            }
            foreach (var rotation in VfxPreviewSettings.LightRotations)
            {
                var go = EditorUtility.CreateGameObjectWithHideFlags(CustomLightName, HideFlags.HideAndDontSave, typeof(Light));
                Configure(go.GetComponent<Light>(), VfxPreviewSettings.LightIntensity, rotation);
                utility.AddSingleGO(go);
                customLights.Add(go);
            }
        }

        static void Configure(Light light, float intensity, Vector2 rotation)
        {
            light.color = Color.white;
            light.bounceIntensity = 1f;
            light.intensity = intensity;
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(rotation.x, rotation.y, 0f);
            light.renderMode = LightRenderMode.ForceVertex;
            light.shadows = LightShadows.None;
        }

        #endregion

        #region Draw

        public void HandleCamera(Rect area, int slot)
        {
            if (VfxPreviewSettings.Enabled) HandleZoomAndPan(area, Views[slot]);
        }

        public void Draw(Rect image, int slot, Func<ParticleSystem, bool> visible, ParticleSystem? solo)
        {
            if (Instance == null) return;
            var view = Views[slot];
            var enabled = VfxPreviewSettings.Enabled;
            view.Direction = Orbit(view.Direction, image);
            if (Event.current.type != EventType.Repaint || image.width < 1f || image.height < 1f) return;
            var frame = enabled ? FramedBounds(view, solo) : unityFrame;
            Show(slot, visible, solo);
            Render(image, slot, view, frame, enabled);
            Show(-1, visible, solo);
        }

        Bounds FramedBounds(View view, ParticleSystem? solo)
        {
            var frame = BaseFrame(solo);
            if (view.PanStep.magnitude > 0f)
            {
                var distance = Vector3.Distance(view.CameraPosition, frame.center);
                var rotation = Quaternion.Euler(-view.Direction.y, -view.Direction.x, 0f);
                view.Pan += (rotation * Vector3.right * -view.PanStep.x + rotation * Vector3.up * view.PanStep.y) * (distance / 20f);
                view.PanStep = Vector2.zero;
            }
            frame.size *= view.Zoom;
            frame.center += view.Pan;
            return frame;
        }

        void Show(int slot, Func<ParticleSystem, bool> visible, ParticleSystem? solo)
        {
            var particles = VfxPreviewSettings.Enabled && ShowParticles;
            for (var i = 0; i < systems.Length; i++)
                if (renderers[i] != null) renderers[i]!.enabled = rendererOn[i] && (slot < 0 || (particles && visible(systems[i])));
            foreach (var r in meshRenderers) r.enabled = slot < 0 || (solo == null && ShowMeshes);
            foreach (var r in spriteRenderers) r.enabled = slot < 0 || (solo == null && ShowSprites);
        }

        void Render(Rect image, int slot, View view, Bounds frame, bool enabled)
        {
            var camera = utility.camera;
            var scale = EditorGUIUtility.pixelsPerPoint;
            var width = (int)(image.width * scale);
            var height = (int)(image.height * scale);
            var target = targets[slot];
            if (target == null || target.width != width || target.height != height)
            {
                if (target != null) Object.DestroyImmediate(target);
                var format = camera.allowHDR ? GraphicsFormat.R16G16B16A16_SFloat : GraphicsFormat.R8G8B8A8_UNorm;
                target = targets[slot] = new RenderTexture(width, height, format, SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil)) { hideFlags = HideFlags.HideAndDontSave };
            }
            var half = Mathf.Max(frame.extents.magnitude, 0.0001f);
            var distance = half * DistanceFactor;
            var rotation = Quaternion.Euler(-view.Direction.y, -view.Direction.x, 0f);
            camera.transform.position = frame.center - rotation * (Vector3.forward * distance);
            camera.transform.rotation = rotation;
            view.CameraPosition = camera.transform.position;
            camera.nearClipPlane = enabled ? 0.001f : distance - half * 1.1f;
            camera.farClipPlane = enabled ? 9999999f : distance + half * 1.1f;
            var skybox = enabled && VfxPreviewSettings.Skybox;
            camera.clearFlags = !enabled ? CameraClearFlags.Depth : skybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            camera.backgroundColor = enabled ? VfxPreviewSettings.Background : QualitySettings.activeColorSpace == ColorSpace.Linear ? UnityBackground.linear : UnityBackground;
            var lights = utility.lights;
            lights[0].intensity = 0.7f;
            lights[0].transform.rotation = rotation * Quaternion.Euler(40f, 40f, 0f);
            lights[1].intensity = 0.7f;
            lights[1].transform.rotation = rotation * Quaternion.Euler(340f, 218f, 177f);
            utility.ambientColor = enabled ? VfxPreviewSettings.Ambient : UnityAmbient;
            var scene = Instance!.scene;
            if (Unsupported.SetOverrideLightingSettings(scene))
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = ReflectionProbe.defaultTexture;
            }
            var fov = VfxPreviewSettings.FieldOfView;
            var stretch = Mathf.Max(1f, height / (float)width);
            camera.fieldOfView = Mathf.Atan(stretch * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad)) * Mathf.Rad2Deg * 2f;
            var previous = RenderTexture.active;
            if (!enabled) PaintBackdrop(target, camera.backgroundColor);
            camera.targetTexture = target;
            utility.Render(true, false);
            camera.targetTexture = null;
            camera.fieldOfView = fov;
            RenderTexture.active = previous;
            Unsupported.RestoreOverrideLightingSettings();
            Unsupported.RestoreOverrideLightingSettings();
            foreach (var light in lights) light.enabled = false;
            GUI.DrawTexture(image, target, ScaleMode.StretchToFill, false);
        }

        // the plain inspector preview clears depth only, over its cleared colour and the preBackground style
        static void PaintBackdrop(RenderTexture target, Color clear)
        {
            RenderTexture.active = target;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, target.width, target.height, 0f);
            GL.Clear(true, true, clear);
            var style = (GUIStyle)"preBackground";
            if (style.normal.background != null)
                Graphics.DrawTexture(style.overflow.Add(new Rect(0f, 0f, target.width, target.height)), style.normal.background, new Rect(0f, 0f, 1f, 1f),
                    style.border.left, style.border.right, style.border.top, style.border.bottom, new Color(0.5f, 0.5f, 0.5f, 0.5f), null);
            GL.PopMatrix();
        }

        static Vector2 Orbit(Vector2 direction, Rect r)
        {
            var id = GUIUtility.GetControlID(OrbitHash, FocusType.Passive);
            var e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (r.Contains(e.mousePosition) && r.width > 50f)
                    {
                        GUIUtility.hotControl = id;
                        e.Use();
                        EditorGUIUtility.SetWantsMouseJumping(1);
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        direction -= e.delta * (e.shift ? 3 : 1) / Mathf.Min(r.width, r.height) * 140f;
                        e.Use();
                        GUI.changed = true;
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) GUIUtility.hotControl = 0;
                    EditorGUIUtility.SetWantsMouseJumping(0);
                    break;
            }
            return direction;
        }

        static void HandleZoomAndPan(Rect r, View view)
        {
            var id = GUIUtility.GetControlID(PanHash, FocusType.Passive);
            var e = Event.current;
            if (r.Contains(e.mousePosition))
            {
                if (e.type == EventType.ScrollWheel)
                {
                    var step = -HandleUtility.niceMouseDeltaZoom * (e.shift ? 0.6f : 0.3f);
                    view.Zoom += view.Zoom * (step * 0.05f);
                    e.Use();
                }
                if (e.type == EventType.MouseDown && e.button == 2)
                {
                    EditorGUIUtility.SetWantsMouseJumping(1);
                    GUIUtility.hotControl = id;
                    e.Use();
                    return;
                }
            }
            if (GUIUtility.hotControl != id) return;
            if (e.type == EventType.MouseDrag && e.button == 2)
            {
                var step = e.delta * 0.02f;
                if (Mathf.Abs(step.x) < 5f && Mathf.Abs(step.y) < 5f) view.PanStep = step;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 2)
            {
                EditorGUIUtility.SetWantsMouseJumping(0);
                GUIUtility.hotControl = 0;
                e.Use();
            }
            if (e.type == EventType.Repaint) EditorGUIUtility.AddCursorRect(r, MouseCursor.Pan);
        }

        #endregion

        #region Toolbar

        public void RotateX(View view) => view.Direction.x = (int)((view.Direction.x + 90f) / 90f) * 90;

        public void RotateY(View view)
        {
            var direction = view.Direction;
            direction.y -= 90f;
            direction.y = direction.y <= -90f && direction.y > -180f ? -90f : -2.5f;
            if (view.CameraPosition.z > 0.1f) direction.x = 180f;
            else if (view.CameraPosition.z < -0.1f) direction.x = 0f;
            view.Direction = direction;
        }

        public void ToggleParticles()
        {
            ShowParticles = !ShowParticles;
            Timeline.CurrentTime = 0f;
            Timeline.Playing = false;
            Timeline.Playing = true;
            foreach (var ps in tops)
            {
                ps.time = 0f;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play();
            }
        }

        public void Lock(View view, bool locked)
        {
            VfxPreviewSettings.CameraLocked = locked;
            Debug.Log($"[VFX Studio] Preview camera lock: {(locked ? "On" : "Off")}.");
            if (!locked) return;
            VfxPreviewSettings.LockedDirection = view.Direction;
            VfxPreviewSettings.LockedZoom = view.Zoom;
            VfxPreviewSettings.LockedPan = view.Pan;
        }

        #endregion

        public void Dispose()
        {
            foreach (var target in targets)
                if (target != null) Object.DestroyImmediate(target);
            Array.Clear(targets, 0, targets.Length);
            foreach (var go in customLights)
                if (go != null) Object.DestroyImmediate(go);
            customLights.Clear();
            if (Instance != null) Object.DestroyImmediate(Instance);
            utility.Cleanup();
        }
    }
}
