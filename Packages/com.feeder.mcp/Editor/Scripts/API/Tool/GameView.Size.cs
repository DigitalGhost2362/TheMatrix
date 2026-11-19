#nullable enable
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Feeder.Reflect.Utils;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_GameView
    {
        public const string GameViewSizeToolId = "game-view-size";
        const string EntryName = "Matrix";
        // SessionState survives the domain reload of a compile or of entering play mode
        const string BeforeKey = "Feeder.MCP.GameViewSize.Before";

        [AiTool
        (
            GameViewSizeToolId,
            Title = "Editor / Game View Size",
            Enabled = false
        )]
        [AiSkillDescription("Set the Game view to a fixed resolution for screenshots and layout checks, then put back the size that was selected before.")]
        [AiSkillBody("Sets the Game view resolution through `PlayModeWindow.SetCustomRenderingResolution` (a custom entry named `Matrix`).\n\n" +
            "- `width`, `height` - the resolution in pixels. The size selected before the first change is remembered until restored, across domain reloads.\n" +
            "- Both `0` - selects that earlier entry again (for example Free Aspect) and forgets it.\n\n" +
            "Answers the size before and after. Other sessions' screenshots see the new size, so restore when done.")]
        [Description("Sets the Game view resolution to width x height; both 0 puts back the size selected before the first change. Answers the old and new size.")]
        public ResponseCallTool Size
        (
            [Description("Width in pixels. 0 together with height 0 restores the earlier size.")]
            int width = 0,
            [Description("Height in pixels. 0 together with width 0 restores the earlier size.")]
            int height = 0
        )
        {
            return MainThread.Instance.Run(() => Apply(width, height, out var message)
                ? ResponseCallTool.Success(message)
                : ResponseCallTool.Error(message));
        }

        internal static bool Apply(int width, int height, out string message)
        {
            var view = OpenGameView();
            if (view == null)
            {
                message = "No Game view is open: open Window > General > Game.";
                return false;
            }
            var before = Resolution();
            if (width == 0 && height == 0)
            {
                var saved = SessionState.GetInt(BeforeKey, -1);
                if (saved < 0)
                {
                    message = $"Nothing to restore; the Game view is {before}.";
                    return true;
                }
                view.GetType().GetMethod("SizeSelectionCallback", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                    .Invoke(view, new object?[] { saved, null });
                SessionState.EraseInt(BeforeKey);
                view.Repaint();
                message = $"Game view restored: {before} -> {Resolution()}.";
                return true;
            }
            if (width < 1 || height < 1)
            {
                message = "width and height must both be positive, or both 0 to restore.";
                return false;
            }
            if (SessionState.GetInt(BeforeKey, -1) < 0)
                SessionState.SetInt(BeforeKey, (int)view.GetType().GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(view));
            PlayModeWindow.SetCustomRenderingResolution((uint)width, (uint)height, EntryName);
            view.Repaint();
            message = $"Game view: {before} -> {Resolution()}. Call with width=0, height=0 to put back the earlier size.";
            return true;
        }

        internal static string Resolution()
        {
            PlayModeWindow.GetRenderingResolution(out var width, out var height);
            return $"{width}x{height}";
        }

        static EditorWindow? OpenGameView()
        {
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            return type == null ? null : Resources.FindObjectsOfTypeAll(type).OfType<EditorWindow>().FirstOrDefault();
        }
    }
}
