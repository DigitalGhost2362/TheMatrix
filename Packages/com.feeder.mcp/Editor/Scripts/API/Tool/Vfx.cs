#nullable enable
using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Feeder.McpPlugin;
using Feeder.McpPlugin.Common.Model;
using Feeder.ReflectorNet.Utils;
using Feeder.MCP.Editor.Vfx;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_Vfx
    {
        public const string ApplyToolId = "vfx-apply";
        public const string ReadToolId = "vfx-read";
        public const string PreviewToolId = "vfx-preview";
        public const string TextureToolId = "vfx-texture";
        public const string HistoryToolId = "vfx-history";

        const string SeeSkill = " Spec format, workflow and recipes: skill '" + Skill_Vfx.SkillId + "'.";
        const string Png = Feeder.McpPlugin.Common.Consts.MimeType.ImagePng;

        [AiTool(ApplyToolId, Title = "VFX / Apply spec", Enabled = false)]
        [AiSkillDescription("Create or patch a ParticleSystem VFX prefab from a compact JSON spec (emitters addressed by name, only changed fields). Saves a history version and returns text stats, optionally a time-lapse contact sheet.")]
        [AiSkillBody("# VFX / Apply spec\n\nCreates the prefab when missing, otherwise patches it: emitters are matched by `name`, missing ones are created, `{\"name\":\"X\",\"remove\":true}` deletes. `replace=true` rebuilds every emitter from the spec. Every call stores a history version (see `vfx-history`).\n\n`preview`: `stats` (default, text only, cheapest), `sheet` (adds the contact sheet image), `none`.\n\nFull spec reference and the iteration loop live in the `" + Skill_Vfx.SkillId + "` skill.")]
        [Description("Create or patch a VFX prefab from a compact JSON spec. Returns version + text stats (preview=stats), or also a contact sheet (preview=sheet)." + SeeSkill)]
        public ResponseCallTool Apply
        (
            [Description("Prefab path, e.g. Assets/VFX/HitSpark.prefab. Created if missing.")]
            string path,
            [Description("JSON: {\"defaults\":{...},\"root\":{...},\"emitters\":[{\"name\":\"Core\",\"material\":\"vfx_glow/add\",\"main\":{...},\"emission\":{...},...}]}. Only changed fields.")]
            string spec,
            [Description("true = rebuild all emitters from spec; false = patch by name.")]
            bool replace = false,
            [Description("Short note stored with this history version.")]
            string? note = null,
            [Description("stats | sheet | none")]
            string preview = "stats"
        )
        {
            return MainThread.Instance.Run(() =>
            {
                try
                {
                    var outcome = VfxService.Apply(path, spec, replace, note);
                    var text = outcome.Summary(path);
                    if (preview == "none") return ResponseCallTool.Success(text);
                    var result = VfxService.Preview(path, new VfxPreview.Options { Image = preview == "sheet" });
                    text += "\n" + result.Stats;
                    return result.Png != null ? ResponseCallTool.Image(result.Png, Png, text) : ResponseCallTool.Success(text);
                }
                catch (Exception ex)
                {
                    return ResponseCallTool.Error(ex.Message);
                }
            });
        }

        [AiTool(ReadToolId, Title = "VFX / Read spec", ReadOnlyHint = true, IdempotentHint = true, Enabled = false)]
        [AiSkillDescription("Read a VFX prefab as compact JSON: only values that differ from the baseline emitter. Use it to see manual Inspector edits before patching.")]
        [AiSkillBody("# VFX / Read spec\n\nReturns the same format `vfx-apply` accepts, minified, with defaults omitted. Rotations are in degrees. Read before patching an effect a human may have touched.")]
        [Description("Read a VFX prefab as compact JSON (non-default values only, same format vfx-apply accepts)." + SeeSkill)]
        public ResponseCallTool Read
        (
            [Description("Prefab path.")]
            string path
        )
        {
            return MainThread.Instance.Run(() =>
            {
                try { return ResponseCallTool.Success(VfxService.Read(path)); }
                catch (Exception ex) { return ResponseCallTool.Error(ex.Message); }
            });
        }

        [AiTool(PreviewToolId, Title = "VFX / Preview", ReadOnlyHint = true, IdempotentHint = true, Enabled = false)]
        [AiSkillDescription("Deterministic preview of a VFX prefab: text stats (alive counts over time, per-emitter windows, fill/overdraw, warnings) and one labelled contact-sheet PNG of N time samples from a fixed camera.")]
        [AiSkillBody("# VFX / Preview\n\nSimulates with a fixed seed in an isolated layer, fits one camera to the union of all frames, renders `frames` cells into one grid. Pass the returned `frame=` value back to keep framing identical between iterations. `statsOnly=true` skips the image (a few hundred tokens instead of ~1k).")]
        [Description("Render a labelled time-lapse contact sheet + text stats of a VFX prefab. statsOnly=true for text only." + SeeSkill)]
        public ResponseCallTool Preview
        (
            [Description("Prefab path.")]
            string path,
            [Description("Number of time samples (1-16).")]
            int frames = 8,
            [Description("Grid columns.")]
            int cols = 4,
            [Description("Cell size in px (64-512).")]
            int cell = 192,
            [Description("front | back | side | top | iso")]
            string view = "front",
            [Description("Background hex, e.g. #1E1E1E (dark) or #9A9A9A (light, to check alpha blends).")]
            string bg = "#1E1E1E",
            [Description("Fixed framing from a previous result, 'radius' or 'radius@x,y,z'. Empty = auto.")]
            string? frame = null,
            [Description("Explicit sample times in seconds, comma separated. Overrides frames.")]
            string? times = null,
            [Description("Seconds covered. 0 = auto from durations and lifetimes.")]
            float duration = 0f,
            [Description("Random seed.")]
            int seed = 1,
            [Description("Particle budget for the warning.")]
            int budget = 300,
            [Description("true = text stats only, no image.")]
            bool statsOnly = false,
            [Description("Optional model prefab path rendered with the effect parented at its root, e.g. the weapon an aura sits on.")]
            string? context = null,
            [Description("Optional camera name in the open scene. Renders the instance already placed in the scene through that camera, with its post-processing (bloom). Ignores view/bg/frame/context.")]
            string? camera = null
        )
        {
            return MainThread.Instance.Run(() =>
            {
                try
                {
                    var o = new VfxPreview.Options
                    {
                        Frames = frames,
                        Cols = cols,
                        Cell = Mathf.Clamp(cell, 64, 512),
                        View = view,
                        Background = VfxCodec.DecodeColor(bg, "bg"),
                        Duration = duration,
                        Seed = seed,
                        Budget = budget,
                        Image = !statsOnly
                    };
                    if (!string.IsNullOrWhiteSpace(frame)) VfxPreview.ParseFrame(frame!, o);
                    if (!string.IsNullOrWhiteSpace(context))
                        o.Context = AssetDatabase.LoadAssetAtPath<GameObject>(context) ?? throw new ArgumentException($"context prefab not found '{context}'");
                    if (!string.IsNullOrWhiteSpace(times))
                        o.Times = times!.Split(',').Select(VfxCodec.ParseFloat).Take(16).ToArray();
                    if (!string.IsNullOrWhiteSpace(camera))
                    {
                        var cam = UnityEngine.Object.FindObjectsOfType<Camera>(true).FirstOrDefault(c => c.name == camera)
                                  ?? throw new ArgumentException($"camera '{camera}' not found in the open scene");
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new ArgumentException($"prefab not found '{path}'");
                        var sceneResult = VfxPreview.RunInScene(prefab, cam, o);
                        return sceneResult.Png != null ? ResponseCallTool.Image(sceneResult.Png, Png, sceneResult.Stats) : ResponseCallTool.Success(sceneResult.Stats);
                    }
                    var result = VfxService.Preview(path, o);
                    return result.Png != null ? ResponseCallTool.Image(result.Png, Png, result.Stats) : ResponseCallTool.Success(result.Stats);
                }
                catch (Exception ex)
                {
                    return ResponseCallTool.Error(ex.Message);
                }
            });
        }

        [AiTool(TextureToolId, Title = "VFX / Texture", Enabled = false)]
        [AiSkillDescription("Generate procedural particle textures (glow, circle, ring, spark, star, flare, smoke flipbook, tiling noise, trail, shockwave, polygon, lightning) into Assets/VFX/_Textures, or list existing ones with their recipes.")]
        [AiSkillBody("# VFX / Texture\n\n`action=create` writes `Assets/VFX/_Textures/<name>.png` (white RGB, shape in alpha, import settings set) and stores the recipe as its description. `action=list` prints every texture with its recipe. Reference a texture in a spec as `\"material\":\"<name>/add\"` (blend add|alpha|premul|mul); flipbook frames are wired automatically.")]
        [Description("Generate a procedural particle texture (action=create, name, recipe JSON) or list existing ones (action=list)." + SeeSkill)]
        public ResponseCallTool Texture
        (
            [Description("create | list")]
            string action = "create",
            [Description("Texture name (file name without extension).")]
            string? name = null,
            [Description("Recipe JSON, e.g. {\"shape\":\"smoke\",\"size\":512,\"frames\":[4,4],\"seed\":3}. Shapes: glow circle ring spark star flare smoke noise trail shockwave polygon lightning.")]
            string? recipe = null,
            [Description("true = also return the texture image.")]
            bool preview = false
        )
        {
            return MainThread.Instance.Run(() =>
            {
                try
                {
                    if (action == "list")
                    {
                        var list = VfxTextures.List();
                        return ResponseCallTool.Success(list.Count == 0 ? "no textures yet in " + VfxTextures.Folder : string.Join("\n", list));
                    }
                    if (action != "create") return ResponseCallTool.Error($"unknown action '{action}'. Valid: create|list");
                    var path = VfxTextures.Create(name ?? "", VfxService.ParseObject(recipe, "recipe"));
                    var text = $"{path} {VfxTextures.RecipeOf(VfxTextures.Find(path))}";
                    if (!preview) return ResponseCallTool.Success(text);
                    return ResponseCallTool.Image(System.IO.File.ReadAllBytes(path), Png, text);
                }
                catch (Exception ex)
                {
                    return ResponseCallTool.Error(ex.Message);
                }
            });
        }

        [AiTool(HistoryToolId, Title = "VFX / History", Enabled = false)]
        [AiSkillDescription("List, score or restore saved versions of a VFX prefab. Every vfx-apply stores one; manual Inspector edits are captured before the next apply.")]
        [AiSkillBody("# VFX / History\n\n`action=list` shows `v<id> time score note`. `action=score` stores a 0-1 score (and note) on a version so the best one can be picked. `action=restore` rebuilds the prefab from a version and records the restore as a new version.")]
        [Description("VFX prefab versions: action=list | score (id, score 0-1, note) | restore (id)." + SeeSkill)]
        public ResponseCallTool History
        (
            [Description("Prefab path.")]
            string path,
            [Description("list | score | restore")]
            string action = "list",
            [Description("Version id for score/restore.")]
            int id = 0,
            [Description("Score 0-1 for action=score.")]
            float score = 0f,
            [Description("Note for action=score.")]
            string? note = null
        )
        {
            return MainThread.Instance.Run(() =>
            {
                try
                {
                    switch (action)
                    {
                        case "list":
                        {
                            var entries = VfxHistory.List(path);
                            if (entries.Count == 0) return ResponseCallTool.Success("no history");
                            return ResponseCallTool.Success(string.Join("\n", entries.Select(e =>
                                $"v{e.Id} {e.Time} {(e.Score.HasValue ? e.Score.Value.ToString("0.00", CultureInfo.InvariantCulture) : "-")} {e.Note}")));
                        }
                        case "score":
                            VfxHistory.Rate(path, id, Mathf.Clamp01(score), note);
                            return ResponseCallTool.Success($"v{id} scored {score.ToString("0.00", CultureInfo.InvariantCulture)}");
                        case "restore":
                            return ResponseCallTool.Success(VfxService.Restore(path, id).Summary(path));
                        default:
                            return ResponseCallTool.Error($"unknown action '{action}'. Valid: list|score|restore");
                    }
                }
                catch (Exception ex)
                {
                    return ResponseCallTool.Error(ex.Message);
                }
            });
        }
    }
}
