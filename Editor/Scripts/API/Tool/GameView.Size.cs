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
            var before = Describe(view);
            PlayModeWindow.SetCustomRenderingResolution((uint)width, (uint)height, EntryName(width, height));
            view.Repaint();
            message = $"Game view: {before} -> {Describe(view)}, kept in the size list.";
            return true;
        }

        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

        // the size the view renders at now; PlayModeWindow.GetRenderingResolution lags behind it until the view
        // repaints, which an unfocused Unity may not do for a while
        internal static Vector2Int TargetSize(EditorWindow view)
        {
            var size = (Vector2)view.GetType().GetProperty("targetRenderSize", Any)!.GetValue(view);
            return new Vector2Int(Mathf.RoundToInt(size.x), Mathf.RoundToInt(size.y));
        }

        internal static string Resolution()
        {
            var view = OpenGameView();
            if (view == null)
                return "unknown (no Game view)";
            var size = TargetSize(view);
            return $"{size.x}x{size.y}";
        }

        // size plus the selected entry's name, so a fixed entry that renders at another size is not mistaken for a failure
        static string Describe(EditorWindow view)
        {
            var sizesType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizes");
            var sizes = sizesType?.GetProperty("instance", Any)?.GetValue(null);
            var group = sizes == null ? null : sizesType!.GetProperty("currentGroup", Any)?.GetValue(sizes);
            var index = (int)view.GetType().GetProperty("selectedSizeIndex", Any)!.GetValue(view);
            var entry = group?.GetType().GetMethod("GetGameViewSize")?.Invoke(group, new object[] { index });
            var name = entry?.GetType().GetProperty("baseText")?.GetValue(entry) as string;
            var size = TargetSize(view);
            return string.IsNullOrEmpty(name) ? $"{size.x}x{size.y}" : $"{size.x}x{size.y} ({name})";
        }

        internal static EditorWindow? OpenGameView()
        {
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            return type == null ? null : Resources.FindObjectsOfTypeAll(type).OfType<EditorWindow>().FirstOrDefault();
        }
    }
}
