#nullable enable
using System.ComponentModel;
using System.Linq;
using Feeder.MCP.Data;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_UI
    {
        public const string UiInspectWindowsToolId = "ui-inspect-windows";

        [AiTool
        (
            UiInspectWindowsToolId,
            Title = "UI / Inspect Windows",
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [AiSkillDescription("List every EditorWindow currently alive in the Unity Editor — type, title, rect, focus, " +
            "dock state, and whether it has a UI Toolkit element tree. Entry point for the ui-inspect tool family: " +
            "pick a window here, then drill in with '" + UiInspectTreeToolId + "'.")]
        [AiSkillBody("Lists all open EditorWindows so the AI can pick one to inspect.\n\n" +
            "## Output\n\n" +
            "For each window: full type name (use as `windowName` in the other ui-inspect tools), tab title, " +
            "desktop rect, focus/dock state, and `RootChildCount` — when it is 0 the window is pure IMGUI and " +
            "its content cannot be traversed by the UI inspector.")]
        [Description("Lists all EditorWindows currently alive in the Unity Editor: type name, title, position, size, " +
            "focus and dock state, and whether the window has a UI Toolkit element tree (RootChildCount > 0). " +
            "Use the returned type name as 'windowName' for the other ui-inspect tools.")]
        public UiWindowsResult InspectWindows()
        {
            return MainThread.Instance.Run(() =>
            {
                var windows = Resources.FindObjectsOfTypeAll<EditorWindow>()
                    .Where(w => w != null)
                    .Select(w => new UiWindowInfo
                    {
                        TypeName = w.GetType().FullName,
                        Title = w.titleContent?.text,
                        X = w.position.x,
                        Y = w.position.y,
                        Width = w.position.width,
                        Height = w.position.height,
                        Focused = w == EditorWindow.focusedWindow,
                        Docked = w.docked,
                        RootChildCount = w.rootVisualElement?.hierarchy.childCount ?? 0
                    })
                    .OrderByDescending(w => w.RootChildCount)
                    .ToArray();

                return new UiWindowsResult
                {
                    Windows = windows,
                    Count = windows.Length
                };
            });
        }
    }
}
