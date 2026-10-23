#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Feeder.MCP.Editor.MatrixSpace;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Feeder.MCP.Editor.Watch
{
    // cyan frame with a STOP button over every Game View while a watch is armed
    internal static class WatchFrame
    {
        const string FrameName = "matrix-watch-frame";
        const string Variant = "watch";
        const string StopName = "watch-stop";
        const string TagArmed = "MATRIX  -  WATCH ARMED  -  PRESS PLAY";
        const string TagRecording = "MATRIX  -  WATCHING THE ARCHITECT  -  F9 MARK";

        static readonly Type? _playModeViewType = Type.GetType("UnityEditor.PlayModeView,UnityEditor");

        public static void Show(bool recording)
        {
            foreach (var view in Views())
            {
                ViewFrame.Show(view, FrameName, Variant, recording ? TagRecording : TagArmed);
                var frame = view.rootVisualElement.Q(FrameName)?.Q("ptf-frame");
                if (frame != null && frame.Q<Button>(StopName) == null)
                {
                    var stop = new Button(OnStop) { name = StopName, text = "STOP" };
                    stop.AddToClassList("ptf-stop");
                    frame.Add(stop);
                }
                view.Repaint();
            }
        }

        static double _nextCheck;

        // while armed, every half second: views rebuild their UI late after a domain reload (later still when the
        // Editor is in the background), and Game views can be opened or maximized on play
        public static void Keep()
        {
            EditorApplication.update -= Check;
            EditorApplication.update += Check;
            _nextCheck = 0;
        }

        static void Check()
        {
            if (!WatchSession.IsArmed)
            {
                EditorApplication.update -= Check;
                return;
            }
            var now = EditorApplication.timeSinceStartup;
            if (now < _nextCheck)
                return;
            _nextCheck = now + 0.5;
            var recording = WatchSession.Recorder != null;
            foreach (var view in Views())
            {
                var tag = view.rootVisualElement.Q(FrameName)?.Q<Label>("ptf-tag");
                if (tag == null || tag.text != (recording ? TagRecording : TagArmed))
                {
                    Show(recording);
                    return;
                }
            }
        }

        public static void Hide()
        {
            foreach (var view in Views())
            {
                ViewFrame.Hide(view, FrameName);
                view.Repaint();
            }
        }

        static void OnStop()
        {
            if (WatchSession.Stop(WatchSession.ReasonStopButton) == null)
                Hide();
        }

        static IEnumerable<EditorWindow> Views() => _playModeViewType == null
            ? Enumerable.Empty<EditorWindow>()
            : Resources.FindObjectsOfTypeAll(_playModeViewType).OfType<EditorWindow>();
    }
}
