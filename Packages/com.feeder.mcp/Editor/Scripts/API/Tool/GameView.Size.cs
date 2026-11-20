#nullable enable
using System.ComponentModel;
using System.Linq;
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
        const string EntryPrefix = "Matrix";

        [AiTool
        (
            GameViewSizeToolId,
            Title = "Editor / Game View Size",
            Enabled = false
        )]
        [AiSkillDescription("Set the Game view to a fixed resolution for screenshots and layout checks. Each size stays in the user's Game view size list so they can review the work at it.")]
        [AiSkillBody("Sets the Game view resolution through `PlayModeWindow.SetCustomRenderingResolution`, as a custom entry named `Matrix <width>x<height>`.\n\n" +
            "- `width`, `height` - the resolution in pixels, both above 0.\n" +
            "- Every size gets its own entry and it is kept: the user picks it from the Game view size menu to see what was tested. Setting a size again selects its entry, no duplicate.\n\n" +
            "Answers the size before and after. Other sessions' screenshots see the new size.")]
        [Description("Sets the Game view resolution to width x height and keeps it in the size list as 'Matrix <width>x<height>'. Answers the old and new size.")]
        public ResponseCallTool Size
        (
            [Description("Width in pixels.")]
            int width,
            [Description("Height in pixels.")]
            int height
        )
        {
            return MainThread.Instance.Run(() => Apply(width, height, out var message)
                ? ResponseCallTool.Success(message)
                : ResponseCallTool.Error(message));
        }

        internal static string EntryName(int width, int height) => $"{EntryPrefix} {width}x{height}";

        internal static bool Apply(int width, int height, out string message)
        {
            var view = OpenGameView();
            if (view == null)
            {
                message = "No Game view is open: open Window > General > Game.";
                return false;
            }
            if (width < 1 || height < 1)
            {
                message = "width and height must both be above 0.";
                return false;
            }
            var before = Resolution();
            PlayModeWindow.SetCustomRenderingResolution((uint)width, (uint)height, EntryName(width, height));
            view.Repaint();
            message = $"Game view: {before} -> {Resolution()}, entry '{EntryName(width, height)}' kept in the size list.";
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
