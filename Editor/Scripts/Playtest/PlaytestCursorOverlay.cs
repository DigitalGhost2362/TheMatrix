#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Feeder.MCP.Editor.Activity;

namespace Feeder.MCP.Editor.Playtest
{
    // draws the cursor as UI Toolkit elements on top of every Game view window: nothing enters the scene, and a
    // resting cursor changes no style, so the window does not repaint for it. Once shown it stays, idle where it
    // last clicked, until play mode ends, also across runs and across Game view rebuilds.
    [InitializeOnLoad]
    internal static class PlaytestCursorOverlay
    {
        const string RootName = "matrix-playtest-cursor";
        const string PersistKey = "Feeder_Matrix_PlaytestCursor";
        const char Sep = '';
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        static readonly Type? _gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
        static readonly PropertyInfo? _viewInWindow = _gameViewType?.GetProperty("viewInWindow", Instance);
        static readonly PropertyInfo? _targetInView = _gameViewType?.GetProperty("targetInView", Instance);

        static PlaytestCursorTheme? _theme;
        static string _key = string.Empty;
        static List<IPlaytestCursorLayer> _layers = new List<IPlaytestCursorLayer>();
        static double _nextAttach;
        static EditorWindow[] _views = Array.Empty<EditorWindow>();

        public static bool Visible => _theme != null;

        // shared by every run of the play session, so a new run starts where the last one left the cursor
        public static PlaytestCursorState State { get; } = new PlaytestCursorState();

        static PlaytestCursorOverlay()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;
            // a domain reload inside play mode drops the statics; bring the cursor back where it was
            var saved = SessionState.GetString(PersistKey, string.Empty).Split(Sep);
            if (saved.Length == 4)
                EditorEvents.NextTick(() =>
                {
                    if (!EditorApplication.isPlaying)
                    {
                        SessionState.EraseString(PersistKey);
                        return;
                    }
                    State.Position = new Vector2(Parse(saved[2]), Parse(saved[3]));
                    Show(saved[0], saved[1]);
                });
        }

        public static void Show(string themePath, string onlyLayers)
        {
            var key = themePath + Sep + onlyLayers;
            if (_theme != null && key == _key)
                return;
            RemoveElements();
            var theme = PlaytestCursorTheme.Load(themePath);
            var only = string.IsNullOrWhiteSpace(onlyLayers)
                ? null
                : onlyLayers.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            _layers = TypeCache.GetTypesDerivedFrom<IPlaytestCursorLayer>()
                .Where(t => !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(Create)
                .Where(layer => layer != null && Enabled(layer!.Id, only, theme))
                .Select(layer => layer!)
                .OrderBy(layer => layer.Order)
                .ToList();
            _theme = theme;
            _key = key;
            _nextAttach = 0;
            Persist();
            EditorApplication.update -= Keep;
            EditorApplication.update += Keep;
            PlaySessionGuard.Arm(Hide);
        }

        public static PlaytestCursorTheme? Theme => _theme;

        public static void Hide()
        {
            EditorApplication.update -= Keep;
            _theme = null;
            _key = string.Empty;
            SessionState.EraseString(PersistKey);
            State.ClickFrame = -1;
            State.Target = null;
            State.Caption = State.Blocked = string.Empty;
            RemoveElements();
        }

        // between runs: re-attaches to Game views that rebuilt their UI (maximize, layout change, play start)
        static void Keep()
        {
            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Hide();
                return;
            }
            if (EditorApplication.timeSinceStartup >= _nextAttach)
                Refresh();
        }

        public static void Refresh()
        {
            if (_theme == null)
                return;
            var now = EditorApplication.timeSinceStartup;
            var attach = now >= _nextAttach;
            if (attach)
            {
                _nextAttach = now + 0.5;
                _views = GameViews().ToArray();
                Persist();
            }
            foreach (var view in _views)
            {
                if (view == null)
                    continue;
                var root = view.rootVisualElement.Q(RootName);
                if (root == null)
                {
                    if (!attach)
                        continue;
                    root = Build();
                    view.rootVisualElement.Add(root);
                }
                else if (attach && root.parent.IndexOf(root) != root.parent.childCount - 1)
                    root.BringToFront();
                var map = new PlaytestViewMap(GameRect(view));
                for (var i = 0; i < _layers.Count && i < root.childCount; i++)
                    _layers[i].Refresh(root[i], State, map, _theme);
            }
        }

        static void RemoveElements()
        {
            foreach (var view in GameViews())
                view.rootVisualElement.Q(RootName)?.RemoveFromHierarchy();
        }

        static void Persist() => SessionState.SetString(PersistKey, string.Join(Sep.ToString(), _key,
            State.Position.x.ToString(System.Globalization.CultureInfo.InvariantCulture),
            State.Position.y.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        static float Parse(string value) =>
            float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0.5f;

        // screen point (pixels, bottom-left origin) -> 0..1 of the game screen, top-left origin
        public static Vector2 Normalize(Vector2 screen) => new Vector2(
            Screen.width > 0 ? screen.x / Screen.width : 0.5f,
            Screen.height > 0 ? 1f - screen.y / Screen.height : 0.5f);

        public static Rect Normalize(Rect screen)
        {
            var min = Normalize(new Vector2(screen.xMin, screen.yMax));
            var max = Normalize(new Vector2(screen.xMax, screen.yMin));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        // the drawn game image inside the window, in points; letterbox and zoom included
        public static Rect GameRect(EditorWindow view)
        {
            if (_viewInWindow?.GetValue(view) is Rect inWindow && _targetInView?.GetValue(view) is Rect target && target.width > 0)
                return new Rect(inWindow.x + target.x, inWindow.y + target.y, target.width, target.height);
            var layout = view.rootVisualElement.layout;
            return float.IsNaN(layout.width) ? new Rect(0, 0, view.position.width, view.position.height) : layout;
        }

        public static float PointsPerScreenPixel()
        {
            var view = GameViews().FirstOrDefault();
            return view == null || Screen.width <= 0 ? 1f : GameRect(view).width / Screen.width;
        }

        static VisualElement Build()
        {
            var root = new VisualElement { name = RootName, pickingMode = PickingMode.Ignore };
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            foreach (var layer in _layers)
            {
                VisualElement element;
                try { element = layer.Create(_theme!); }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    element = new VisualElement();
                }
                element.style.position = Position.Absolute;
                element.pickingMode = PickingMode.Ignore;
                root.Add(element);
            }
            return root;
        }

        static IPlaytestCursorLayer? Create(Type type)
        {
            try { return (IPlaytestCursorLayer)Activator.CreateInstance(type); }
            catch (Exception e)
            {
                Debug.LogWarning($"[Matrix Playtest] cursor layer {type.Name} could not be created: {e.Message}");
                return null;
            }
        }

        static bool Enabled(string id, string[]? only, PlaytestCursorTheme theme) =>
            (only == null || only.Contains(id, StringComparer.OrdinalIgnoreCase)) &&
            !theme.disabledLayers.Contains(id, StringComparer.OrdinalIgnoreCase);

        public static IEnumerable<EditorWindow> GameViews() => _gameViewType == null
            ? Enumerable.Empty<EditorWindow>()
            : Resources.FindObjectsOfTypeAll(_gameViewType).OfType<EditorWindow>();
    }
}
