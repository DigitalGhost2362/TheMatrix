#nullable enable
using Feeder.MCP.Data;
using System;
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.MatrixSpace;
using Feeder.MCP.Editor.Utils;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Editor
    {
        public const string EditorApplicationSetStateToolId = "editor-application-set-state";
        [AiTool
        (
            EditorApplicationSetStateToolId,
            Title = "Editor / Application / Set State",
            IdempotentHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Start / stop / pause the Unity Editor 'playmode'. " +
            "Use '" + EditorApplicationGetStateToolId + "' to inspect the current state first. " +
            "Throws if the project currently has compilation errors.")]
        [AiSkillBody("Control the Unity Editor application state. " +
            "You can start, stop, or pause the 'playmode'. " +
            "Use '" + EditorApplicationGetStateToolId + "' tool to get the current state first.\n\n" +
            "## Inputs\n\n" +
            "- `isPlaying` (default `false`) — sets `EditorApplication.isPlaying`.\n" +
            "- `isPaused` (default `false`) — sets `EditorApplication.isPaused`.\n\n" +
            "## Behavior\n\n" +
            "Refuses any state change while `EditorUtility.scriptCompilationFailed` is true — instead throws with " +
            "the compilation error details so the caller can fix them first. On success returns the post-change " +
            "`EditorStatsData` snapshot.")]
        [Description("Control the Unity Editor application state. " +
            "You can start, stop, or pause the 'playmode'. " +
            "Use '" + EditorApplicationGetStateToolId + "' tool to get the current state first.")]
        public EditorStatsData? SetApplicationState
        (
            [Description("If true, the 'playmode' will be started. If false, the 'playmode' will be stopped.")]
            bool isPlaying = false,
            [Description("If true, the 'playmode' will be paused. If false, the 'playmode' will be resumed.")]
            bool isPaused = false
        )
        {
            return MainThread.Instance.Run(() =>
            {
                if (UnityEditor.EditorUtility.scriptCompilationFailed)
                {
                    var compilationErrorDetails = ScriptUtils.GetCompilationErrorDetails();
                    throw new Exception($"Unity project has compilation error. Please fix all compilation errors before doing this operation.\n{compilationErrorDetails}");
                }
                if (isPlaying && !EditorApplication.isPlaying)
                    AgentActivity.MarkAgentPlay();
                EditorApplication.isPlaying = isPlaying;
                EditorApplication.isPaused = isPaused;

                return EditorStatsData.FromEditor();
            });
        }
    }
}
