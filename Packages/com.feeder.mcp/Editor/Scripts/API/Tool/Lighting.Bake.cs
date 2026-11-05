#nullable enable
using System;
using System.ComponentModel;
using System.Text;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityEditor;
using UnityEngine.SceneManagement;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Lighting
    {
        public const string LightingBakeToolId = "lighting-bake";
        const string StartKey = "Feeder_MCP_Lighting_BakeStart";
        const string DoneKey = "Feeder_MCP_Lighting_BakeDone";

        [InitializeOnLoadMethod]
        static void HookBakeCompleted()
        {
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            Lightmapping.bakeCompleted += OnBakeCompleted;
        }

        static void OnBakeCompleted() => SessionState.SetString(DoneKey, DateTime.UtcNow.Ticks.ToString());

        [AiTool
        (
            LightingBakeToolId,
            Title = "Lighting / Bake",
            Enabled = false
        )]
        [AiSkillDescription("Start, poll, cancel or clear the lightmap bake of the active scene. 'start' returns at once; poll with action='status' " +
            "(state running with progress and retry_after_ms, or idle with the lightmap count). The bake survives a domain reload.")]
        [Description("Lightmap bake as a job. action: 'start' (Lightmapping.BakeAsync of the active scene, which must be saved and not in play mode), " +
            "'status' (state running|idle, progress, seconds since start, last finish, lightmap count), 'cancel', 'clear' (delete the baked data of the scene). " +
            "Editor-changing: a bake rewrites lighting data on disk and follows the editor lock.")]
        public string Bake
        (
            [Description("'start', 'status', 'cancel' or 'clear'. Default: 'status'")]
            string action = "status"
        )
        {
            var kind = (action ?? string.Empty).Trim().ToLowerInvariant();
            if (kind != "start" && kind != "status" && kind != "cancel" && kind != "clear")
                throw new ArgumentException("action must be 'start', 'status', 'cancel' or 'clear'.");

            return MainThread.Instance.Run(() =>
            {
                switch (kind)
                {
                    case "start":
                        if (EditorApplication.isPlayingOrWillChangePlaymode)
                            throw new InvalidOperationException("Cannot bake in play mode.");
                        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                            throw new InvalidOperationException("The Editor is compiling or importing: try again when it is done.");
                        if (Lightmapping.isRunning)
                            return "[already_running] A bake is already running.\n" + Status();
                        if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                            throw new InvalidOperationException("The active scene is not saved; save it before baking.");
                        SessionState.SetString(StartKey, DateTime.UtcNow.Ticks.ToString());
                        SessionState.EraseString(DoneKey);
                        if (!Lightmapping.BakeAsync())
                            throw new InvalidOperationException("Lightmapping.BakeAsync refused to start (check the Lighting settings and the console).");
                        return "started\n" + Status();
                    case "cancel":
                        if (!Lightmapping.isRunning)
                            return "idle: no bake is running.";
                        Lightmapping.Cancel();
                        return "cancel requested\n" + Status();
                    case "clear":
                        if (Lightmapping.isRunning)
                            throw new InvalidOperationException("A bake is running: cancel it first.");
                        Lightmapping.Clear();
                        return "cleared\n" + Status();
                    default:
                        return Status();
                }
            });
        }

        static string Status()
        {
            var sb = new StringBuilder();
            var running = Lightmapping.isRunning;
            sb.Append($"state={(running ? "running" : "idle")} scene={SceneManager.GetActiveScene().name}");
            if (running)
                sb.Append($" progress={RenderingParse.F(Lightmapping.buildProgress)} retry_after_ms=3000");
            if (long.TryParse(SessionState.GetString(StartKey, string.Empty), out var started))
                sb.Append($" elapsed_s={(int)TimeSpan.FromTicks(Math.Max(0, DateTime.UtcNow.Ticks - started)).TotalSeconds}");
            if (!running && SessionState.GetString(DoneKey, string.Empty).Length > 0)
                sb.Append(" last_bake=completed");
            sb.Append($" lightmaps={UnityEngine.LightmapSettings.lightmaps.Length}");
            return sb.ToString();
        }
    }
}
