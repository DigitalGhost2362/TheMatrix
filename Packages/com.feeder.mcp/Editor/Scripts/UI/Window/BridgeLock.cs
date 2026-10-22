#nullable enable
using System;
using System.Text;
using Feeder.MCP.Editor.Matrix;
using Feeder.Rx;
using UnityEditor;
using UnityEngine;

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

        /// <summary>One short line: who, for what, for how long.</summary>
        public static string Line(EditorLockState state)
        {
            if (!state.Held)
                return "editor free";
            var age = Format(Age(state.AcquiredUnixMs));
            return state.Kind switch
            {
                KindOutside => $"{Name(state)} (lock file)  -  {Task(state)}{age}",
                _ => $"{Name(state)}  -  {Task(state)}{age}",
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
            text.Append("Click for options.");
            return text.ToString();
        }

        public static void ShowMenu(Rect at)
        {
            var state = _state.Value;
            var menu = new GenericMenu();
            if (state is not { Held: true })
                return;
            menu.AddItem(new GUIContent($"Free the claim of {Name(state)}..."), false, ConfirmRelease);
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
