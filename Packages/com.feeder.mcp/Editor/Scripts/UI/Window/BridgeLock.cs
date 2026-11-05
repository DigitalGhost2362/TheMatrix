#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Feeder.MCP.Editor.Matrix;
using Feeder.Rx;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Feeder.MCP.Editor.UI
{
    /// <summary>
    /// The editor lock as the matrix reports it: which agent session (or lock file) holds the Unity editor,
    /// for what and since when, plus the menu to free a claim.
    /// </summary>
    internal static class BridgeLock
    {
        public const string KindAgent = "agent";
        public const string KindOutside = "outside";

        static readonly TimeSpan Cap = TimeSpan.FromMinutes(15);

        // null: not linked, or the server does not keep the lock
        static readonly ReactiveProperty<EditorLockState?> _state = new(null);

        public static ReadOnlyReactiveProperty<EditorLockState?> State => _state;

        internal static void Publish(EditorLockState? state) => _state.Value = state;

        public static bool IsPastCap(EditorLockState state) => state.Held && Age(state.AcquiredUnixMs) > Cap;

        /// <summary>The holder is past the cap while others wait for the editor.</summary>
        public static bool IsBlocking(EditorLockState state) => IsPastCap(state) && state.Waiting.Length > 0;

        /// <summary>One short line: who, for what, for how long.</summary>
        public static string Line(EditorLockState state)
        {
            if (!state.Held)
                return state.Waiting.Length > 0 ? $"editor free  -  {state.Waiting.Length} waiting" : "editor free";
            var age = Format(Age(state.AcquiredUnixMs));
            var blocking = IsBlocking(state) ? $"  -  OVER THE CAP, {state.Waiting.Length} waiting" : string.Empty;
            return state.Kind switch
            {
                KindOutside => $"{Name(state)} (lock file)  -  {Task(state)}{age}{blocking}",
                _ => $"{Name(state)}  -  {Task(state)}{age}{blocking}",
            };
        }

        public static string Tooltip(EditorLockState state)
        {
            if (!state.Held)
                return "No agent holds the Unity editor: it is yours. Agents claim it for each compile or play test.";

            var text = new StringBuilder();
            text.AppendLine(state.Kind switch
            {
                KindOutside => $"Held by {Name(state)} through Temp/agent-editor-lock (a session claiming with the lock file).",
                _ => $"Held by agent {Name(state)}. Other sessions only run read-only tools.",
            });
            var client = $"{state.ClientName} {state.ClientVersion}".Trim();
            if (client.Length > 0)
                text.AppendLine($"Client: {client}" + (state.SessionId.Length > 0 ? $", session {Short(state.SessionId)}" : string.Empty));
            if (state.Task.Length > 0)
                text.AppendLine($"Task: {state.Task}");
            text.Append($"Since: {Clock(state.AcquiredUnixMs)} ({Format(Age(state.AcquiredUnixMs))})");
            text.AppendLine(IsPastCap(state) ? ", past the 15-minute cap" : string.Empty);
            if (state.Kind == KindAgent)
                text.AppendLine(state.CallsInFlight > 0
                    ? $"Running: {state.CallsInFlight} call(s)"
                    : $"Last call: {Format(Age(state.LastActivityUnixMs))} ago");
            else if (state.Kind == KindOutside && state.LastActivityUnixMs > 0)
                text.AppendLine($"Last heartbeat: {Format(Age(state.LastActivityUnixMs))} ago");
            if (state.ExpiresUnixMs > 0)
                text.AppendLine($"Freed on its own at {Clock(state.ExpiresUnixMs)} if it stays idle");
            if (state.Waiting.Length > 0)
                text.AppendLine($"Waiting: {state.Waiting.Length} (the first gets the editor when it is freed)");
            text.Append("Click for options.");
            return text.ToString();
        }

        public static void ShowMenu(Rect at)
        {
            var state = _state.Value;
            var menu = new GenericMenu();
            if (state == null || (!state.Held && state.Waiting.Length == 0 && !state.QueuePaused))
                return;
            if (state.Held)
                menu.AddItem(new GUIContent($"Free the claim of {Name(state)}..."), false, ConfirmRelease);
            menu.AddItem(new GUIContent(state.QueuePaused ? "Resume the queue" : "Pause the queue"), false, () => SetPaused(!state.QueuePaused));
            menu.AddItem(new GUIContent("Copy details"), false, CopyDetails);
            menu.DropDown(at);
        }

        static void ConfirmRelease()
        {
            var state = _state.Value;
            if (state == null || !state.Held)
                return;
            var ok = EditorUtility.DisplayDialog(
                "Free the editor lock",
                $"{Name(state)} holds the editor for \"{state.Task}\".\n\nIt may be in the middle of a compile or a play test. " +
                "Freeing the claim lets other sessions change the editor right away.",
                "Free it",
                "Cancel");
            if (ok)
                Release();
        }

        static async void Release()
        {
            try
            {
                await MatrixPluginEditor.Instance.ReleaseEditorLockAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Matrix Bridge] Could not free the editor lock: {e.Message}");
            }
        }

        static async void SetPaused(bool paused)
        {
            try
            {
                await MatrixPluginEditor.Instance.SetEditorLockQueuePausedAsync(paused);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Matrix Bridge] Could not {(paused ? "pause" : "resume")} the editor lock queue: {e.Message}");
            }
        }

        static async void Remove(EditorLockWaiter waiter)
        {
            try
            {
                await MatrixPluginEditor.Instance.RemoveFromEditorLockQueueAsync(waiter.Ticket);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Matrix Bridge] Could not remove {waiter.Owner} from the editor lock queue: {e.Message}");
            }
        }

        static VisualElement? _queueView;
        static string _queueKey = string.Empty;
        static readonly List<(Label Age, EditorLockWaiter Waiter)> _ages = new();

        /// <summary>Lists the sessions waiting for the editor under the holder line; rebuilt only when the queue changes.</summary>
        public static void ShowQueue(VisualElement view, EditorLockState? state)
        {
            var visible = state != null && (state.Waiting.Length > 0 || state.QueuePaused);
            view.EnableInClassList("mb-queue--visible", visible);
            if (!visible || state == null)
            {
                _queueKey = string.Empty;
                return;
            }

            var key = $"{state.QueuePaused}|{state.QueueHeldByPlayMode}|" + string.Join("|", Array.ConvertAll(state.Waiting, w => w.Ticket + w.Owner + w.Task));
            if (!ReferenceEquals(view, _queueView) || key != _queueKey)
            {
                _queueView = view;
                _queueKey = key;
                Rebuild(view, state);
            }
            foreach (var (label, waiter) in _ages)
                label.text = Format(Age(waiter.WaitingSinceUnixMs));
        }

        static void Rebuild(VisualElement view, EditorLockState state)
        {
            view.Clear();
            _ages.Clear();
            var note = state.QueuePaused ? "  -  PAUSED" : state.QueueHeldByPlayMode ? "  -  WAITING FOR PLAY MODE TO END" : string.Empty;
            var header = new VisualElement();
            header.AddToClassList("mb-queue__row");
            header.Add(new Label($"QUEUE  {state.Waiting.Length} waiting{note}") { name = "mb-queue-title" });
            var pause = new Label(state.QueuePaused ? "RESUME" : "PAUSE") { tooltip = state.QueuePaused ? "Let the first in line have the editor" : "Nobody is granted the editor until you resume" };
            pause.AddToClassList("mb-queue__button");
            pause.RegisterCallback<ClickEvent>(_ => SetPaused(!state.QueuePaused));
            header.Add(pause);
            view.Add(header);

            for (var i = 0; i < state.Waiting.Length; i++)
            {
                var waiter = state.Waiting[i];
                var row = new VisualElement();
                row.AddToClassList("mb-queue__row");
                var who = waiter.Owner.Length > 0 ? waiter.Owner : "a session";
                var client = waiter.ClientName.Length > 0 && waiter.ClientName != waiter.Owner ? $" ({waiter.ClientName})" : string.Empty;
                var task = new Label($"{i + 1}.  {who}{client}  -  {waiter.Task}") { tooltip = $"{who}{client}" + Environment.NewLine + waiter.Task };
                task.AddToClassList("mb-queue__task");
                var age = new Label(Format(Age(waiter.WaitingSinceUnixMs)));
                age.AddToClassList("mb-queue__age");
                var remove = new Label("X") { tooltip = $"Take {who} out of the queue" };
                remove.AddToClassList("mb-queue__button");
                remove.RegisterCallback<ClickEvent>(_ => Remove(waiter));
                row.Add(task);
                row.Add(age);
                row.Add(remove);
                view.Add(row);
                _ages.Add((age, waiter));
            }
        }

        static void CopyDetails()
        {
            if (_state.Value is { } state)
                GUIUtility.systemCopyBuffer = Tooltip(state).Replace("Click for options.", string.Empty).Trim();
        }

        static string Name(EditorLockState state) => state.Owner.Length > 0 ? state.Owner : "a session";

        static string Task(EditorLockState state) => state.Task.Length > 0 ? state.Task + "  -  " : string.Empty;

        static TimeSpan Age(long unixMs)
        {
            var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
            return age < TimeSpan.Zero ? TimeSpan.Zero : age;
        }

        static string Format(TimeSpan span)
            => span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m"
                : span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes}m {span.Seconds}s"
                : $"{span.Seconds}s";

        static string Clock(long unixMs) => DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime().ToString("HH:mm");

        static string Short(string sessionId) => sessionId.Length > 8 ? sessionId.Substring(0, 8) : sessionId;
    }
}
