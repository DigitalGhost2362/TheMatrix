#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Feeder.MCP.Editor.Query;
using Feeder.MCP.Editor.Vfx;
using Feeder.Reflect.Utils;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Vfx
    {
        const string PathHelp = "Prefab asset path, e.g. Assets/VFX/HitSpark.prefab.";
        const string TargetHelp = "Alternative to path: object in the open scene (or open prefab stage), by hierarchy path, unique name or instance id.";

        public const string ControlToolId = "vfx-control";

        [AiTool(ControlToolId, Title = "VFX / Control playback", Enabled = false)]
        [AiSkillDescription("Play, pause, stop, clear or simulate the ParticleSystems of a scene object, in Edit mode or Play mode, and report how many particles are alive.")]
        [AiSkillBody("# VFX / Control playback\n\n`action`:`play`, `pause`, `stop` (stop and clear), `clear`, `simulate` (advance by `time` seconds, from the start when `restart=true`, else from the current state). Works on the object and all its child systems. In Edit mode a played system only advances while the Editor repaints; use `simulate` for a deterministic state, then read `alive`. Nothing is saved and playback state is not recorded in Undo.")]
        [Description("Control playback of a scene object's ParticleSystems: play | pause | stop | clear | simulate (time, restart). Returns alive particle counts." + SeeSkill)]
        public ResponseCallTool Control
        (
            [Description("Object in the open scene: hierarchy path, unique name or instance id.")]
            string target,
            [Description("play | pause | stop | clear | simulate")]
            string action = "play",
            [Description("Seconds to simulate (action=simulate).")]
            float time = 1f,
            [Description("simulate: true = restart from time 0 first, false = continue from the current state.")]
            bool restart = true
        )
        {
            return MainThread.Instance.Run(() =>
            {
                try
                {
                    var go = Resolve(target);
                    var systems = go.GetComponentsInChildren<ParticleSystem>(true);
                    if (systems.Length == 0) return ResponseCallTool.Error($"'{go.name}' has no ParticleSystem");
                    var root = systems.FirstOrDefault(s => s.gameObject == go) ?? systems[0];
                    switch (action)
                    {
                        case "play": root.Play(true); break;
                        case "pause": root.Pause(true); break;
                        case "stop": root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); break;
                        case "clear": root.Clear(true); break;
                        case "simulate":
                            if (time < 0f) return ResponseCallTool.Error("time must be >= 0");
                            root.Simulate(time, true, restart, true);
                            break;
                        default: return ResponseCallTool.Error($"unknown action '{action}'. Valid: play|pause|stop|clear|simulate");
                    }
                    SceneView.RepaintAll();
                    var alive = systems.Sum(s => s.particleCount);
                    return ResponseCallTool.Success($"{go.name} {action}: systems={systems.Length} alive={alive} playing={root.isPlaying} time={root.time:0.###}");
                }
                catch (Exception ex) { return ResponseCallTool.Error(ex.Message); }
            });
        }

        static string ReadScene(string target)
        {
            var go = Resolve(target);
            var warnings = new List<string>();
            var json = VfxSpec.Read(go, warnings).ToJsonString();
            return warnings.Count == 0 ? json : json + "\nwarn " + string.Join("; ", warnings);
        }

        static string ApplyScene(string target, string spec, bool replace)
        {
            var go = Resolve(target);
            var json = VfxService.ParseObject(spec, "spec");
            Undo.IncrementCurrentGroup();
            Undo.RegisterFullObjectHierarchyUndo(go, "Matrix VFX apply");
            var before = new HashSet<GameObject>(go.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject));
            var result = new VfxSpec.ApplyResult();
            VfxSpec.Apply(go, json, replace, result);
            foreach (var added in go.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject).Where(g => !before.Contains(g)))
                Undo.RegisterCreatedObjectUndo(added, "Matrix VFX apply");
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
            EditorUtility.SetDirty(go);
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(go);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(stage != null ? stage.scene : go.scene);
            var text = go.name + (result.Log.Count > 0 ? " | " + string.Join("; ", result.Log) : "");
            if (result.Errors.Count > 0) text += $"\nERRORS {result.Errors.Count}: {string.Join("; ", result.Errors)}";
            return text;
        }

        static GameObject Resolve(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("target is empty");
            try { return QuerySources.Find(target, 0); }
            catch (QueryException ex) { throw new ArgumentException(ex.Message); }
        }

        static void RequireOne(string? path, string? target)
        {
            if (string.IsNullOrWhiteSpace(path) == string.IsNullOrWhiteSpace(target))
                throw new ArgumentException("pass exactly one of path (prefab asset) or target (scene object)");
        }
    }
}
