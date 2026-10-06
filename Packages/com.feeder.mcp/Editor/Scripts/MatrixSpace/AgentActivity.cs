#nullable enable
using System;
using Feeder.McpPlugin;
using Feeder.MCP.Editor.UI;
using R3;
using UnityEditor;

namespace Feeder.MCP.Editor.MatrixSpace
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

        public static Observable<Unit> Changed => _changed;

        public static string LastWriteTool => SessionState.GetString(LastWriteToolKey, string.Empty);

        public static bool IsWorking => _writesInFlight > 0 || SecondsSinceLastWrite < WorkingLingerSeconds;

        public static double WorkingSecondsLeft => _writesInFlight > 0
            ? WorkingLingerSeconds
            : Math.Max(0, WorkingLingerSeconds - SecondsSinceLastWrite);

        public static bool IsAgentPlay => EditorApplication.isPlaying && SessionState.GetBool(AgentPlayKey, false);

        static double SecondsSinceLastWrite => EditorApplication.timeSinceStartup - SessionState.GetFloat(LastWriteAtKey, float.MinValue);

        static AgentActivity()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;

            UnityMcpPluginEditor.PluginProperty.Subscribe(OnPlugin);
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void MarkAgentPlay()
        {
            SessionState.SetBool(AgentPlayKey, true);
            _changed.OnNext(Unit.Default);
        }

        static void OnPlugin(IMcpPlugin? plugin)
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
                SessionState.SetBool(AgentPlayKey, true);
            else if (state == PlayModeStateChange.EnteredEditMode)
                SessionState.EraseBool(AgentPlayKey);
            _changed.OnNext(Unit.Default);
        }
    }
}
