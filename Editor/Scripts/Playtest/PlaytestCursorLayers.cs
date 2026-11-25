#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Feeder.MCP.Editor.Playtest
{
    // what the runner tells the cursor each frame; points and rects are 0..1 of the game screen, top-left origin
    public sealed class PlaytestCursorState
    {
        public Vector2 Position = new Vector2(0.5f, 0.5f);
        public int ClickFrame = -1;
        public Rect? Target;
        public string Caption = string.Empty;
        public string Blocked = string.Empty;
    }

    // maps the 0..1 game screen onto one Game view's overlay, in UI Toolkit points
    public readonly struct PlaytestViewMap
    {
        public PlaytestViewMap(Rect game) => Game = game;

        public Rect Game { get; }
        public Vector2 Point(Vector2 normalized) => new Vector2(Game.x + normalized.x * Game.width, Game.y + normalized.y * Game.height);
        public Rect Area(Rect normalized) => new Rect(Point(normalized.position), new Vector2(normalized.width * Game.width, normalized.height * Game.height));
    }

    // one visual part of the cursor; any non-abstract class with a parameterless constructor in an Editor assembly is
    // picked up, and turned off by its Id in theme.disabledLayers or the 'cursor-layers:' setup line
    public interface IPlaytestCursorLayer
    {
        string Id { get; }
        int Order { get; }
        VisualElement Create(PlaytestCursorTheme theme);
        void Refresh(VisualElement element, PlaytestCursorState state, PlaytestViewMap map, PlaytestCursorTheme theme);
    }

    internal sealed class SpriteCursorLayer : IPlaytestCursorLayer
    {
        public string Id => "sprite";
        public int Order => 100;

        public VisualElement Create(PlaytestCursorTheme theme) => new Image { scaleMode = ScaleMode.StretchToFill };

        public void Refresh(VisualElement element, PlaytestCursorState state, PlaytestViewMap map, PlaytestCursorTheme theme)
        {
            var image = (Image)element;
            var clicking = state.ClickFrame >= 0 && theme.clickSheet != null;
            var texture = clicking ? theme.clickSheet : theme.idle;
            if (image.image != texture)
                image.image = texture;
            var uv = new Rect(0, 0, 1, 1);
            if (clicking)
            {
                var col = state.ClickFrame % theme.columns;
                var row = state.ClickFrame / theme.columns;
                uv = new Rect((float)col / theme.columns, 1f - (row + 1f) / theme.rows, 1f / theme.columns, 1f / theme.rows);
            }
            if (image.uv != uv)
                image.uv = uv;
            var tip = map.Point(state.Position);
            image.style.width = theme.size;
            image.style.height = theme.size;
            image.style.left = tip.x - theme.size * theme.hotspot.x;
            image.style.top = tip.y - theme.size * theme.hotspot.y;
        }
    }

    internal sealed class OutlineCursorLayer : IPlaytestCursorLayer
    {
        public string Id => "outline";
        public int Order => 10;

        public VisualElement Create(PlaytestCursorTheme theme)
        {
            var box = new VisualElement();
            box.style.borderTopWidth = box.style.borderBottomWidth = box.style.borderLeftWidth = box.style.borderRightWidth = 2;
            box.style.borderTopColor = box.style.borderBottomColor = box.style.borderLeftColor = box.style.borderRightColor = theme.accent;
            return box;
        }

        public void Refresh(VisualElement element, PlaytestCursorState state, PlaytestViewMap map, PlaytestCursorTheme theme)
        {
            element.style.display = state.Target.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (!state.Target.HasValue)
                return;
            var area = map.Area(state.Target.Value);
            element.style.left = area.x - 3;
            element.style.top = area.y - 3;
            element.style.width = area.width + 6;
            element.style.height = area.height + 6;
        }
    }

    internal sealed class CaptionCursorLayer : IPlaytestCursorLayer
    {
        public string Id => "caption";
        public int Order => 20;

        public VisualElement Create(PlaytestCursorTheme theme)
        {
            var label = new Label();
            label.style.color = theme.accent;
            label.style.backgroundColor = new Color(0f, 0f, 0f, 0.7f);
            label.style.fontSize = 12;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.paddingLeft = label.style.paddingRight = 6;
            label.style.paddingTop = label.style.paddingBottom = 2;
            return label;
        }

        public void Refresh(VisualElement element, PlaytestCursorState state, PlaytestViewMap map, PlaytestCursorTheme theme)
        {
            var label = (Label)element;
            if (label.text != state.Caption)
                label.text = state.Caption;
            element.style.display = state.Caption.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            element.style.left = map.Game.x + 6;
            element.style.top = map.Game.yMax - 26;
        }
    }

    internal sealed class BlockedCursorLayer : IPlaytestCursorLayer
    {
        public string Id => "blocked";
        public int Order => 30;

        public VisualElement Create(PlaytestCursorTheme theme)
        {
            var label = new Label();
            label.style.color = Color.white;
            label.style.backgroundColor = theme.warning;
            label.style.fontSize = 12;
            label.style.paddingLeft = label.style.paddingRight = 6;
            return label;
        }

        public void Refresh(VisualElement element, PlaytestCursorState state, PlaytestViewMap map, PlaytestCursorTheme theme)
        {
            var label = (Label)element;
            element.style.display = state.Blocked.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (state.Blocked.Length == 0)
                return;
            if (label.text != state.Blocked)
                label.text = state.Blocked;
            var tip = map.Point(state.Position);
            element.style.left = tip.x + 16;
            element.style.top = tip.y + 16;
        }
    }
}
