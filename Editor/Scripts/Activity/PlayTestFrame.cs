#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Feeder.MCP.Utils;
using Feeder.Logging;
using Feeder.Rx;
using UnityEditor;
using UnityEngine;
using ILogger = Feeder.Logging.ILogger;

namespace Feeder.MCP.Editor.Activity
{
    [InitializeOnLoad]
    internal static class PlayTestFrame
    {
        const string FrameName = "matrix-play-test-frame";
        const string Variant = "play";
        const string Tag = "MATRIX  -  AGENT PLAY TEST";

        static readonly Type? _playModeViewType = Type.GetType("UnityEditor.PlayModeView,UnityEditor");
        static readonly ILogger _logger = UnityLoggerFactory.LoggerFactory.CreateLogger(nameof(PlayTestFrame));

        static PlayTestFrame()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;

            // the state, not a play mode event: the frame goes with the mark however play mode ended
            AgentActivity.Changed.Subscribe(_ => Sync());
            Sync();
        }

        internal static void Show()
        {
            foreach (var view in PlayModeViews())
                ViewFrame.Show(view, FrameName, Variant, Tag);
        }

        internal static void Hide()
        {
            foreach (var view in PlayModeViews())
                ViewFrame.Hide(view, FrameName);
        }

        static bool _shown;

        static void Sync()
        {
            if (AgentActivity.IsAgentPlay == _shown)
                return;
            _shown = !_shown;
            // the Game view builds its UI after play mode starts and after a domain reload
            if (_shown)
                EditorEvents.NextTick(ShowIfAgentPlay);
            else
                Hide();
        }

        static void ShowIfAgentPlay()
        {
            if (AgentActivity.IsAgentPlay)
                Show();
        }

        static IEnumerable<EditorWindow> PlayModeViews()
        {
            if (_playModeViewType == null)
            {
                _logger.LogError("{method} UnityEditor.PlayModeView type not found.", nameof(PlayModeViews));
                return Enumerable.Empty<EditorWindow>();
            }
            return Resources.FindObjectsOfTypeAll(_playModeViewType).OfType<EditorWindow>();
        }
    }
}
