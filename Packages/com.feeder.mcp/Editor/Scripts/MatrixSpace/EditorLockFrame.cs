#nullable enable
using Feeder.MCP.Editor.Matrix;
using Feeder.MCP.Editor.UI;
using Feeder.Rx;
using UnityEditor;

namespace Feeder.MCP.Editor.MatrixSpace
{
    /// <summary>Green frame around every Scene view while an agent holds the editor lock, tagged with the holder.</summary>
    [InitializeOnLoad]
    internal static class EditorLockFrame
    {
        const string FrameName = "matrix-editor-lock-frame";
        const string Variant = "lock";

        static EditorLockFrame()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;

            // the first value is replayed at once; it also clears a frame left from before a domain reload
            BridgeLock.State
                .ObserveOnCurrentSynchronizationContext()
                .Subscribe(OnState);
        }

        // the state is pushed on changes only (at most every few seconds while held), so each push also
        // frames a Scene view opened since the last one
        static void OnState(EditorLockState? state)
        {
            if (state is not { Held: true })
            {
                foreach (SceneView view in SceneView.sceneViews)
                    ViewFrame.Hide(view, FrameName);
                return;
            }

            var tag = Tag(state);
            foreach (SceneView view in SceneView.sceneViews)
                ViewFrame.Show(view, FrameName, Variant, tag);
        }

        static string Tag(EditorLockState state)
        {
            var owner = state.Owner.Length > 0 ? state.Owner : "a session";
            var who = state.Kind == BridgeLock.KindOutside ? $"LOCKED BY {owner} (LOCK FILE)" : $"LOCKED BY {owner}";
            return ("MATRIX  -  " + who).ToUpperInvariant();
        }
    }
}
