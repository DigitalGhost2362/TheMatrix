#nullable enable
using System;
using Feeder.ToolHost;
using Feeder.MCP.Editor.UI;
using Feeder.Rx;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Activity
{
    [InitializeOnLoad]
    public static class AgentActivity
    {
        public const double WorkingLingerSeconds = 8;
        const double PlayHandoffSeconds = 2;
        const string LastWriteAtKey = "Feeder_Matrix_LastWriteAt";
        const string LastWriteToolKey = "Feeder_Matrix_LastWriteTool";
        const string AgentPlayKey = "Feeder_Matrix_AgentPlay";

        static readonly Subject<Unit> _changed = new();
        static IDisposable? _toolCalls;
        static int _writesInFlight;
        // SessionState only carries it across domain reloads; it is read once per load, never per tick
        static bool _agentPlay;
        static bool _warnedLeftover;

        public static Observable<Unit> Changed => _changed;

        public static string LastWriteTool => SessionState.GetString(LastWriteToolKey, string.Empty);

        public static bool IsWorking => _writesInFlight > 0 || SecondsSinceLastWrite < WorkingLingerSeconds;

        public static double WorkingSecondsLeft => _writesInFlight > 0
            ? WorkingLingerSeconds
            : Math.Max(0, WorkingLingerSeconds - SecondsSinceLastWrite);

        public static bool IsAgentPlay => _agentPlay && EditorApplication.isPlaying;

        static double SecondsSinceLastWrite => EditorApplication.timeSinceStartup - SessionState.GetFloat(LastWriteAtKey, float.MinValue);

        static AgentActivity()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;

            MatrixPluginEditor.PluginProperty.Subscribe(OnPlugin);
            _agentPlay = SessionState.GetBool(AgentPlayKey, false);
            if (_agentPlay)
                PlaySessionGuard.Arm(ClearLeftover);
            EditorEvents.OnPlayModeChanged(OnPlayModeStateChanged);
        }

        public static void MarkAgentPlay()
        {
            SetAgentPlay();
            _changed.OnNext(Unit.Default);
        }

        static void SetAgentPlay()
        {
            _agentPlay = true;
            SessionState.SetBool(AgentPlayKey, true);
            PlaySessionGuard.Arm(ClearLeftover);
        }

        static bool ClearAgentPlay()
        {
            if (!_agentPlay)
                return false;
            _agentPlay = false;
            SessionState.EraseBool(AgentPlayKey);
            return true;
        }

        // the guard finds the mark still set only when EnteredEditMode never got here
        static void ClearLeftover()
        {
            if (!ClearAgentPlay())
                return;
            if (!_warnedLeftover)
            {
                _warnedLeftover = true;
                Debug.LogWarning("[Matrix Bridge] Cleared an agent play mark that outlived play mode: Unity's play mode event " +
                    "did not reach Matrix (a handler of another package threw before it, see the exception in the Console), " +
                    "or Unity refused to enter play mode.");
            }
            _changed.OnNext(Unit.Default);
        }

        static void OnPlugin(IToolHost? plugin)
        {
            _toolCalls?.Dispose();
            _toolCalls = null;
            _writesInFlight = 0;
            var tools = plugin?.McpManager.ToolManager;
            if (tools == null)
                return;
            _toolCalls = tools.OnToolCall.ObserveOnCurrentSynchronizationContext().Subscribe(OnToolCall);
        }

        static void OnToolCall(ToolCallActivity call)
        {
            if (SkillCatalog.RiskOf(call.ReadOnlyHint, call.DestructiveHint) == SkillRisk.ReadOnly)
                return;
            _writesInFlight = call.Finished ? Math.Max(0, _writesInFlight - 1) : _writesInFlight + 1;
            SessionState.SetFloat(LastWriteAtKey, (float)EditorApplication.timeSinceStartup);
            SessionState.SetString(LastWriteToolKey, call.Name);
            _changed.OnNext(Unit.Default);
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // a write tool right before play (e.g. script-execute setting isPlaying) counts as the agent starting it
            if (state == PlayModeStateChange.ExitingEditMode && (_writesInFlight > 0 || SecondsSinceLastWrite < PlayHandoffSeconds))
                SetAgentPlay();
            else if (state == PlayModeStateChange.EnteredEditMode)
                ClearAgentPlay();
            _changed.OnNext(Unit.Default);
        }
    }
}
