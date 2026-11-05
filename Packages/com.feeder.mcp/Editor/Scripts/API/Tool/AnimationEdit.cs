#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    public sealed class CurveKey
    {
        [Description("Time in seconds.")]
        public float Time { get; set; }

        [Description("Value at that time.")]
        public float Value { get; set; }

        [Description("Incoming tangent (optional; smoothed automatically when omitted).")]
        public float? InTangent { get; set; }

        [Description("Outgoing tangent (optional; smoothed automatically when omitted).")]
        public float? OutTangent { get; set; }
    }

    public sealed class ClipEvent
    {
        [Description("Time in seconds.")]
        public float Time { get; set; }

        [Description("Name of the method called on the animated object.")]
        public string FunctionName { get; set; } = string.Empty;

        [Description("Optional float parameter.")]
        public float? FloatParameter { get; set; }

        [Description("Optional int parameter.")]
        public int? IntParameter { get; set; }

        [Description("Optional string parameter.")]
        public string? StringParameter { get; set; }
    }

    public sealed class BlendChild
    {
        [Description("Asset path of the child clip (or blend tree).")]
        public string Motion { get; set; } = string.Empty;

        [Description("Threshold for a 1D blend tree.")]
        public float Threshold { get; set; }

        [Description("X position for a 2D blend tree.")]
        public float X { get; set; }

        [Description("Y position for a 2D blend tree.")]
        public float Y { get; set; }
    }

    public sealed class ControllerOp
    {
        [Description("add-parameter, remove-parameter, add-layer, remove-layer, add-state, remove-state, set-default-state, add-transition or add-blend-tree.")]
        public string Op { get; set; } = string.Empty;

        [Description("Layer name (default: the first layer). For add-layer it is the new layer's name.")]
        public string? Layer { get; set; }

        [Description("Name of the parameter, layer or state the operation works on.")]
        public string? Name { get; set; }

        [Description("Parameter type for add-parameter: Float, Int, Bool or Trigger.")]
        public string? Type { get; set; }

        [Description("Default value of a parameter (Bool: 0 or 1).")]
        public float? Default { get; set; }

        [Description("Asset path of the clip a state plays (add-state).")]
        public string? Motion { get; set; }

        [Description("State playback speed (add-state).")]
        public float? Speed { get; set; }

        [Description("Layer weight (add-layer), 0 to 1.")]
        public float? Weight { get; set; }

        [Description("Source state of add-transition: a state name, 'Any' (Any State) or 'Entry'.")]
        public string? From { get; set; }

        [Description("Destination state of add-transition.")]
        public string? To { get; set; }

        [Description("Transition conditions, all must hold: 'Speed > 0.5', 'Speed < 1', 'Mode == 2', 'Mode != 2', 'Jump' (bool true or trigger), '!Grounded' (bool false).")]
        public string[]? Conditions { get; set; }

        [Description("Transition has exit time. Default: false when there are conditions, else true.")]
        public bool? HasExitTime { get; set; }

        [Description("Normalized exit time. Default: 0.9")]
        public float? ExitTime { get; set; }

        [Description("Transition duration in seconds (fixed). Default: 0.1")]
        public float? Duration { get; set; }

        [Description("Blend tree type for add-blend-tree: Simple1D, SimpleDirectional2D, FreeformDirectional2D or FreeformCartesian2D. Default: Simple1D")]
        public string? BlendType { get; set; }

        [Description("Parameter that drives the blend tree (X axis).")]
        public string? BlendParameter { get; set; }

        [Description("Second parameter for 2D blend trees (Y axis).")]
        public string? BlendParameterY { get; set; }

        [Description("Blend tree children.")]
        public BlendChild[]? Children { get; set; }
    }

    /// <summary>The clip and controller edits, kept free of tool plumbing so they can be tested on temporary assets.</summary>
    public static class AnimationEdit
    {
        public const int MaxKeys = 10000;
        public const int MaxOps = 200;

        public static string CheckAssetPath(string? path, string extension)
        {
            var clean = (path ?? string.Empty).Trim().Replace('\\', '/');
            if (!clean.StartsWith("Assets/", StringComparison.Ordinal) || !clean.EndsWith(extension, StringComparison.OrdinalIgnoreCase) || clean.Split('/').Contains(".."))
                throw new ArgumentException($"path '{clean}' must be a project path like 'Assets/Animations/Name{extension}'.");
            return clean;
        }

        public static Type ResolveType(string? name)
        {
            var wanted = (name ?? string.Empty).Trim();
            if (wanted.Length == 0)
                throw new ArgumentException("componentType is empty. Use a type name such as 'Transform' or 'UnityEngine.Light'.");
            var matches = TypeCache.GetTypesDerivedFrom<UnityEngine.Object>()
                .Where(t => string.Equals(t.FullName, wanted, StringComparison.OrdinalIgnoreCase) || string.Equals(t.Name, wanted, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count > 1)
            {
                var exact = matches.Where(t => string.Equals(t.FullName, wanted, StringComparison.OrdinalIgnoreCase)).ToList();
                matches = exact.Count > 0 ? exact : matches.Where(t => t.Namespace == "UnityEngine").ToList();
            }
            if (matches.Count == 0)
                throw new ArgumentException($"Type '{wanted}' was not found.");
            if (matches.Count > 1)
                throw new ArgumentException($"Type '{wanted}' is ambiguous: {string.Join(", ", matches.Select(t => t.FullName))}. Give the full name.");
            return matches[0];
        }

        static readonly string[] AnimatableAliases = { "localEulerAngles", "localEulerAnglesRaw", "localEulerAnglesBaked", "material", "blendShape", "IsActive" };

        /// <summary>Unity accepts bindings to properties a component does not have; this only flags a likely typo.</summary>
        public static string? PropertyWarning(Type type, string property)
        {
            var segment = (property ?? string.Empty).Trim().Split('.')[0];
            if (segment.Length == 0)
                return null;
            var bare = segment.StartsWith("m_", StringComparison.Ordinal) ? segment.Substring(2) : segment;
            if (AnimatableAliases.Any(a => string.Equals(a, bare, StringComparison.OrdinalIgnoreCase)))
                return null;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                if (t.GetFields(flags).Any(f => string.Equals(f.Name.TrimStart('_'), bare, StringComparison.OrdinalIgnoreCase)) ||
                    t.GetProperties(flags).Any(p => string.Equals(p.Name, bare, StringComparison.OrdinalIgnoreCase)))
                    return null;
            }
            return $"warning: '{segment}' is not a known property of {type.Name}; Unity accepts the binding but it will animate nothing.";
        }

        public static AnimationCurve BuildCurve(IReadOnlyList<CurveKey>? keys, string? interpolation)
        {
            if (keys == null || keys.Count == 0)
                throw new ArgumentException("keys is empty. Give at least one {time, value} key.");
            if (keys.Count > MaxKeys)
                throw new ArgumentException($"At most {MaxKeys} keys, got {keys.Count}.");
            var mode = (interpolation ?? "smooth").Trim().ToLowerInvariant();
            if (mode != "smooth" && mode != "linear" && mode != "constant")
                throw new ArgumentException("interpolation must be 'smooth', 'linear' or 'constant'.");

            var sorted = keys.OrderBy(k => k.Time).ToList();
            for (var i = 0; i < sorted.Count; i++)
            {
                if (float.IsNaN(sorted[i].Time) || float.IsInfinity(sorted[i].Time) || sorted[i].Time < 0)
                    throw new ArgumentException($"Key #{i}: time must be a number >= 0.");
                if (float.IsNaN(sorted[i].Value) || float.IsInfinity(sorted[i].Value))
                    throw new ArgumentException($"Key #{i}: value must be a finite number.");
                if (i > 0 && Mathf.Approximately(sorted[i].Time, sorted[i - 1].Time))
                    throw new ArgumentException($"Two keys share the time {sorted[i].Time.ToString(CultureInfo.InvariantCulture)}.");
            }

            var curve = new AnimationCurve();
            for (var i = 0; i < sorted.Count; i++)
            {
                var k = sorted[i];
                float inT = 0, outT = 0;
                if (mode == "linear")
                {
                    inT = i > 0 ? Slope(sorted[i - 1], k) : 0;
                    outT = i < sorted.Count - 1 ? Slope(k, sorted[i + 1]) : 0;
                }
                else if (mode == "constant")
                {
                    outT = i < sorted.Count - 1 ? float.PositiveInfinity : 0;
                }
                curve.AddKey(new Keyframe(k.Time, k.Value, k.InTangent ?? inT, k.OutTangent ?? outT));
            }
            if (mode == "smooth")
                for (var i = 0; i < sorted.Count; i++)
                    if (!sorted[i].InTangent.HasValue && !sorted[i].OutTangent.HasValue)
                        curve.SmoothTangents(i, 0);
            return curve;
        }

        static float Slope(CurveKey a, CurveKey b) => (b.Value - a.Value) / (b.Time - a.Time);

        public static AnimationClip CreateClip(string path, float frameRate, bool loop)
        {
            if (frameRate <= 0 || frameRate > 1000)
                throw new ArgumentException("frameRate must be between 1 and 1000.");
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
                throw new InvalidOperationException($"'{path}' already exists.");
            EnsureFolder(path);
            var clip = new AnimationClip { frameRate = frameRate };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, path);
            AssetDatabase.SaveAssetIfDirty(clip);
            return clip;
        }

        public static AnimationClip LoadClip(string path)
            => AssetDatabase.LoadAssetAtPath<AnimationClip>(path) ?? throw new ArgumentException($"No AnimationClip at '{path}'.");

        public static int SetCurve(AnimationClip clip, string bindingPath, string componentType, string property, IReadOnlyList<CurveKey>? keys, string? interpolation, bool remove)
        {
            if (string.IsNullOrWhiteSpace(property))
                throw new ArgumentException("property is empty. Use e.g. 'm_LocalPosition.x' for a Transform or 'm_Intensity' for a Light.");
            var type = ResolveType(componentType);
            var binding = EditorCurveBinding.FloatCurve((bindingPath ?? string.Empty).Trim().Trim('/'), type, property.Trim());
            var curve = remove ? null : BuildCurve(keys, interpolation);
            Undo.RegisterCompleteObjectUndo(clip, remove ? "Remove animation curve" : "Set animation curve");
            AnimationUtility.SetEditorCurve(clip, binding, curve);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            return curve?.length ?? 0;
        }

        public static void SetEvents(AnimationClip clip, IReadOnlyList<ClipEvent>? events)
        {
            var list = new List<AnimationEvent>();
            foreach (var e in events ?? Array.Empty<ClipEvent>())
            {
                if (string.IsNullOrWhiteSpace(e.FunctionName))
                    throw new ArgumentException("An event has no functionName.");
                if (float.IsNaN(e.Time) || e.Time < 0)
                    throw new ArgumentException($"Event '{e.FunctionName}': time must be >= 0.");
                list.Add(new AnimationEvent
                {
                    time = e.Time,
                    functionName = e.FunctionName.Trim(),
                    floatParameter = e.FloatParameter ?? 0,
                    intParameter = e.IntParameter ?? 0,
                    stringParameter = e.StringParameter ?? string.Empty,
                });
            }
            Undo.RegisterCompleteObjectUndo(clip, "Set animation events");
            AnimationUtility.SetAnimationEvents(clip, list.OrderBy(x => x.time).ToArray());
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
        }

        public static string DescribeClip(AnimationClip clip)
        {
            var sb = new StringBuilder();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            sb.Append($"length_s={clip.length:0.###} frame_rate={clip.frameRate:0.###} loop={settings.loopTime} legacy={clip.legacy}");
            var bindings = AnimationUtility.GetCurveBindings(clip);
            sb.Append($"\ncurves ({bindings.Length}):");
            foreach (var b in bindings)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                var keys = curve == null ? "" : string.Join(" ", curve.keys.Take(8).Select(k => $"{k.time:0.###}={k.value:0.###}"));
                var more = curve != null && curve.length > 8 ? $" (+{curve.length - 8} more)" : string.Empty;
                sb.Append($"\n- path='{b.path}' type={b.type.Name} property={b.propertyName} keys={curve?.length ?? 0}: {keys}{more}");
            }
            var events = AnimationUtility.GetAnimationEvents(clip);
            sb.Append($"\nevents ({events.Length}):");
            foreach (var e in events)
                sb.Append($"\n- t={e.time:0.###} {e.functionName}(float={e.floatParameter:0.###} int={e.intParameter} string='{e.stringParameter}')");
            return sb.ToString();
        }

        public static AnimatorController CreateController(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
                throw new InvalidOperationException($"'{path}' already exists.");
            EnsureFolder(path);
            return AnimatorController.CreateAnimatorControllerAtPath(path);
        }

        public static AnimatorController LoadController(string path)
            => AssetDatabase.LoadAssetAtPath<AnimatorController>(path) ?? throw new ArgumentException($"No AnimatorController at '{path}'.");

        static void EnsureFolder(string assetPath)
        {
            var folder = Path.GetDirectoryName(assetPath)?.Replace('\\', '/') ?? "Assets";
            if (AssetDatabase.IsValidFolder(folder))
                return;
            var parts = folder.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        public static (string Parameter, AnimatorConditionMode Mode, float Threshold) ParseCondition(string text, IReadOnlyList<AnimatorControllerParameter> parameters)
        {
            var raw = (text ?? string.Empty).Trim();
            if (raw.Length == 0)
                throw new ArgumentException("A condition is empty.");

            string name;
            string op = string.Empty;
            var number = 0f;
            var negated = raw.StartsWith("!", StringComparison.Ordinal);
            var index = raw.IndexOfAny(new[] { '<', '>', '=', '!' }, negated ? 1 : 0);
            if (negated && index < 0)
            {
                name = raw.Substring(1).Trim();
            }
            else if (index < 0)
            {
                name = raw;
            }
            else
            {
                name = raw.Substring(0, index).Trim();
                if (negated)
                    throw new ArgumentException($"Condition '{raw}': '!' only goes with a Bool parameter.");
                var rest = raw.Substring(index);
                op = rest.StartsWith("==", StringComparison.Ordinal) || rest.StartsWith("!=", StringComparison.Ordinal) ? rest.Substring(0, 2) : rest.Substring(0, 1);
                var value = rest.Substring(op.Length).Trim();
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                    throw new ArgumentException($"Condition '{raw}': '{value}' is not a number.");
            }

            var parameter = parameters.FirstOrDefault(p => p.name == name)
                ?? throw new ArgumentException($"Condition '{raw}': no parameter named '{name}'.");

            switch (parameter.type)
            {
                case AnimatorControllerParameterType.Bool:
                    if (op.Length > 0)
                        throw new ArgumentException($"Condition '{raw}': a Bool parameter is written 'Name' or '!Name'.");
                    return (name, negated ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If, 0);
                case AnimatorControllerParameterType.Trigger:
                    if (op.Length > 0 || negated)
                        throw new ArgumentException($"Condition '{raw}': a Trigger parameter is written just 'Name'.");
                    return (name, AnimatorConditionMode.If, 0);
                case AnimatorControllerParameterType.Float:
                    if (op == ">") return (name, AnimatorConditionMode.Greater, number);
                    if (op == "<") return (name, AnimatorConditionMode.Less, number);
                    throw new ArgumentException($"Condition '{raw}': a Float parameter supports '>' and '<'.");
                default:
                    if (op == ">") return (name, AnimatorConditionMode.Greater, number);
                    if (op == "<") return (name, AnimatorConditionMode.Less, number);
                    if (op == "==") return (name, AnimatorConditionMode.Equals, number);
                    if (op == "!=") return (name, AnimatorConditionMode.NotEqual, number);
                    throw new ArgumentException($"Condition '{raw}': an Int parameter supports '>', '<', '==' and '!='.");
            }
        }

        public static string ApplyOps(AnimatorController controller, IReadOnlyList<ControllerOp>? ops)
        {
            if (ops == null || ops.Count == 0)
                throw new ArgumentException("operations is empty.");
            if (ops.Count > MaxOps)
                throw new ArgumentException($"At most {MaxOps} operations, got {ops.Count}.");

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Matrix animator edit");
            var group = Undo.GetCurrentGroup();
            var done = 0;
            try
            {
                foreach (var op in ops)
                {
                    Undo.RegisterCompleteObjectUndo(controller, "Matrix animator edit");
                    Apply(controller, op);
                    done++;
                }
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
            {
                Finish(controller, group);
                throw new ArgumentException($"Applied {done} of {ops.Count} operations; #{done} ('{ops[done].Op}') failed: {e.Message}");
            }
            Finish(controller, group);
            return $"Applied {done} operation(s).";
        }

        static void Finish(AnimatorController controller, int group)
        {
            Undo.CollapseUndoOperations(group);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
        }

        static AnimatorControllerParameterType ParseParameterType(string? text)
            => Enum.TryParse<AnimatorControllerParameterType>((text ?? string.Empty).Trim(), true, out var type)
                ? type
                : throw new ArgumentException("type must be Float, Int, Bool or Trigger.");

        static int LayerIndex(AnimatorController c, string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return 0;
            var index = Array.FindIndex(c.layers, l => l.name == name);
            return index >= 0 ? index : throw new ArgumentException($"No layer named '{name}'.");
        }

        static string Need(string? value, string field)
            => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{field} is required.") : value!.Trim();

        static AnimatorState FindState(AnimatorStateMachine sm, string name)
            => sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name) ?? throw new ArgumentException($"No state named '{name}'.");

        static Motion LoadMotion(string path)
            => AssetDatabase.LoadAssetAtPath<Motion>(path.Trim()) ?? throw new ArgumentException($"No clip or blend tree at '{path}'.");

        static void Apply(AnimatorController c, ControllerOp op)
        {
            var kind = (op.Op ?? string.Empty).Trim().ToLowerInvariant();
            switch (kind)
            {
                case "add-parameter":
                {
                    var name = Need(op.Name, "name");
                    if (c.parameters.Any(p => p.name == name))
                        throw new ArgumentException($"Parameter '{name}' already exists.");
                    var type = ParseParameterType(op.Type);
                    c.AddParameter(new AnimatorControllerParameter
                    {
                        name = name,
                        type = type,
                        defaultFloat = type == AnimatorControllerParameterType.Float ? op.Default ?? 0 : 0,
                        defaultInt = type == AnimatorControllerParameterType.Int ? (int)(op.Default ?? 0) : 0,
                        defaultBool = type == AnimatorControllerParameterType.Bool && (op.Default ?? 0) != 0,
                    });
                    break;
                }
                case "remove-parameter":
                {
                    var name = Need(op.Name, "name");
                    var parameter = c.parameters.FirstOrDefault(p => p.name == name) ?? throw new ArgumentException($"No parameter named '{name}'.");
                    c.RemoveParameter(parameter);
                    break;
                }
                case "add-layer":
                {
                    var name = Need(op.Layer ?? op.Name, "layer");
                    if (c.layers.Any(l => l.name == name))
                        throw new ArgumentException($"Layer '{name}' already exists.");
                    c.AddLayer(name);
                    if (op.Weight.HasValue)
                    {
                        var layers = c.layers;
                        layers[layers.Length - 1].defaultWeight = Mathf.Clamp01(op.Weight.Value);
                        c.layers = layers;
                    }
                    break;
                }
                case "remove-layer":
                {
                    var index = LayerIndex(c, op.Layer ?? op.Name);
                    if (index == 0)
                        throw new ArgumentException("The base layer cannot be removed.");
                    c.RemoveLayer(index);
                    break;
                }
                case "add-state":
                {
                    var sm = c.layers[LayerIndex(c, op.Layer)].stateMachine;
                    var name = Need(op.Name, "name");
                    if (sm.states.Any(s => s.state.name == name))
                        throw new ArgumentException($"State '{name}' already exists.");
                    var motion = string.IsNullOrWhiteSpace(op.Motion) ? null : LoadMotion(op.Motion!);
                    var state = sm.AddState(name);
                    state.motion = motion;
                    if (op.Speed.HasValue)
                        state.speed = op.Speed.Value;
                    break;
                }
                case "remove-state":
                {
                    var sm = c.layers[LayerIndex(c, op.Layer)].stateMachine;
                    sm.RemoveState(FindState(sm, Need(op.Name, "name")));
                    break;
                }
                case "set-default-state":
                {
                    var sm = c.layers[LayerIndex(c, op.Layer)].stateMachine;
                    sm.defaultState = FindState(sm, Need(op.Name, "name"));
                    break;
                }
                case "add-transition":
                    AddTransition(c, op);
                    break;
                case "add-blend-tree":
                    AddBlendTree(c, op);
                    break;
                default:
                    throw new ArgumentException($"Unknown op '{op.Op}'. Use add-parameter, remove-parameter, add-layer, remove-layer, add-state, remove-state, set-default-state, add-transition or add-blend-tree.");
            }
        }

        static void AddTransition(AnimatorController c, ControllerOp op)
        {
            var sm = c.layers[LayerIndex(c, op.Layer)].stateMachine;
            var from = Need(op.From, "from");
            var destination = FindState(sm, Need(op.To, "to"));
            var conditions = (op.Conditions ?? Array.Empty<string>()).Select(x => ParseCondition(x, c.parameters)).ToList();

            AnimatorStateTransition transition;
            var isEntry = string.Equals(from, "Entry", StringComparison.OrdinalIgnoreCase);
            if (isEntry)
            {
                var entry = sm.AddEntryTransition(destination);
                foreach (var (parameter, mode, threshold) in conditions)
                    entry.AddCondition(mode, threshold, parameter);
                return;
            }
            if (string.Equals(from, "Any", StringComparison.OrdinalIgnoreCase))
                transition = sm.AddAnyStateTransition(destination);
            else
                transition = FindState(sm, from).AddTransition(destination);

            transition.hasExitTime = op.HasExitTime ?? conditions.Count == 0;
            transition.exitTime = op.ExitTime ?? 0.9f;
            transition.hasFixedDuration = true;
            transition.duration = op.Duration ?? 0.1f;
            foreach (var (parameter, mode, threshold) in conditions)
                transition.AddCondition(mode, threshold, parameter);
        }

        static void AddBlendTree(AnimatorController c, ControllerOp op)
        {
            var layer = LayerIndex(c, op.Layer);
            var name = Need(op.Name, "name");
            var sm = c.layers[layer].stateMachine;
            if (sm.states.Any(s => s.state.name == name))
                throw new ArgumentException($"State '{name}' already exists.");
            var typeName = string.IsNullOrWhiteSpace(op.BlendType) ? "Simple1D" : op.BlendType!.Trim();
            if (!Enum.TryParse<BlendTreeType>(typeName, true, out var blendType))
                throw new ArgumentException("blendType must be Simple1D, SimpleDirectional2D, FreeformDirectional2D or FreeformCartesian2D.");
            var parameterX = Need(op.BlendParameter, "blendParameter");
            if (c.parameters.All(p => p.name != parameterX))
                throw new ArgumentException($"No parameter named '{parameterX}'.");
            if (blendType != BlendTreeType.Simple1D)
            {
                var y = Need(op.BlendParameterY, "blendParameterY");
                if (c.parameters.All(p => p.name != y))
                    throw new ArgumentException($"No parameter named '{y}'.");
            }
            var children = (op.Children ?? Array.Empty<BlendChild>()).Select(ch => (Child: ch, Motion: LoadMotion(ch.Motion ?? string.Empty))).ToList();

            c.CreateBlendTreeInController(name, out var tree, layer);
            tree.blendType = blendType;
            tree.blendParameter = parameterX;
            if (blendType != BlendTreeType.Simple1D)
                tree.blendParameterY = op.BlendParameterY!.Trim();
            foreach (var (child, motion) in children)
            {
                if (blendType == BlendTreeType.Simple1D)
                    tree.AddChild(motion, child.Threshold);
                else
                    tree.AddChild(motion, new Vector2(child.X, child.Y));
            }
        }

        public static string DescribeController(AnimatorController c)
        {
            var sb = new StringBuilder();
            sb.Append($"parameters ({c.parameters.Length}):");
            foreach (var p in c.parameters)
                sb.Append($"\n- {p.name} {p.type} default={DefaultOf(p)}");
            var layers = c.layers;
            sb.Append($"\nlayers ({layers.Length}):");
            foreach (var layer in layers)
            {
                var sm = layer.stateMachine;
                sb.Append($"\n- layer '{layer.name}' weight={layer.defaultWeight:0.###} default_state={sm.defaultState?.name ?? "none"}");
                foreach (var cs in sm.states)
                {
                    var s = cs.state;
                    var motion = s.motion == null ? "none" : s.motion is BlendTree bt ? $"BlendTree({bt.blendType}, {bt.blendParameter}, {bt.children.Length} children)" : AssetDatabase.GetAssetPath(s.motion) is { Length: > 0 } p ? p : s.motion.name;
                    sb.Append($"\n    state '{s.name}' motion={motion} speed={s.speed:0.###}");
                    foreach (var t in s.transitions)
                        sb.Append($"\n      -> {t.destinationState?.name ?? "exit"} {FormatTransition(t)}");
                }
                foreach (var t in sm.anyStateTransitions)
                    sb.Append($"\n    Any -> {t.destinationState?.name ?? "exit"} {FormatTransition(t)}");
                foreach (var t in sm.entryTransitions)
                    sb.Append($"\n    Entry -> {t.destinationState?.name ?? "exit"} [{FormatConditions(t.conditions)}]");
            }
            return sb.ToString();
        }

        static string DefaultOf(AnimatorControllerParameter p) => p.type switch
        {
            AnimatorControllerParameterType.Float => p.defaultFloat.ToString("0.###", CultureInfo.InvariantCulture),
            AnimatorControllerParameterType.Int => p.defaultInt.ToString(CultureInfo.InvariantCulture),
            AnimatorControllerParameterType.Bool => p.defaultBool.ToString(),
            _ => "-",
        };

        static string FormatTransition(AnimatorStateTransition t)
            => $"exit_time={(t.hasExitTime ? t.exitTime.ToString("0.###", CultureInfo.InvariantCulture) : "off")} duration={t.duration:0.###} [{FormatConditions(t.conditions)}]";

        static string FormatConditions(AnimatorCondition[] conditions)
            => string.Join(" & ", conditions.Select(c => c.mode switch
            {
                AnimatorConditionMode.If => c.parameter,
                AnimatorConditionMode.IfNot => "!" + c.parameter,
                AnimatorConditionMode.Greater => $"{c.parameter} > {c.threshold:0.###}",
                AnimatorConditionMode.Less => $"{c.parameter} < {c.threshold:0.###}",
                AnimatorConditionMode.Equals => $"{c.parameter} == {c.threshold:0.###}",
                _ => $"{c.parameter} != {c.threshold:0.###}",
            }));
    }
}
