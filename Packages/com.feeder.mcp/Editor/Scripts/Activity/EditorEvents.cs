#nullable enable
using System;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Activity
{
    // the one place that subscribes to Unity's shared events: a handler that throws there would cut the chain for
    // every package after it
    internal static class EditorEvents
    {
        const string DropKey = "Feeder_Matrix_DropPlayEvents";
        static bool _drop = SessionState.GetBool(DropKey, false);

        // tests only: these events never reach Matrix, as when a handler of another package throws first
        internal static bool DropForTests
        {
            get => _drop;
            set
            {
                _drop = value;
                if (value)
                    SessionState.SetBool(DropKey, true);
                else
                    SessionState.EraseBool(DropKey);
            }
        }

        public static Action<PlayModeStateChange> OnPlayModeChanged(Action<PlayModeStateChange> handler)
        {
            void Guarded(PlayModeStateChange state)
            {
                if (_drop)
                    return;
                try { handler(state); }
                catch (Exception e) { Debug.LogException(e); }
            }
            EditorApplication.playModeStateChanged += Guarded;
            return Guarded;
        }

        public static void OnAfterAssemblyReload(Action handler)
        {
            AssemblyReloadEvents.afterAssemblyReload += () =>
            {
                if (_drop)
                    return;
                try { handler(); }
                catch (Exception e) { Debug.LogException(e); }
            };
        }

        // update, not delayCall: one throwing delayCall handler drops every later one, and delayCall sleeps unfocused
        public static void NextTick(Action once)
        {
            void Run()
            {
                EditorApplication.update -= Run;
                try { once(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            EditorApplication.update += Run;
        }
    }
}
