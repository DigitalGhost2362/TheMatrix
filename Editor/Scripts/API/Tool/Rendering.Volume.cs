#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Runtime.Utils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Rendering
    {
        public const string RenderingVolumeGetToolId = "rendering-volume-get";
        public const string RenderingVolumeSetToolId = "rendering-volume-set";

        [AiTool
        (
            RenderingVolumeGetToolId,
            Title = "Rendering / Volume Get",
            ReadOnlyHint = true,
            IdempotentHint = true,
            Enabled = false
        )]
        [AiSkillDescription("List the Volumes of the open scenes, or read the overrides of one Volume (scene object path) or Volume Profile asset: " +
            "every component with its parameters and whether each is overridden. Works with URP/HDRP post-processing and any pipeline that uses Volumes.")]
        [Description("Without target: lists the Volumes in the loaded scenes (path, global, priority, weight, profile). With target (scene object path with a Volume, or a " +
            "'.asset' Volume Profile path): the profile's components and parameters. Only overridden parameters are shown unless includeAll is true.")]
        public string VolumeGet
        (
            [Description("Scene path of a GameObject with a Volume, or an 'Assets/.../Profile.asset' Volume Profile. Omit to list the scene's Volumes.")]
            string? target = null,
            [Description("Also show parameters that are not overridden. Default: false")]
            bool includeAll = false
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var types = VolumeTypes.Require();
                if (string.IsNullOrWhiteSpace(target))
                    return ListVolumes(types);
                var (profile, label) = ResolveProfile(types, target!, createInstance: false);
                return DescribeProfile(types, profile, label, includeAll);
            });
        }

        [AiTool
        (
            RenderingVolumeSetToolId,
            Title = "Rendering / Volume Set",
            Enabled = false
        )]
        [AiSkillDescription("Set one Volume override: component (e.g. 'Bloom'), parameter (e.g. 'intensity'), value and override state, on a scene Volume or a Volume Profile asset. " +
            "Adds the component when missing. One undo step. Use 'rendering-volume-get' to see the names.")]
        [Description("Sets a parameter of a Volume component on the target's profile (scene object path with a Volume, or a Volume Profile asset). " +
            "Values: numbers, true/false, '#RRGGBB' colors, 'x,y[,z[,w]]' vectors, enum names, or an asset path / 'none' for object references. " +
            "Passing a value turns the override on unless overrideState=false. Profile assets are saved; Volumes with an instance profile mark the scene dirty. Editor-changing.")]
        public string VolumeSet
        (
            [Description("Scene path of a GameObject with a Volume, or an 'Assets/.../Profile.asset' Volume Profile.")]
            string target,
            [Description("Volume component type name, e.g. 'Bloom', 'Vignette', 'ColorAdjustments'.")]
            string component,
            [Description("Parameter field name, e.g. 'intensity'. Omit to only add the component or change 'active'.")]
            string? parameter = null,
            [Description("New value (see the tool description). Omit to only change overrideState.")]
            string? value = null,
            [Description("Override flag of the parameter. Default: true when a value is given, otherwise unchanged.")]
            bool? overrideState = null,
            [Description("Turn the whole component on or off.")]
            bool? active = null,
            [Description("Add the component when the profile does not have it. Default: true")]
            bool addIfMissing = true
        )
        {
            if (string.IsNullOrWhiteSpace(target))
                throw new ArgumentException("target is required.");
            if (string.IsNullOrWhiteSpace(component))
                throw new ArgumentException("component is required.");

            return MainThread.Instance.Run(() =>
            {
                var types = VolumeTypes.Require();
                var (profile, label) = ResolveProfile(types, target, createInstance: true);
                var compType = types.FindComponentType(component);

                Undo.IncrementCurrentGroup();
                var group = Undo.GetCurrentGroup();
                Undo.RecordObject(profile, "Matrix volume set");

                var comp = types.GetComponent(profile, compType);
                if (comp == null)
                {
                    if (!addIfMissing)
                        throw new InvalidOperationException($"The profile has no '{compType.Name}' component and addIfMissing is false.");
                    comp = types.AddComponent(profile, compType);
                }

                Undo.RecordObject(comp, "Matrix volume set");
                if (active.HasValue)
                    VolumeTypes.Set(types.ComponentActive, comp, active.Value);

                string? changed = null;
                if (!string.IsNullOrEmpty(parameter))
                {
                    var field = types.ParameterFields(compType).FirstOrDefault(f => string.Equals(f.Name, parameter, StringComparison.OrdinalIgnoreCase))
                        ?? throw new ArgumentException($"'{compType.Name}' has no parameter '{parameter}'. Parameters: {string.Join(", ", types.ParameterFields(compType).Select(f => f.Name))}.");
                    var param = field.GetValue(comp)!;
                    if (value != null)
                        VolumeTypes.SetValue(param, value);
                    var state = overrideState ?? (value != null ? true : (bool?)null);
                    if (state.HasValue)
                        VolumeTypes.Set(types.OverrideState, param, state.Value);
                    changed = field.Name;
                }
                else if (value != null || overrideState.HasValue)
                    throw new ArgumentException("parameter is required when value or overrideState is given.");

                EditorUtility.SetDirty(comp);
                EditorUtility.SetDirty(profile);
                if (EditorUtility.IsPersistent(profile))
                    AssetDatabase.SaveAssetIfDirty(profile);
                else
                {
                    var scene = types.SceneOf(target);
                    if (scene.IsValid())
                        EditorSceneManager.MarkSceneDirty(scene);
                }
                Undo.SetCurrentGroupName("Matrix volume set");
                Undo.CollapseUndoOperations(group);

                return $"updated {label} {compType.Name}{(changed != null ? "." + changed : string.Empty)}\n" + DescribeProfile(types, profile, label, false, compType);
            });
        }

        static string ListVolumes(VolumeTypes t)
        {
            var sb = new StringBuilder();
            var volumes = Resources.FindObjectsOfTypeAll(t.Volume)
                .OfType<Behaviour>().Where(v => v.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(v)).OrderBy(v => v.gameObject.GetPath()).ToList();
            sb.AppendLine($"volumes={volumes.Count}");
            foreach (var v in volumes)
            {
                var shared = VolumeTypes.Get(t.SharedProfile, v) as UnityEngine.Object;
                var prof = shared != null ? AssetDatabase.GetAssetPath(shared) : "(instance)";
                sb.AppendLine($"{v.gameObject.GetPath()} global={VolumeTypes.Get(t.IsGlobal, v)} priority={RenderingParse.F(Convert.ToSingle(VolumeTypes.Get(t.Priority, v)))} " +
                    $"weight={RenderingParse.F(Convert.ToSingle(VolumeTypes.Get(t.Weight, v)))} enabled={v.enabled} profile={prof}");
            }
            return sb.ToString().TrimEnd();
        }

        static (UnityEngine.Object Profile, string Label) ResolveProfile(VolumeTypes t, string target, bool createInstance)
        {
            if (target.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                var asset = AssetDatabase.LoadAssetAtPath(target, t.Profile)
                    ?? throw new ArgumentException($"target: no Volume Profile at '{target}'.");
                return (asset, target);
            }

            var go = SceneLookup.Find(target)
                ?? throw new ArgumentException($"target: no scene object '{target}' (and it is not an '.asset' path).");
            var volume = go.GetComponent(t.Volume)
                ?? throw new ArgumentException($"target: '{target}' has no Volume component.");
            var profile = VolumeTypes.Get(t.SharedProfile, volume) as UnityEngine.Object;
            if (profile == null)
            {
                if (!createInstance)
                    throw new ArgumentException($"target: the Volume on '{target}' has no profile.");
                profile = VolumeTypes.Get(t.InstanceProfile, volume) as UnityEngine.Object
                    ?? throw new InvalidOperationException("Could not create an instance profile.");
                return (profile, target + " (instance profile)");
            }
            return (profile, target + " (" + AssetDatabase.GetAssetPath(profile) + ")");
        }

        static string DescribeProfile(VolumeTypes t, UnityEngine.Object profile, string label, bool includeAll, Type? only = null)
        {
            var sb = new StringBuilder();
            var comps = ((IEnumerable)VolumeTypes.Get(t.Components, profile)!).Cast<UnityEngine.Object>().Where(c => c != null).ToList();
            sb.AppendLine($"profile={label} components={comps.Count}");
            foreach (var c in comps)
            {
                if (only != null && c.GetType() != only)
                    continue;
                sb.AppendLine($"{c.GetType().Name} active={VolumeTypes.Get(t.ComponentActive, c)}");
                foreach (var f in t.ParameterFields(c.GetType()))
                {
                    var p = f.GetValue(c);
                    if (p == null)
                        continue;
                    var overridden = (bool)VolumeTypes.Get(t.OverrideState, p)!;
                    if (!overridden && !includeAll)
                        continue;
                    sb.AppendLine($"  {f.Name} = {VolumeTypes.Format(VolumeTypes.GetValue(p))}{(overridden ? " [override]" : string.Empty)}");
                }
            }
            return sb.ToString().TrimEnd();
        }
    }

    // Volumes live in the render-pipelines core package; reflect so the bridge builds without it
    internal sealed class VolumeTypes
    {
        public Type Volume = null!, Profile = null!, Component = null!, Parameter = null!;
        public MemberInfo SharedProfile = null!, InstanceProfile = null!, IsGlobal = null!, Priority = null!, Weight = null!;
        public MemberInfo Components = null!, ComponentActive = null!, OverrideState = null!;
        readonly Dictionary<Type, FieldInfo[]> _fields = new();

        static VolumeTypes? _cached;

        public static VolumeTypes Require()
        {
            if (_cached != null)
                return _cached;
            Type? Find(string full) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(full, false)).FirstOrDefault(x => x != null);
            var volume = Find("UnityEngine.Rendering.Volume");
            var profile = Find("UnityEngine.Rendering.VolumeProfile");
            var component = Find("UnityEngine.Rendering.VolumeComponent");
            var parameter = Find("UnityEngine.Rendering.VolumeParameter");
            if (volume == null || profile == null || component == null || parameter == null)
                throw new InvalidOperationException("Volumes are not available: the render-pipelines core package (URP/HDRP) is not installed.");
            MemberInfo P(Type type, string name) => (MemberInfo?)type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                ?? type.GetField(name, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"{type.Name}.{name} was not found; this render pipeline version is not supported.");
            return _cached = new VolumeTypes
            {
                Volume = volume, Profile = profile, Component = component, Parameter = parameter,
                SharedProfile = P(volume, "sharedProfile"), InstanceProfile = P(volume, "profile"),
                IsGlobal = P(volume, "isGlobal"), Priority = P(volume, "priority"), Weight = P(volume, "weight"),
                Components = P(profile, "components"), ComponentActive = P(component, "active"), OverrideState = P(parameter, "overrideState"),
            };
        }

        public UnityEngine.SceneManagement.Scene SceneOf(string target)
            => SceneLookup.Find(target)?.scene ?? default;

        public FieldInfo[] ParameterFields(Type componentType)
        {
            if (!_fields.TryGetValue(componentType, out var fields))
                _fields[componentType] = fields = componentType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Where(f => Parameter.IsAssignableFrom(f.FieldType)).ToArray();
            return fields;
        }

        public Type FindComponentType(string name)
        {
            var all = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(x => x != null)!; } })
                .Where(x => Component.IsAssignableFrom(x) && !x.IsAbstract).ToList();
            var hit = all.FirstOrDefault(x => string.Equals(x.FullName, name, StringComparison.OrdinalIgnoreCase))
                ?? all.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            return hit ?? throw new ArgumentException($"component: no Volume component named '{name}'. Examples: {string.Join(", ", all.Select(x => x.Name).Distinct().Take(12))}.");
        }

        public UnityEngine.Object? GetComponent(UnityEngine.Object profile, Type type)
            => ((IEnumerable)Get(Components, profile)!).Cast<UnityEngine.Object>().FirstOrDefault(c => c != null && c.GetType() == type);

        public UnityEngine.Object AddComponent(UnityEngine.Object profile, Type type)
        {
            var comp = (ScriptableObject)ScriptableObject.CreateInstance(type);
            comp.name = type.Name;
            if (EditorUtility.IsPersistent(profile))
                AssetDatabase.AddObjectToAsset(comp, profile);
            ((IList)Get(Components, profile)!).Add(comp);
            Undo.RegisterCreatedObjectUndo(comp, "Matrix volume set");
            return comp;
        }

        public static object? Get(MemberInfo m, object o) => m is FieldInfo f ? f.GetValue(o) : ((PropertyInfo)m).GetValue(o);

        public static void Set(MemberInfo m, object o, object? v)
        {
            if (m is FieldInfo f) f.SetValue(o, v);
            else ((PropertyInfo)m).SetValue(o, v);
        }

        public static object? GetValue(object param) => param.GetType().GetProperty("value")?.GetValue(param);

        public static void SetValue(object param, string text)
        {
            var prop = param.GetType().GetProperty("value") ?? throw new InvalidOperationException("This parameter has no value.");
            prop.SetValue(param, Parse(prop.PropertyType, text.Trim()));
        }

        static object? Parse(Type type, string text)
        {
            if (type == typeof(float)) return RenderingParse.Floats(text, 1)[0];
            if (type == typeof(int)) return (int)RenderingParse.Floats(text, 1)[0];
            if (type == typeof(bool))
                return bool.TryParse(text, out var b) ? b : throw new ArgumentException($"'{text}' is not true/false.");
            if (type == typeof(string)) return text;
            if (type == typeof(Color)) return RenderingParse.RequireColor("value", text);
            if (type == typeof(Vector2)) { var v = RenderingParse.Floats(text, 2); return new Vector2(v[0], v[1]); }
            if (type == typeof(Vector3)) { var v = RenderingParse.Floats(text, 3); return new Vector3(v[0], v[1], v[2]); }
            if (type == typeof(Vector4)) { var v = RenderingParse.Floats(text, 4); return new Vector4(v[0], v[1], v[2], v[3]); }
            if (type.IsEnum)
                return Enum.TryParse(type, text, true, out var e) && Enum.IsDefined(type, e!)
                    ? e : throw new ArgumentException($"'{text}' is not valid. Allowed: {string.Join(", ", Enum.GetNames(type))}.");
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                if (text.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
                return AssetDatabase.LoadAssetAtPath(text, type) ?? throw new ArgumentException($"No {type.Name} at '{text}'.");
            }
            throw new ArgumentException($"Parameters of type {type.Name} cannot be set with this tool.");
        }

        public static string Format(object? v) => v switch
        {
            null => "(null)",
            float f => RenderingParse.F(f),
            Color c => RenderingParse.Color(c),
            Vector2 a => $"{RenderingParse.F(a.x)},{RenderingParse.F(a.y)}",
            Vector3 a => $"{RenderingParse.F(a.x)},{RenderingParse.F(a.y)},{RenderingParse.F(a.z)}",
            Vector4 a => $"{RenderingParse.F(a.x)},{RenderingParse.F(a.y)},{RenderingParse.F(a.z)},{RenderingParse.F(a.w)}",
            AnimationCurve curve => $"curve({curve.length} keys)",
            UnityEngine.Object o => o == null ? "(null)" : (AssetDatabase.GetAssetPath(o) is { Length: > 0 } path ? path : o.name),
            _ => v.ToString() ?? string.Empty,
        };
    }
}
