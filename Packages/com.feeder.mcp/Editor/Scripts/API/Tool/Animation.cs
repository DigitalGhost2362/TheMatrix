#nullable enable
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public static partial class Tool_Animation
    {
        public const string ClipCreateToolId = "animation-clip-create";
        public const string ClipGetToolId = "animation-clip-get";
        public const string ClipSetCurveToolId = "animation-clip-set-curve";
        public const string ClipSetEventsToolId = "animation-clip-set-events";
        public const string ControllerCreateToolId = "animation-controller-create";
        public const string ControllerGetToolId = "animation-controller-get";
        public const string ControllerEditToolId = "animation-controller-edit";

        [AiTool(ClipCreateToolId, Title = "Animation / Clip Create", Enabled = false)]
        [AiSkillDescription("Create an empty AnimationClip asset (frame rate, loop).")]
        [Description("Creates an empty AnimationClip asset. Fails when the path already exists. Add curves with 'animation-clip-set-curve'.")]
        public static string ClipCreate
        (
            [Description("Asset path ending in .anim, e.g. 'Assets/Animations/Spin.anim'.")]
            string path,
            [Description("Frames per second. Default: 60")]
            float frameRate = 60,
            [Description("Loop the clip. Default: false")]
            bool loop = false
        )
        {
            var clean = AnimationEdit.CheckAssetPath(path, ".anim");
            return MainThread.Instance.Run(() =>
            {
                var clip = AnimationEdit.CreateClip(clean, frameRate, loop);
                return $"Created {clean} (frame_rate={clip.frameRate:0.###} loop={loop}).";
            });
        }

        [AiTool(ClipGetToolId, Title = "Animation / Clip Get", ReadOnlyHint = true, IdempotentHint = true, Enabled = false)]
        [AiSkillDescription("Read an AnimationClip: length, frame rate, loop, every curve (target path, component, property, keys) and events.")]
        [Description("Reads an AnimationClip: length, frame rate, loop, curves (path, component type, property, first keys) and events.")]
        public static string ClipGet
        (
            [Description("Asset path of the .anim file.")]
            string path
        )
        {
            var clean = AnimationEdit.CheckAssetPath(path, ".anim");
            return MainThread.Instance.Run(() => AnimationEdit.DescribeClip(AnimationEdit.LoadClip(clean)));
        }

        [AiTool(ClipSetCurveToolId, Title = "Animation / Clip Set Curve", Enabled = false)]
        [AiSkillDescription("Set or remove one property curve of an AnimationClip from {time, value} keys (smooth, linear or constant).")]
        [AiSkillBody("Sets (replaces) or removes the curve of one property on an AnimationClip.\n\n" +
            "- `bindingPath` is the child path from the animated root, e.g. `Arm/Hand`; empty means the root itself.\n" +
            "- `componentType` is a type name such as `Transform`, `Light`, `SpriteRenderer`.\n" +
            "- `property` is the serialized property name: `m_LocalPosition.x`, `localEulerAnglesRaw.y`, `m_LocalScale.z`, `m_Intensity`, `m_IsActive` (on GameObject).\n" +
            "- `keys` is `[{\"time\":0,\"value\":0},{\"time\":1,\"value\":5}]`; tangents are optional.\n" +
            "- `interpolation` is `smooth` (default), `linear` or `constant`.\n" +
            "- A property the component does not have is accepted (Unity allows it) but the result carries a `warning:` line.\n" +
            "- `remove=true` deletes the curve and needs no keys.\n\n" +
            "Read the result back with `animation-clip-get`. The change is one Undo step.")]
        [Description("Sets or removes a property curve on an AnimationClip. Keys are sorted by time; times must be unique and >= 0. Undo is recorded.")]
        public static string ClipSetCurve
        (
            [Description("Asset path of the .anim file.")]
            string path,
            [Description("Child path of the animated object from the animator root ('' = the root).")]
            string bindingPath,
            [Description("Component type name, e.g. 'Transform'.")]
            string componentType,
            [Description("Serialized property name, e.g. 'm_LocalPosition.x'.")]
            string property,
            [Description("Keys {time, value, inTangent?, outTangent?}. Not needed when remove=true.")]
            CurveKey[]? keys = null,
            [Description("'smooth' (default), 'linear' or 'constant'.")]
            string? interpolation = null,
            [Description("Remove the curve instead of setting it. Default: false")]
            bool remove = false
        )
        {
            var clean = AnimationEdit.CheckAssetPath(path, ".anim");
            return MainThread.Instance.Run(() =>
            {
                var clip = AnimationEdit.LoadClip(clean);
                var count = AnimationEdit.SetCurve(clip, bindingPath, componentType, property, keys, interpolation, remove);
                var warning = remove ? null : AnimationEdit.PropertyWarning(AnimationEdit.ResolveType(componentType), property);
                var text = remove
                    ? $"Removed {property} curve from {clean}."
                    : $"Set {property} curve with {count} key(s) on {clean}; length_s={clip.length:0.###}.";
                return warning == null ? text : text + "\n" + warning;
            });
        }

        [AiTool(ClipSetEventsToolId, Title = "Animation / Clip Set Events", Enabled = false)]
        [AiSkillDescription("Replace all animation events of a clip ({time, functionName, float/int/string parameter}); an empty list clears them.")]
        [Description("Replaces every animation event of a clip with the given list (sorted by time); an empty list clears the events. Undo is recorded.")]
        public static string ClipSetEvents
        (
            [Description("Asset path of the .anim file.")]
            string path,
            [Description("Events {time, functionName, floatParameter?, intParameter?, stringParameter?}.")]
            ClipEvent[]? events = null
        )
        {
            var clean = AnimationEdit.CheckAssetPath(path, ".anim");
            return MainThread.Instance.Run(() =>
            {
                AnimationEdit.SetEvents(AnimationEdit.LoadClip(clean), events);
                return $"Clip {clean} now has {events?.Length ?? 0} event(s).";
            });
        }

        [AiTool(ControllerCreateToolId, Title = "Animation / Controller Create", Enabled = false)]
        [AiSkillDescription("Create an empty AnimatorController asset.")]
        [Description("Creates an AnimatorController asset with one base layer. Fails when the path already exists.")]
        public static string ControllerCreate
        (
            [Description("Asset path ending in .controller, e.g. 'Assets/Animations/Player.controller'.")]
            string path
        )
        {
            var clean = AnimationEdit.CheckAssetPath(path, ".controller");
            return MainThread.Instance.Run(() =>
            {
                AnimationEdit.CreateController(clean);
                return $"Created {clean}.";
            });
        }

        [AiTool(ControllerGetToolId, Title = "Animation / Controller Get", ReadOnlyHint = true, IdempotentHint = true, Enabled = false)]
        [AiSkillDescription("Read an AnimatorController: parameters, layers, states (motion, speed), transitions (conditions, exit time, duration), blend trees.")]
        [Description("Reads an AnimatorController: parameters, layers, states with their motion, and every transition with conditions.")]
        public static string ControllerGet
        (
            [Description("Asset path of the .controller file.")]
            string path
        )
        {
            var clean = AnimationEdit.CheckAssetPath(path, ".controller");
            return MainThread.Instance.Run(() => AnimationEdit.DescribeController(AnimationEdit.LoadController(clean)));
        }

        [AiTool(ControllerEditToolId, Title = "Animation / Controller Edit", Enabled = false)]
        [AiSkillDescription("Edit an AnimatorController with an ordered list of operations: parameters, layers, states, transitions with conditions, blend trees.")]
        [AiSkillBody("Applies `operations` in order to an AnimatorController (one Undo step). It stops at the first failing operation and reports how many were applied; earlier ones stay.\n\n" +
            "Operations (`op`):\n" +
            "- `add-parameter` name, type (Float|Int|Bool|Trigger), default\n" +
            "- `remove-parameter` name\n" +
            "- `add-layer` layer (new name), weight / `remove-layer` layer\n" +
            "- `add-state` name, motion (clip asset path), speed, layer\n" +
            "- `remove-state` name / `set-default-state` name\n" +
            "- `add-transition` from (state, `Any` or `Entry`), to, conditions, hasExitTime, exitTime, duration\n" +
            "- `add-blend-tree` name, blendType (Simple1D|SimpleDirectional2D|FreeformDirectional2D|FreeformCartesian2D), blendParameter, blendParameterY, children [{motion, threshold | x,y}]\n\n" +
            "Conditions: `Speed > 0.5`, `Speed < 1`, `Mode == 2`, `Mode != 2`, `Jump` (bool or trigger), `!Grounded`. " +
            "Add parameters before the conditions that use them. Omit `layer` for the base layer. Check with `animation-controller-get`.")]
        [Description("Edits an AnimatorController with ordered operations (add/remove parameter, layer, state; set default state; add transition; add blend tree). " +
            "Stops at the first failing operation; undo is recorded as one step.")]
        public static string ControllerEdit
        (
            [Description("Asset path of the .controller file.")]
            string path,
            [Description("Operations, 1 to 200, applied in order.")]
            ControllerOp[] operations
        )
        {
            var clean = AnimationEdit.CheckAssetPath(path, ".controller");
            return MainThread.Instance.Run(() => AnimationEdit.ApplyOps(AnimationEdit.LoadController(clean), operations));
        }
    }
}
