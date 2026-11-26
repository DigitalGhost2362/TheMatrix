#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
#if MATRIX_UGUI
using UnityEngine.EventSystems;
using UnityEngine.UI;
#endif

// FindObjectsByType without a sort mode is Unity 6.4+, the package still supports 2022.3
#pragma warning disable CS0618
namespace Feeder.MCP.Editor.Playtest
{
    // finds play-mode objects by hierarchy path or button label and clicks uGUI the way a pointer would
    internal static class PlaytestScene
    {
        static Scene? _dontDestroyScene;
#if MATRIX_UGUI
        static readonly List<RaycastResult> _hits = new List<RaycastResult>();
#endif

        const int MaxLabel = 40;
        static readonly Dictionary<Type, PropertyInfo?> _textProperty = new Dictionary<Type, PropertyInfo?>();

        public static void Reset() => _dontDestroyScene = null;

        // "/Canvas/List/Item[2]": the third sibling named Item gets an index
        public static string PathOf(GameObject go)
        {
            var parts = new List<string>();
            for (var t = go.transform; t != null; t = t.parent)
            {
                var parent = t.parent;
                int same = 0, index = 0;
                if (parent != null)
                {
                    for (var i = 0; i < parent.childCount; i++)
                    {
                        var child = parent.GetChild(i);
                        if (child.name != t.name)
                            continue;
                        if (child == t)
                            index = same;
                        same++;
                    }
                }
                parts.Add(same > 1 ? $"{t.name}[{index}]" : t.name);
            }
            parts.Reverse();
            return "/" + string.Join("/", parts);
        }

        // first visible text under the object, read through any component with a string 'text' property
        public static string Label(GameObject go)
        {
            foreach (var component in go.GetComponentsInChildren<Component>())
            {
                if (component == null)
                    continue;
                var type = component.GetType();
                if (!_textProperty.TryGetValue(type, out var property))
                {
                    property = type.Name.Contains("Text") ? type.GetProperty("text", typeof(string)) : null;
                    _textProperty[type] = property;
                }
                if (property?.GetValue(component) is string text && text.Trim().Length > 0)
                {
                    text = text.Trim().Replace('\n', ' ').Replace('|', '/');
                    return text.Length > MaxLabel ? text.Substring(0, MaxLabel) + "..." : text;
                }
            }
            return string.Empty;
        }

        public static bool IsLabel(string target) => target.Length >= 2 && target[0] == '"' && target[target.Length - 1] == '"';

        public static GameObject? Find(string target)
        {
            if (IsLabel(target))
                return FindByLabel(target.Substring(1, target.Length - 2));
            var parts = target.Trim('/').Split('/');
            if (parts.Length == 0 || parts[0].Length == 0)
                return null;
            foreach (var root in Roots())
            {
                if (!Matches(root.transform, root.transform.parent, parts[0]))
                    continue;
                var current = root.transform;
                for (var i = 1; i < parts.Length && current != null; i++)
                    current = Child(current, parts[i]);
                if (current != null)
                    return current.gameObject;
            }
            return null;
        }

        static GameObject? FindByLabel(string label)
        {
#if MATRIX_UGUI
            // a panel hidden by Canvas.enabled or alpha 0 keeps its buttons active: the shown one wins
            GameObject? active = null, inactive = null;
            foreach (var selectable in UnityEngine.Object.FindObjectsByType<Selectable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!string.Equals(Label(selectable.gameObject), label, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!selectable.gameObject.activeInHierarchy)
                    inactive ??= selectable.gameObject;
                else if (Hidden(selectable.gameObject) == null)
                    return selectable.gameObject;
                else
                    active ??= selectable.gameObject;
            }
            return active ?? inactive;
#else
            return null;
#endif
        }

        static IEnumerable<GameObject> Roots()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                    foreach (var root in scene.GetRootGameObjects())
                        yield return root;
            }
            var dontDestroy = DontDestroyScene();
            if (dontDestroy.HasValue && dontDestroy.Value.isLoaded)
                foreach (var root in dontDestroy.Value.GetRootGameObjects())
                    yield return root;
        }

        // the DontDestroyOnLoad scene is not listed by SceneManager; a hidden probe object reveals it once per run
        static Scene? DontDestroyScene()
        {
            if (_dontDestroyScene.HasValue || !Application.isPlaying)
                return _dontDestroyScene;
            var probe = new GameObject("MxPlaytestProbe") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(probe);
            _dontDestroyScene = probe.scene;
            UnityEngine.Object.DestroyImmediate(probe);
            return _dontDestroyScene;
        }

        static Transform? Child(Transform parent, string part)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (Matches(child, parent, part))
                    return child;
            }
            return null;
        }

        // "Item[2]" is the third sibling named Item, as PathOf writes it
        static bool Matches(Transform t, Transform? parent, string part)
        {
            var open = part.LastIndexOf('[');
            if (open <= 0 || !part.EndsWith("]", StringComparison.Ordinal) ||
                !int.TryParse(part.Substring(open + 1, part.Length - open - 2), out var index))
                return t.name == part;
            var name = part.Substring(0, open);
            if (t.name != name)
                return false;
            if (parent == null)
                return index == 0;
            var same = 0;
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name != name)
                    continue;
                if (child == t)
                    return same == index;
                same++;
            }
            return false;
        }

        public static void HideType(string typeName, List<string> hidden)
        {
#if UNITY_EDITOR
            var type = UnityEditor.TypeCache.GetTypesDerivedFrom<Component>()
                .FirstOrDefault(t => t.FullName == typeName || t.Name == typeName);
#else
            var type = AppDomain.CurrentDomain.GetAssemblies().SelectMany(PlaytestFlow.SafeTypes)
                .FirstOrDefault(t => typeof(Component).IsAssignableFrom(t) && (t.FullName == typeName || t.Name == typeName));
#endif
            if (type == null)
                throw new ArgumentException($"no component type '{typeName}'");
            foreach (var component in UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Exclude, FindObjectsSortMode.None).OfType<Component>())
            {
                component.gameObject.SetActive(false);
                hidden.Add(PathOf(component.gameObject));
            }
        }

        // null when a player can see it: active, every Canvas above it enabled, and an alpha above 0 through its
        // CanvasGroups; otherwise why not. Panels that hide by Canvas.enabled and alpha stay active.
        public static string? Hidden(GameObject go)
        {
            if (!go.activeInHierarchy)
                return "inactive";
            var alpha = 1f;
            var groupsDone = false;
            for (var t = go.transform; t != null; t = t.parent)
            {
                var canvas = t.GetComponent<Canvas>();
                if (canvas != null && !canvas.enabled)
                    return "hidden: Canvas off on " + PathOf(t.gameObject);
                if (groupsDone)
                    continue;
                var group = t.GetComponent<CanvasGroup>();
                if (group == null || !group.enabled)
                    continue;
                alpha *= group.alpha;
                if (alpha <= 0.001f)
                    return "hidden: alpha 0 on " + PathOf(t.gameObject);
                groupsDone = group.ignoreParentGroups;
            }
            return null;
        }

        // changes while anything on screen still moves or fades: CanvasGroup alpha, Canvas on/off, and the place and
        // scale of every nested Canvas and of every panel under a root Canvas; order-free so an unsorted find is fine
        public static long UiSignature()
        {
            unchecked
            {
                long sum = 0;
                foreach (var group in UnityEngine.Object.FindObjectsByType<CanvasGroup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    sum += Mix(group.GetInstanceID(), Mathf.RoundToInt(group.alpha * 1000));
                foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    sum += Mix(canvas.GetInstanceID(), canvas.enabled ? 1 : 0);
                    if (!canvas.isRootCanvas)
                        sum += Mix(canvas.GetInstanceID(), Pose(canvas.transform));
                    else
                        foreach (Transform panel in canvas.transform)
                            if (panel.gameObject.activeSelf)
                                sum += Mix(panel.GetInstanceID(), Pose(panel));
                }
                return sum;
            }
        }

        static long Mix(int id, int value) => unchecked(((long)id * 1000003L) ^ (value * 7919L + 17));

        static int Pose(Transform t)
        {
            var p = t.position;
            var s = t.lossyScale;
            return unchecked(Mathf.RoundToInt(p.x) * 73856093 ^ Mathf.RoundToInt(p.y) * 19349663 ^ Mathf.RoundToInt(s.x * 1000) * 83492791 ^ Mathf.RoundToInt(s.y * 1000));
        }

        static bool _tweenLookupDone;
        static MethodInfo? _playingTweens;
        static MethodInfo? _tweenLoops;

        // DOTween tweens that will end on their own; endless loops (a pulsing badge) never settle and are left out.
        // 0 without DOTween in the project.
        public static int FiniteTweensPlaying()
        {
            if (!_tweenLookupDone)
            {
                _tweenLookupDone = true;
                var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "DOTween");
                var tween = assembly?.GetType("DG.Tweening.Tween");
                _playingTweens = assembly?.GetType("DG.Tweening.DOTween")?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "PlayingTweens" && m.GetParameters().Length == 1);
                _tweenLoops = tween == null ? null : assembly!.GetType("DG.Tweening.TweenExtensions")?.GetMethod("Loops", new[] { tween });
            }
            if (_playingTweens == null)
                return 0;
            if (!(_playingTweens.Invoke(null, new object?[] { null }) is IList list))
                return 0;
            var count = 0;
            foreach (var tween in list)
                if (_tweenLoops == null || (int)_tweenLoops.Invoke(null, new[] { tween }) != -1)
                    count++;
            return count;
        }

        // null when a pointer at the object's center would reach it; otherwise why not
        public static string? Blocker(GameObject go, out Vector2 screen, out Rect screenRect)
        {
            screen = default;
            screenRect = default;
            var hidden = Hidden(go);
            if (hidden != null)
                return hidden;
#if MATRIX_UGUI
            var selectable = go.GetComponent<Selectable>();
            if (selectable != null && !selectable.IsInteractable())
                return "not interactable";
            if (!(go.transform is RectTransform rect))
                return "not a UI object";
            var canvas = go.GetComponentInParent<Canvas>();
            if (canvas == null)
                return "no Canvas above it";
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            screenRect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            screen = screenRect.center;
            if (screen.x < 0 || screen.y < 0 || screen.x > Screen.width || screen.y > Screen.height)
                return "off screen";
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return "no EventSystem";
            _hits.Clear();
            eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = screen }, _hits);
            if (_hits.Count == 0 || _hits[0].gameObject == null)
                return "no raycast target at its center";
            var top = _hits[0].gameObject;
            if (Handler(top) != Handler(go))
                return "covered by " + PathOf(top);
            return null;
#else
            return "uGUI is not installed";
#endif
        }

#if MATRIX_UGUI
        static GameObject Handler(GameObject go) => ExecuteEvents.GetEventHandler<IPointerClickHandler>(go) ?? go;
#endif

        public static void Click(GameObject go, Vector2 screen)
        {
#if MATRIX_UGUI
            var eventSystem = EventSystem.current;
            var handler = Handler(go);
            _hits.Clear();
            eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = screen }, _hits);
            var raycast = _hits.Count > 0 ? _hits[0] : new RaycastResult { gameObject = go, screenPosition = screen };
            var data = new PointerEventData(eventSystem)
            {
                position = screen,
                pressPosition = screen,
                button = PointerEventData.InputButton.Left,
                pointerCurrentRaycast = raycast,
                pointerPressRaycast = raycast,
                clickCount = 1,
                clickTime = Time.unscaledTime,
                eligibleForClick = true,
            };
            ExecuteEvents.Execute(handler, data, ExecuteEvents.pointerEnterHandler);
            data.pointerPress = ExecuteEvents.ExecuteHierarchy(raycast.gameObject ?? go, data, ExecuteEvents.pointerDownHandler) ?? handler;
            data.rawPointerPress = raycast.gameObject ?? go;
            ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(handler, data, ExecuteEvents.pointerClickHandler);
            ExecuteEvents.Execute(handler, data, ExecuteEvents.pointerExitHandler);
#endif
        }
    }
}
