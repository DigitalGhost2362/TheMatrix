#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Activity
{
    // Unity stops a callback chain at the first handler that throws, so cleanup of play-scoped state must not hang on
    // playModeStateChanged alone. This sits on update only while such state exists and asks the Editor itself.
    internal static class PlaySessionGuard
    {
        static readonly List<Action> _cleanups = new();
        static bool _ticking;

        internal static bool IsArmed => _ticking;

        // cleanup runs once play mode is over, also when the event already did the work: it must be safe to repeat
        public static void Arm(Action cleanup)
        {
            if (!_cleanups.Contains(cleanup))
                _cleanups.Add(cleanup);
            if (_ticking)
                return;
            _ticking = true;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            Flush();
        }

        internal static void Flush()
        {
            EditorApplication.update -= Tick;
            _ticking = false;
            var cleanups = _cleanups.ToArray();
            _cleanups.Clear();
            foreach (var cleanup in cleanups)
            {
                try { cleanup(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
