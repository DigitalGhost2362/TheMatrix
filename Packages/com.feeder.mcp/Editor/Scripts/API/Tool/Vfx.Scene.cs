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
        public const string SceneReadToolId = "vfx-scene-read";
        public const string SceneApplyToolId = "vfx-scene-apply";
        public const string ControlToolId = "vfx-control";

        const string TargetHelp = "Object in the open scene: hierarchy path, unique name or instance id.";

        [AiTool(SceneReadToolId, Title = "VFX / Read scene object", ReadOnlyHint = true, IdempotentHint = true, Enabled = false)]
        [AiSkillDescription("Read a ParticleSystem object that lives in the open scene (not a prefab asset) as the same compact JSON spec vfx-read returns.")]
        [AiSkillBody("# VFX / Read scene object\n\nSame output as `vfx-read`, for a GameObject in the open scene addressed by path, name or instance id. Use it to see what a human changed on an instance before patching it with `vfx-scene-apply`.")]
        [Description("Read a scene ParticleSystem object (path, name or id) as compact VFX spec JSON." + SeeSkill)]
        public ResponseCallTool SceneRead
        (
            [Description(TargetHelp)]
            string target
        )
        {
            return MainThread.Instance.Run(() =>
            {
                try
                {
                    var go = Resolve(target);
                    var warnings = new List<string>();
                    var json = VfxSpec.Read(go, warnings).ToJsonString();
                    return ResponseCallTool.Success(warnings.Count == 0 ? json : json + "\nwarn " + string.Join("; ", warnings));
                }
                catch (Exception ex) { return ResponseCallTool.Error(ex.Message); }
            });
        }

        [AiTool(SceneApplyToolId, Title = "VFX / Apply spec to scene object", Enabled = false)]
        [AiSkillDescription("Create or patch the ParticleSystem emitters of an object in the open scene from a compact JSON spec (same format as vfx-apply). Recorded in Undo, scene marked dirty, nothing saved.")]
        [AiSkillBody("# VFX / Apply spec to scene object\n\nSame spec as `vfx-apply`, applied to a live scene object (a ParticleSystem is added to it when missing). The change is one Undo step and marks the scene dirty; the scene is not saved. No history version is stored, so read the object first when you may need the old values. Removing emitters (`remove` or `replace=true`) is not covered by Undo.")]
        [Description("Apply a VFX spec to a scene object (path, name or id). Undoable, does not save." + SeeSkill)]
        public ResponseCallTool SceneApply
        (
            [Description(TargetHelp)]
            string target,
            [Description("VFX spec JSON, same format as vfx-apply.")]
            string spec,
            [Description("true = rebuild all emitters from the spec; false = patch by name.")]
            bool replace = false
        )
        {
            return MainThread.Instance.Run(() =>
            {
                try
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
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
                    var text = go.name + (result.Log.Count > 0 ? " | " + string.Join("; ", result.Log) : "");
                    if (result.Errors.Count > 0) text += $"\nERRORS {result.Errors.Count}: {string.Join("; ", result.Errors)}";
                    return ResponseCallTool.Success(text);
                }
                catch (Exception ex) { return ResponseCallTool.Error(ex.Message); }
            });
        }

        [AiTool(ControlToolId, Title = "VFX / Control playback", Enabled = false)]
        [AiSkillDescription("Play, pause, stop, clear or simulate the ParticleSystems of a scene object, in Edit mode or Play mode, and report how many particles are alive.")]
        [AiSkillBody("# VFX / Control playback\n\n`action`: `play`, `pause`, `stop` (stop and clear), `clear`, `simulate` (advance by `time` seconds, from the start when `restart=true`, else from the current state). Works on the object and all its child systems. In Edit mode a played system only advances while the Editor repaints; use `simulate` for a deterministic state, then read `alive`. Nothing is saved and playback state is not recorded in Undo.")]
        [Description("Control playback of a scene object's ParticleSystems: play | pause | stop | clear | simulate (time, restart). Returns alive particle counts." + SeeSkill)]
        public ResponseCallTool Control
        (
            [Description(TargetHelp)]
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

        static GameObject Resolve(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("target is empty");
            try { return QuerySources.Find(target, 0); }
            catch (QueryException ex) { throw new ArgumentException(ex.Message); }
        }
    }
}
