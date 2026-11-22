#nullable enable

using System;
using UnityEngine.UIElements;

namespace Feeder.MCP.Editor.UI.Controls
{
    /// <summary>
    /// Bottom-right toast stack for transient notifications, with an optional action
    /// button (e.g. UNDO). Add one instance to the window root; it ignores pointer
    /// events except on the toasts themselves.
    /// </summary>
    public sealed class ToastLayer : VisualElement
    {
        private const int DefaultDurationMs = 4000;

        public ToastLayer()
        {
            AddToClassList("toast-layer");
            pickingMode = PickingMode.Ignore;
        }

        public void Show(string message, string? actionLabel = null, Action? action = null,
            int durationMs = DefaultDurationMs)
        {
            var toast = new VisualElement();
            toast.AddToClassList("toast");

            var text = new Label(message);
            text.AddToClassList("toast-text");
            toast.Add(text);

            if (actionLabel != null && action != null)
            {
                var button = new Button(() =>
                {
                    action();
                    Dismiss(toast);
                }) { text = actionLabel };
                button.AddToClassList("btn-primary");
                button.AddToClassList("btn-compact");
                toast.Add(button);
            }

            var close = new Button(() => Dismiss(toast)) { text = "✕" };
            close.AddToClassList("btn-tertiary");
            toast.Add(close);

            Add(toast);
            // Class added a frame later so the opacity/translate transition plays.
            toast.schedule.Execute(() => toast.AddToClassList("toast-visible"));
            toast.schedule.Execute(() => Dismiss(toast)).StartingIn(durationMs);
        }

        private static void Dismiss(VisualElement toast)
        {
            if (toast.parent == null)
                return;
            toast.RemoveFromClassList("toast-visible");
            toast.schedule.Execute(() => toast.RemoveFromHierarchy()).StartingIn(180);
        }
    }
}
