#nullable enable
using System;
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;
using Feeder.Reflect.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_UI
    {
        public const string UiInspectPickToolId = "ui-inspect-pick";

        // Only one pick session may run at a time; starting a new one finishes the previous
        // request so its client is not left waiting forever.
        private static Action? _cancelActivePick;

        [AiTool
        (
            UiInspectPickToolId,
            Title = "UI / Inspect Pick",
            ReadOnlyHint = false,
            IdempotentHint = false
        )]
        [AiSkillDescription("Interactive element picker, like the web DevTools inspect cursor: activates pick mode on an " +
            "open EditorWindow, the user hovers (live highlight) and clicks the misbehaving element, and the tool " +
            "returns that element's full detail (index path, styles, detected issues) via the deferred requestId result.")]
        [AiSkillBody("Lets the USER point at the broken element instead of describing it or sending screenshots.\n\n" +
            "## Flow\n\n" +
            "1. Call with `windowName` (and a `requestId`) — the tool returns Processing immediately.\n" +
            "2. The window shows a highlight overlay following the pointer with the hovered element's identity.\n" +
            "3. The user clicks an element (the click is swallowed, no button is triggered). Esc cancels; a timeout " +
            "(default 60s) also ends the session.\n" +
            "4. The final result — the same detail block as '" + UiInspectElementToolId + "' — is delivered through the requestId.\n\n" +
            "Note: avoid triggering script recompilation while a pick session is active; a domain reload discards it.")]
        [Description("Starts interactive pick mode on an open EditorWindow (like the web DevTools element picker). " +
            "A highlight overlay follows the pointer; when the user clicks an element, its full detail " +
            "(index path, resolved styles, detected layout issues, ancestors, style sheets) is returned via the " +
            "deferred requestId result. The click is swallowed so no button fires. Esc or the timeout cancels. " +
            "Ask the user to click the misrendered element after calling this.")]
        public ResponseCallTool InspectPick
        (
            [Description("EditorWindow class name, full type name, or tab title. Use 'ui-inspect-windows' to list them.")]
            string windowName,
            [Description("Seconds before pick mode auto-cancels. Default 60, max 600.")]
            int timeoutSeconds = 60,
            [RequestID]
            string? requestId = null
        )
        {
            if (string.IsNullOrWhiteSpace(requestId))
                return ResponseCallTool.Error("[Error] Original request with valid RequestID must be provided.");

            timeoutSeconds = Mathf.Clamp(timeoutSeconds, 5, 600);

            return MainThread.Instance.Run(() =>
            {
                EditorWindow window;
                try
                {
                    window = FindWindowByName(windowName);
                }
                catch (Exception ex)
                {
                    return ResponseCallTool.Error(ex.Message).SetRequestID(requestId);
                }

                var root = window.rootVisualElement;
                if (root == null || root.panel == null)
                    return ResponseCallTool.Error(
                        $"Window '{window.GetType().Name}' has no live UI Toolkit panel — cannot pick.")
                        .SetRequestID(requestId);

                _cancelActivePick?.Invoke();

                window.Focus();

                var overlay = new VisualElement { name = "feeder-ui-inspect-pick-overlay", pickingMode = PickingMode.Ignore };
                overlay.style.position = Position.Absolute;
                overlay.style.left = 0;
                overlay.style.top = 0;
                overlay.style.right = 0;
                overlay.style.bottom = 0;

                var highlight = new VisualElement { pickingMode = PickingMode.Ignore };
                highlight.style.position = Position.Absolute;
                highlight.style.display = DisplayStyle.None;
                highlight.style.backgroundColor = new Color(0.31f, 0.79f, 0.69f, 0.18f);
                var borderColor = new Color(0.31f, 0.79f, 0.69f, 1f);
                highlight.style.borderLeftColor = borderColor;
                highlight.style.borderRightColor = borderColor;
                highlight.style.borderTopColor = borderColor;
                highlight.style.borderBottomColor = borderColor;
                highlight.style.borderLeftWidth = 1;
                highlight.style.borderRightWidth = 1;
                highlight.style.borderTopWidth = 1;
                highlight.style.borderBottomWidth = 1;

                var infoLabel = new Label($"Pick mode: click an element to inspect it (Esc cancels, timeout {timeoutSeconds}s)");
                infoLabel.pickingMode = PickingMode.Ignore;
                infoLabel.style.position = Position.Absolute;
                infoLabel.style.top = 4;
                infoLabel.style.left = 4;
                infoLabel.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.92f);
                infoLabel.style.color = Color.white;
                infoLabel.style.fontSize = 11;
                infoLabel.style.paddingLeft = 6;
                infoLabel.style.paddingRight = 6;
                infoLabel.style.paddingTop = 3;
                infoLabel.style.paddingBottom = 3;

                overlay.Add(highlight);
                overlay.Add(infoLabel);
                root.Add(overlay);

                var done = false;
                IVisualElementScheduledItem? timeoutItem = null;
                EventCallback<PointerMoveEvent>? onMove = null;
                EventCallback<PointerDownEvent>? onDown = null;
                EventCallback<KeyDownEvent>? onKey = null;

                VisualElement? PickAt(Vector2 panelPosition)
                {
                    var picked = root.panel?.Pick(panelPosition);
                    if (picked == null || picked == overlay || overlay.Contains(picked))
                        return null;
                    return picked;
                }

                void Finish(ResponseCallTool result)
                {
                    if (done)
                        return;
                    done = true;
                    _cancelActivePick = null;
                    timeoutItem?.Pause();
                    root.UnregisterCallback(onMove!, TrickleDown.TrickleDown);
                    root.UnregisterCallback(onDown!, TrickleDown.TrickleDown);
                    root.UnregisterCallback(onKey!, TrickleDown.TrickleDown);
                    overlay.RemoveFromHierarchy();
                    window.Repaint();
                    _ = MatrixPluginEditor.NotifyToolRequestCompleted(new RequestToolCompletedData
                    {
                        RequestId = requestId!,
                        Result = result.SetRequestID(requestId!)
                    });
                }

                onMove = evt =>
                {
                    var picked = PickAt(evt.position);
                    if (picked == null)
                    {
                        highlight.style.display = DisplayStyle.None;
                        return;
                    }
                    var bound = picked.worldBound;
                    var rootBound = root.worldBound;
                    highlight.style.display = DisplayStyle.Flex;
                    highlight.style.left = bound.x - rootBound.x;
                    highlight.style.top = bound.y - rootBound.y;
                    highlight.style.width = bound.width;
                    highlight.style.height = bound.height;
                    infoLabel.text = $"{DescribeShort(picked)}  [{GetIndexPath(picked, root)}]  ({FormatRect(bound)})";
                };

                onDown = evt =>
                {
                    evt.StopImmediatePropagation();
                    var picked = PickAt(evt.position);
                    if (picked == null)
                        return;
                    var detail = BuildElementDetail(window, picked, root);
                    Finish(ResponseCallTool.Success("[Picked by user]\n" + detail));
                };

                onKey = evt =>
                {
                    if (evt.keyCode != KeyCode.Escape)
                        return;
                    evt.StopImmediatePropagation();
                    Finish(ResponseCallTool.Success(
                        "[Cancelled] Pick mode was cancelled with Esc — no element was selected."));
                };

                root.RegisterCallback(onMove, TrickleDown.TrickleDown);
                root.RegisterCallback(onDown, TrickleDown.TrickleDown);
                root.RegisterCallback(onKey, TrickleDown.TrickleDown);

                timeoutItem = root.schedule
                    .Execute(() => Finish(ResponseCallTool.Success(
                        $"[Timeout] No element was picked within {timeoutSeconds}s — pick mode ended.")))
                    .StartingIn(timeoutSeconds * 1000L);

                _cancelActivePick = () => Finish(ResponseCallTool.Success(
                    "[Cancelled] Pick session was superseded by a newer ui-inspect-pick call."));

                window.Repaint();

                return ResponseCallTool.Processing(
                        $"Pick mode is ON in '{window.GetType().Name}'. Ask the user to click the problematic element "
                        + $"(Esc cancels, auto-timeout in {timeoutSeconds}s). The element detail arrives via this requestId.")
                    .SetRequestID(requestId);
            });
        }
    }
}
