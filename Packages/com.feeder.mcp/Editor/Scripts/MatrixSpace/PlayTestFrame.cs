#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Feeder.MCP.Utils;
using Feeder.Logging;
using UnityEditor;
using UnityEngine;
using ILogger = Feeder.Logging.ILogger;

namespace Feeder.MCP.Editor.MatrixSpace
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

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            if (AgentActivity.IsAgentPlay)
                EditorApplication.delayCall += ShowIfAgentPlay;
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

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
                EditorApplication.delayCall += ShowIfAgentPlay;
            else if (state == PlayModeStateChange.ExitingPlayMode)
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
