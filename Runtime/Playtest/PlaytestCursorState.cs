#nullable enable
using UnityEngine;

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
}
