#nullable enable
using System;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Playtest
{
    // the look of the playtest cursor; a project points 'cursor-theme:' in its Playtest.md at its own asset
    [CreateAssetMenu(menuName = "Matrix/Playtest Cursor Theme", fileName = "PlaytestCursorTheme")]
    public sealed class PlaytestCursorTheme : ScriptableObject
    {
        const string DefaultFolder = "Packages/com.feeder.mcp/Editor/Playtest/Cursor/";

        [Tooltip("Shown while the cursor rests or moves.")]
        public Texture2D? idle;
        [Tooltip("Click animation laid out left to right, top to bottom.")]
        public Texture2D? clickSheet;
        public int columns = 5;
        public int rows = 4;
        public int frames = 20;
        public float fps = 30f;
        [Tooltip("Frame of the click sheet on which the click reaches the game.")]
        public int hitFrame = 3;
        [Tooltip("Drawn size of one frame in Game view points.")]
        public float size = 160f;
        [Tooltip("Where the pointer tip sits inside a frame, 0..1 from the top-left corner.")]
        public Vector2 hotspot = new Vector2(0.5f, 0.5f);
        public float moveMinMs = 250f;
        public float moveMaxMs = 650f;
        [Tooltip("Extra move time per Game view point travelled.")]
        public float moveMsPerPoint = 0.9f;
        public Color accent = new Color(0f, 1f, 0.4f, 1f);
        public Color warning = new Color(1f, 0.25f, 0.25f, 1f);
        [Tooltip("Layer ids to leave out, e.g. caption, outline, blocked, sprite.")]
        public string[] disabledLayers = Array.Empty<string>();

        public static PlaytestCursorTheme Load(string? assetPath)
        {
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                var custom = AssetDatabase.LoadAssetAtPath<PlaytestCursorTheme>(assetPath);
                if (custom != null)
                    return custom;
                Debug.LogWarning($"[Matrix Playtest] cursor-theme '{assetPath}' is not a PlaytestCursorTheme asset; using the default cursor.");
            }
            var theme = CreateInstance<PlaytestCursorTheme>();
            theme.hideFlags = HideFlags.HideAndDontSave;
            theme.idle = AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultFolder + "Glitch_Idle.png");
            theme.clickSheet = AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultFolder + "Glitch_Click_sheet.png");
            return theme;
        }
    }
}
