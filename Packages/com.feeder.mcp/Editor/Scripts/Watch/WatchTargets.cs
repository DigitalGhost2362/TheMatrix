#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
#if MATRIX_UGUI
using UnityEngine.EventSystems;
using UnityEngine.UI;
#endif

namespace Feeder.MCP.Editor.Watch
{
    internal readonly struct WatchTarget
    {
        public WatchTarget(string path, string detail, Vector3 point)
        {
            Path = path;
            Detail = detail;
            Point = point;
        }

        public string Path { get; }
        // kind|label|persistent listeners|game components
        public string Detail { get; }
        public Vector3 Point { get; }
    }

    // what a pointer press landed on: the uGUI handler first, then a 3D or 2D collider under the camera
    internal static class WatchTargets
    {
        const int MaxComponents = 4;
        const int MaxLabel = 40;

        static readonly Dictionary<Type, PropertyInfo?> _textProperty = new Dictionary<Type, PropertyInfo?>();
#if MATRIX_UGUI
        static readonly List<RaycastResult> _results = new List<RaycastResult>();
#endif

        public static WatchTarget Resolve(Vector2 screen)
        {
#if MATRIX_UGUI
            var eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                _results.Clear();
                eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = screen }, _results);
                if (_results.Count > 0 && _results[0].gameObject != null)
                {
                    var hit = _results[0].gameObject;
                    var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit)
                        ?? ExecuteEvents.GetEventHandler<IPointerDownHandler>(hit)
                        ?? ExecuteEvents.GetEventHandler<IBeginDragHandler>(hit)
                        ?? hit;
                    return new WatchTarget(PathOf(handler), Detail("ui", Label(handler), UiListeners(handler), handler), Vector3.zero);
                }
            }
#endif
            var camera = Camera.main != null ? Camera.main : Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null;
            if (camera == null)
                return new WatchTarget(string.Empty, "none", Vector3.zero);

            var ray = camera.ScreenPointToRay(screen);
            if (Physics.Raycast(ray, out var hit3D, 5000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                var go = hit3D.collider.gameObject;
                return new WatchTarget(PathOf(go), Detail("world", string.Empty, string.Empty, go), hit3D.point);
            }
            var hit2D = Physics2D.GetRayIntersection(ray, 5000f);
            if (hit2D.collider != null)
            {
                var go = hit2D.collider.gameObject;
                return new WatchTarget(PathOf(go), Detail("world2d", string.Empty, string.Empty, go), hit2D.point);
            }
            return new WatchTarget(string.Empty, "none", Vector3.zero);
        }

        // same-named siblings get their index, so "/Grid/Item[2]" points at one object
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

        public static string GameComponents(GameObject go) => string.Join(",", go.GetComponents<MonoBehaviour>()
            .Where(component => component != null && !IsEngineType(component.GetType()))
            .Select(component => component.GetType().Name)
            .Distinct()
            .Take(MaxComponents));

        static string Detail(string kind, string label, string listeners, GameObject go) =>
            $"{kind}|{label}|{listeners}|{GameComponents(go)}";

        static bool IsEngineType(Type type)
        {
            var ns = type.Namespace ?? string.Empty;
            return ns.StartsWith("UnityEngine", StringComparison.Ordinal) || ns.StartsWith("TMPro", StringComparison.Ordinal);
        }

#if MATRIX_UGUI
        static string UiListeners(GameObject go)
        {
            var button = go.GetComponent<Button>();
            if (button != null)
                return Listeners(button.onClick);
            var toggle = go.GetComponent<Toggle>();
            return toggle != null ? Listeners(toggle.onValueChanged) : string.Empty;
        }
#endif

        static string Listeners(UnityEventBase unityEvent)
        {
            var names = new List<string>();
            for (var i = 0; i < unityEvent.GetPersistentEventCount(); i++)
            {
                var method = unityEvent.GetPersistentMethodName(i);
                if (string.IsNullOrEmpty(method))
                    continue;
                var target = unityEvent.GetPersistentTarget(i);
                names.Add(target != null ? $"{target.GetType().Name}.{method}" : method);
            }
            return string.Join(",", names);
        }

        // first visible text under the object, read through any component with a string 'text' property
        static string Label(GameObject go)
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
    }
}
