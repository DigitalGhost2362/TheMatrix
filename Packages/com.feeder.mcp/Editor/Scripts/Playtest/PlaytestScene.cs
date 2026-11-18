#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Feeder.MCP.Editor.Watch;
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

        public static void Reset() => _dontDestroyScene = null;

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
            GameObject? inactive = null;
            foreach (var selectable in UnityEngine.Object.FindObjectsByType<Selectable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!string.Equals(WatchTargets.Label(selectable.gameObject), label, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (selectable.gameObject.activeInHierarchy)
                    return selectable.gameObject;
                inactive ??= selectable.gameObject;
            }
            return inactive;
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

        // "Item[2]" is the third sibling named Item, as WatchTargets.PathOf writes it
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
            var type = UnityEditor.TypeCache.GetTypesDerivedFrom<Component>()
                .FirstOrDefault(t => t.FullName == typeName || t.Name == typeName);
            if (type == null)
                throw new ArgumentException($"no component type '{typeName}'");
            foreach (var component in UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Exclude, FindObjectsSortMode.None).OfType<Component>())
            {
                component.gameObject.SetActive(false);
                hidden.Add(WatchTargets.PathOf(component.gameObject));
            }
        }

        // null when a pointer at the object's center would reach it; otherwise why not
        public static string? Blocker(GameObject go, out Vector2 screen, out Rect screenRect)
        {
            screen = default;
            screenRect = default;
            if (!go.activeInHierarchy)
                return "inactive";
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
                return "covered by " + WatchTargets.PathOf(top);
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
