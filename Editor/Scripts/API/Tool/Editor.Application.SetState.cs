#nullable enable
using Feeder.MCP.Data;
using System;
using System.ComponentModel;
using System.Linq;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.Activity;
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
            Enabled = true
        )]
        [AiSkillDescription("Enter, leave or pause play mode. Answers once the editor has reached the new state, domain reload included. " +
            "Refuses to enter play mode while the project has compile errors.")]
        [AiSkillBody("## Inputs\n\n" +
            "- `isPlaying` (default `false`) - true enters play mode, false leaves it.\n" +
            "- `isPaused` (optional) - true pauses, false resumes; leave it out to keep the current pause state.\n\n" +
            "## Behavior\n\n" +
            "Entering play mode is refused while `EditorUtility.scriptCompilationFailed` is true; the error lists the compile errors. " +
            "Leaving play mode and pausing always work. A play mode change answers only once the editor has settled, " +
            "so the next call can rely on it. Returns the `EditorStatsData` snapshot taken in the new state. " +
            "`editor-health` reports the current phase (playing, paused, compiling) at any time.")]
        [Description("Enter (isPlaying=true), leave (false) or pause play mode. Answers once the editor has settled. " +
            "Entering is refused while there are compile errors.")]
        public ResponseCallTool SetApplicationState
        (
            [Description("True enters play mode, false leaves it.")]
            bool isPlaying = false,
            [Description("True pauses, false resumes. Leave it out to keep the current pause state.")]
            bool? isPaused = null,
            [RequestID]
            string? requestId = null
        )
        {
            return MainThread.Instance.Run(() =>
            {
                if (isPlaying && !EditorApplication.isPlaying && UnityEditor.EditorUtility.scriptCompilationFailed)
                {
                    var compilationErrorDetails = ScriptUtils.GetCompilationErrorDetails();
                    throw new Exception($"Unity project has compilation errors. Fix them before entering play mode.\n{compilationErrorDetails}");
                }
                if (isPlaying && !EditorApplication.isPlaying)
                    AgentActivity.MarkAgentPlay();

                var changesPlayMode = EditorApplication.isPlayingOrWillChangePlaymode != isPlaying || EditorApplication.isPlaying != isPlaying;
                EditorApplication.isPlaying = isPlaying;
                if (isPaused.HasValue)
                    EditorApplication.isPaused = isPaused.Value;

                if (!changesPlayMode || string.IsNullOrWhiteSpace(requestId))
                    return PlayModeWait.Result(requestId);

                PlayModeWait.Add(requestId!, isPlaying);
                return ResponseCallTool.Processing(isPlaying ? "Entering play mode..." : "Leaving play mode...").SetRequestID(requestId);
            });
        }
    }

    /// <summary>
    /// Answers a play mode change once the editor has settled in the requested state. Pending calls live
    /// in <see cref="SessionState"/> so they survive the domain reload that entering play mode runs.
    /// </summary>
    [InitializeOnLoad]
    static class PlayModeWait
    {
        const string Key = "Feeder_MCP_PlayModeWait";
        static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(5);

        static PlayModeWait()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;
            _pending = Read().Count > 0;
            EditorApplication.update += Poll;
        }

        public static void Add(string requestId, bool isPlaying)
        {
            var entries = Read().Where(e => e.RequestId != requestId).ToList();
            entries.Add(new Entry(requestId, isPlaying, DateTime.UtcNow.Ticks));
            Write(entries);
            _pending = true;
        }

        public static ResponseCallTool Result(string? requestId)
        {
            var reflector = MatrixPluginEditor.Instance.Reflector ?? throw new Exception("Reflector is not available.");
            var result = new Feeder.Json.Nodes.JsonObject
            {
                ["result"] = Feeder.Json.JsonSerializer.SerializeToNode(EditorStatsData.FromEditor(), reflector.JsonSerializerOptions),
                ["compilationFailed"] = UnityEditor.EditorUtility.scriptCompilationFailed,
            };
            return ResponseCallTool.SuccessStructured(result).SetRequestID(requestId);
        }

        // SessionState is read only while a call is pending, not on every editor tick
        static bool _pending;

        static void Poll()
        {
            if (!_pending)
                return;
            var entries = Read();
            if (entries.Count == 0)
            {
                _pending = false;
                return;
            }
            var settled = !EditorApplication.isCompiling && !EditorApplication.isUpdating;
            var now = DateTime.UtcNow.Ticks;
            var remaining = entries.Where(entry =>
            {
                var reached = settled && EditorApplication.isPlaying == entry.IsPlaying &&
                              EditorApplication.isPlayingOrWillChangePlaymode == entry.IsPlaying;
                // the request is made with isPlayingOrWillChangePlaymode already true, so seeing it false
                // while not playing means Unity refused to enter (compile errors, a modal dialog)
                var refused = settled && entry.IsPlaying && !EditorApplication.isPlaying &&
                              !EditorApplication.isPlayingOrWillChangePlaymode;
                var expired = new TimeSpan(now - entry.StartedTicks) > GiveUpAfter;
                if (!reached && !refused && !expired)
                    return true;
                var response = reached
                    ? Result(entry.RequestId)
                    : refused
                        ? ResponseCallTool.Error(UnityEditor.EditorUtility.scriptCompilationFailed
                            ? $"Unity refused to enter play mode: the project has compilation errors.\n{ScriptUtils.GetCompilationErrorDetails()}"
                            : "Unity refused to enter play mode. Check the Console for the reason.").SetRequestID(entry.RequestId)
                        : ResponseCallTool.Error($"Play mode did not become {(entry.IsPlaying ? "playing" : "stopped")} within {GiveUpAfter.TotalMinutes:0} minutes.").SetRequestID(entry.RequestId);
                _ = MatrixPluginEditor.NotifyToolRequestCompleted(new RequestToolCompletedData { RequestId = entry.RequestId, Result = response });
                return false;
            }).ToList();
            Write(remaining);
        }

        sealed class Entry
        {
            public readonly string RequestId;
            public readonly bool IsPlaying;
            public readonly long StartedTicks;

            public Entry(string requestId, bool isPlaying, long startedTicks)
            {
                RequestId = requestId;
                IsPlaying = isPlaying;
                StartedTicks = startedTicks;
            }
        }

        static System.Collections.Generic.List<Entry> Read()
        {
            var raw = SessionState.GetString(Key, string.Empty);
            if (raw.Length == 0)
                return new System.Collections.Generic.List<Entry>();
            return raw.Split('\n')
                .Select(line => line.Split('|'))
                .Where(parts => parts.Length == 3 && long.TryParse(parts[2], out _))
                .Select(parts => new Entry(parts[0], parts[1] == "1", long.Parse(parts[2])))
                .ToList();
        }

        static void Write(System.Collections.Generic.List<Entry> entries)
        {
            if (entries.Count == 0)
                SessionState.EraseString(Key);
            else
                SessionState.SetString(Key, string.Join("\n", entries.Select(e => $"{e.RequestId}|{(e.IsPlaying ? 1 : 0)}|{e.StartedTicks}")));
        }
    }
}
