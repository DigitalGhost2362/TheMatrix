#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Feeder.Json.Nodes;
using UnityEngine;

namespace Feeder.MCP.Editor.Vfx
{
    // prefab <-> compact json. the prefab stays the source of truth: humans edit it in the inspector,
    // vfx-read shows only what differs from the baseline emitter, vfx-apply patches by emitter name.
    public static class VfxSpec
    {
        public const string MaterialKey = "material";
        public const string TrailMaterialKey = "trailMaterial";

        static readonly string[] ModuleNames =
        {
            "main", "emission", "shape", "velocityOverLifetime", "limitVelocityOverLifetime", "inheritVelocity",
            "lifetimeByEmitterSpeed", "forceOverLifetime", "colorOverLifetime", "colorBySpeed", "sizeOverLifetime",
            "sizeBySpeed", "rotationOverLifetime", "rotationBySpeed", "externalForces", "noise", "collision", "trigger",
            "subEmitters", "textureSheetAnimation", "lights", "trails", "customData"
        };

        static readonly string[] RendererProps =
        {
            "renderMode", "sortMode", "sortingFudge", "sortingOrder", "sortingLayerName", "minParticleSize", "maxParticleSize",
            "alignment", "pivot", "flip", "lengthScale", "velocityScale", "cameraVelocityScale", "normalDirection",
            "allowRoll", "mesh", "maskInteraction", "shadowCastingMode", "receiveShadows", "enableGPUInstancing"
        };

        static readonly (string module, string flag, string[] hideWhenFalse, string[] hideWhenTrue)[] AxisRules =
        {
            ("main", "startSize3D", new[] { "startSizeX", "startSizeY", "startSizeZ" }, new[] { "startSize" }),
            ("main", "startRotation3D", new[] { "startRotationX", "startRotationY", "startRotationZ" }, new[] { "startRotation" }),
            ("sizeOverLifetime", "separateAxes", new[] { "x", "y", "z" }, new[] { "size" }),
            ("sizeBySpeed", "separateAxes", new[] { "x", "y", "z" }, new[] { "size" }),
            ("rotationOverLifetime", "separateAxes", new[] { "x", "y" }, Array.Empty<string>()),
            ("rotationBySpeed", "separateAxes", new[] { "x", "y" }, Array.Empty<string>()),
            ("limitVelocityOverLifetime", "separateAxes", new[] { "limitX", "limitY", "limitZ" }, new[] { "limit" }),
        };

        static readonly HashSet<string> DegreeMembers = new HashSet<string>
        {
            "main.startRotation", "main.startRotationX", "main.startRotationY", "main.startRotationZ",
            "rotationOverLifetime.x", "rotationOverLifetime.y", "rotationOverLifetime.z",
            "rotationBySpeed.x", "rotationBySpeed.y", "rotationBySpeed.z",
        };

        static readonly string[] EmitterKeys = { "name", "parent", "pos", "rot", "scale", "remove", MaterialKey, TrailMaterialKey, "renderer" };

        sealed class Member
        {
            public string Name = "";
            public PropertyInfo Prop = null!;
            public bool Degrees;
        }

        static Dictionary<string, (PropertyInfo moduleProp, List<Member> members)>? modules;
        static List<Member>? rendererMembers;
        static Dictionary<string, string>? emitterBaseline;
        static Dictionary<string, string>? rootBaseline;

        #region Reflection

        static Dictionary<string, (PropertyInfo moduleProp, List<Member> members)> Modules
        {
            get
            {
                if (modules != null) return modules;
                modules = new Dictionary<string, (PropertyInfo, List<Member>)>();
                foreach (var name in ModuleNames)
                {
                    var moduleProp = typeof(ParticleSystem).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                                     ?? throw new MissingMemberException(nameof(ParticleSystem), name);
                    modules[name] = (moduleProp, CollectMembers(moduleProp.PropertyType, name, null));
                }
                return modules;
            }
        }

        static List<Member> RendererMembers => rendererMembers ??= CollectMembers(typeof(ParticleSystemRenderer), "renderer", RendererProps);

        static List<Member> CollectMembers(Type type, string module, string[]? whitelist)
        {
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && p.Name != "enabled")
                .Where(p => p.GetCustomAttribute<ObsoleteAttribute>() == null)
                .Where(p => VfxCodec.IsSupported(p.PropertyType))
                .Where(p => whitelist == null || whitelist.Contains(p.Name))
                .ToList();
            var curveNames = new HashSet<string>(props.Where(p => p.PropertyType == typeof(ParticleSystem.MinMaxCurve)).Select(p => p.Name));
            props.RemoveAll(p => p.Name.EndsWith("Multiplier") && curveNames.Contains(p.Name.Substring(0, p.Name.Length - "Multiplier".Length)));
            if (module == "emission") props.RemoveAll(p => p.Name == "burstCount");
            if (module == "main") props.RemoveAll(p => p.Name == "customSimulationSpace");
            return props
                .OrderBy(p => p.PropertyType == typeof(bool) ? 0 : p.PropertyType.IsEnum ? 1 : 2)
                .Select(p => new Member { Name = p.Name, Prop = p, Degrees = DegreeMembers.Contains(module + "." + p.Name) })
                .ToList();
        }

        static object? GetModule(ParticleSystem ps, string module) => Modules[module].moduleProp.GetValue(ps);

        static bool ModuleEnabled(object moduleObj)
        {
            var p = moduleObj.GetType().GetProperty("enabled");
            return p == null || (bool)p.GetValue(moduleObj);
        }

        static void SetModuleEnabled(object moduleObj, bool enabled) => moduleObj.GetType().GetProperty("enabled")?.SetValue(moduleObj, enabled);

        static bool HasEnabled(string module) => module != "main";

        static ParticleSystem.MinMaxCurve ScaleMinMax(ParticleSystem.MinMaxCurve c, float k)
        {
            switch (c.mode)
            {
                case ParticleSystemCurveMode.Constant: c.constant *= k; break;
                case ParticleSystemCurveMode.TwoConstants: c.constantMin *= k; c.constantMax *= k; break;
                default: c.curveMultiplier *= k; break;
            }
            return c;
        }

        static JsonNode? EncodeMember(object target, Member m)
        {
            object? value;
            try { value = m.Prop.GetValue(target); }
            catch { return null; }
            if (m.Degrees && value is ParticleSystem.MinMaxCurve c) value = ScaleMinMax(c, Mathf.Rad2Deg);
            return VfxCodec.Encode(value, m.Prop.PropertyType);
        }

        static void DecodeMember(object target, Member m, JsonNode? node, string field)
        {
            var value = VfxCodec.Decode(node, m.Prop.PropertyType, field);
            if (m.Degrees && value is ParticleSystem.MinMaxCurve c) value = ScaleMinMax(c, Mathf.Deg2Rad);
            m.Prop.SetValue(target, value);
        }

        static bool Hidden(string module, string member, object moduleObj)
        {
            foreach (var rule in AxisRules)
            {
                if (rule.module != module) continue;
                var flag = (bool)moduleObj.GetType().GetProperty(rule.flag)!.GetValue(moduleObj);
                if ((flag ? rule.hideWhenTrue : rule.hideWhenFalse).Contains(member)) return true;
            }
            return false;
        }

        #endregion

        #region Baseline

        public static void ConfigureNewEmitter(ParticleSystem ps)
        {
            var main = ps.main;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = 1f;
            main.startSpeed = 2f;
            main.startSize = 0.5f;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission;
            emission.rateOverTime = 10f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.1f;
        }

        public static void ConfigureRoot(ParticleSystem ps)
        {
            var main = ps.main;
            main.loop = false;
            main.duration = 1f;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            ps.GetComponent<ParticleSystemRenderer>().enabled = false;
        }

        static Dictionary<string, string> Baseline(bool root)
        {
            var cached = root ? rootBaseline : emitterBaseline;
            if (cached != null) return cached;
            var go = new GameObject("vfx-baseline") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var ps = go.AddComponent<ParticleSystem>();
                if (root) ConfigureRoot(ps);
                else ConfigureNewEmitter(ps);
                var map = new Dictionary<string, string>();
                foreach (var kv in Modules)
                {
                    var moduleObj = kv.Value.moduleProp.GetValue(ps);
                    map[kv.Key + ".enabled"] = ModuleEnabled(moduleObj).ToString();
                    foreach (var m in kv.Value.members)
                        map[kv.Key + "." + m.Name] = EncodeMember(moduleObj, m)?.ToJsonString() ?? "null";
                }
                var r = go.GetComponent<ParticleSystemRenderer>();
                map["renderer.enabled"] = r.enabled.ToString();
                foreach (var m in RendererMembers)
                    map["renderer." + m.Name] = EncodeMember(r, m)?.ToJsonString() ?? "null";
                if (root) rootBaseline = map;
                else emitterBaseline = map;
                return map;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        #endregion

        #region Read

        public static List<ParticleSystem> Emitters(GameObject root) =>
            root.GetComponentsInChildren<ParticleSystem>(true).Where(p => p.gameObject != root).ToList();

        public static JsonObject Read(GameObject root, List<string> warnings)
        {
            var spec = new JsonObject();
            var emitters = Emitters(root);
            var names = new Dictionary<ParticleSystem, string>();
            foreach (var ps in emitters)
            {
                var name = ps.name;
                if (names.ContainsValue(name)) warnings.Add($"duplicate emitter name '{name}' - rename it, patches address emitters by name");
                names[ps] = name;
            }
            var rootSpec = new JsonObject();
            var rootPs = root.GetComponent<ParticleSystem>();
            if (rootPs != null) ReadSystem(rootPs, true, names, warnings, rootSpec);
            if (root.transform.localScale != Vector3.one) rootSpec["scale"] = VfxCodec.Encode(root.transform.localScale, typeof(Vector3));
            if (rootSpec.Count > 0) spec["root"] = rootSpec;
            var list = new JsonArray();
            foreach (var ps in emitters)
            {
                var e = new JsonObject { ["name"] = names[ps] };
                var parent = ps.transform.parent;
                if (parent != null && parent.gameObject != root) e["parent"] = parent.name;
                var t = ps.transform;
                if (t.localPosition != Vector3.zero) e["pos"] = VfxCodec.Encode(t.localPosition, typeof(Vector3));
                if (t.localEulerAngles != Vector3.zero) e["rot"] = VfxCodec.Encode(t.localEulerAngles, typeof(Vector3));
                if (t.localScale != Vector3.one) e["scale"] = VfxCodec.Encode(t.localScale, typeof(Vector3));
                ReadSystem(ps, false, names, warnings, e);
                list.Add(e);
            }
            spec["emitters"] = list;
            return spec;
        }

        static void ReadSystem(ParticleSystem ps, bool isRoot, Dictionary<ParticleSystem, string> names, List<string> warnings, JsonObject o)
        {
            var baseline = Baseline(isRoot);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (!isRoot && renderer != null)
            {
                o[MaterialKey] = VfxMaterials.Describe(renderer.sharedMaterial);
                if (ps.trails.enabled) o[TrailMaterialKey] = VfxMaterials.Describe(renderer.trailMaterial);
            }
            foreach (var kv in Modules)
            {
                var module = kv.Key;
                var moduleObj = kv.Value.moduleProp.GetValue(ps);
                var enabled = ModuleEnabled(moduleObj);
                var baseEnabled = baseline[module + ".enabled"] == true.ToString();
                if (!enabled)
                {
                    if (baseEnabled) o[module] = false;
                    continue;
                }
                var mo = new JsonObject();
                if (module == "subEmitters") ReadSubEmitters(ps, mo, names, warnings);
                foreach (var m in kv.Value.members)
                {
                    if (Hidden(module, m.Name, moduleObj)) continue;
                    var node = EncodeMember(moduleObj, m);
                    var json = node?.ToJsonString() ?? "null";
                    if (baseline.TryGetValue(module + "." + m.Name, out var b) && b == json) continue;
                    if (node == null) continue;
                    mo[m.Name] = node;
                }
                if (module == "emission") ReadBursts(ps, mo);
                if (mo.Count > 0 || !baseEnabled) o[module] = mo;
            }
            if (renderer == null) return;
            var rendererBaseEnabled = baseline["renderer.enabled"] == true.ToString();
            if (!renderer.enabled)
            {
                if (rendererBaseEnabled) o["renderer"] = false;
                return;
            }
            var ro = new JsonObject();
            foreach (var m in RendererMembers)
            {
                var node = EncodeMember(renderer, m);
                if (node == null) continue;
                if (baseline.TryGetValue("renderer." + m.Name, out var b) && b == node.ToJsonString()) continue;
                ro[m.Name] = node;
            }
            if (ro.Count > 0 || !rendererBaseEnabled) o["renderer"] = ro;
        }

        static bool IsRemove(JsonObject item) => item["remove"] is JsonValue v && v.TryGetValue<bool>(out var r) && r;

        static void ReadBursts(ParticleSystem ps, JsonObject mo)
        {
            var bursts = EncodeBursts(ps);
            if (bursts != null) mo["bursts"] = bursts;
        }

        internal static JsonArray? EncodeBursts(ParticleSystem ps)
        {
            var emission = ps.emission;
            if (emission.burstCount == 0) return null;
            var bursts = new ParticleSystem.Burst[emission.burstCount];
            emission.GetBursts(bursts);
            var arr = new JsonArray();
            foreach (var b in bursts)
            {
                var item = new JsonArray(VfxCodec.NumNode(b.time), VfxCodec.EncodeMinMax(b.count));
                var plain = b.cycleCount == 1 && Mathf.Approximately(b.probability, 1f);
                if (!plain)
                {
                    item.Add(b.cycleCount);
                    item.Add(VfxCodec.NumNode(b.repeatInterval));
                    item.Add(VfxCodec.NumNode(b.probability));
                }
                arr.Add(item);
            }
            return arr;
        }

        static void ReadSubEmitters(ParticleSystem ps, JsonObject mo, Dictionary<ParticleSystem, string> names, List<string> warnings)
        {
            var sub = ps.subEmitters;
            var arr = new JsonArray();
            for (var i = 0; i < sub.subEmittersCount; i++)
            {
                var target = sub.GetSubEmitterSystem(i);
                if (target == null)
                {
                    warnings.Add($"'{ps.name}' sub emitter #{i} is empty");
                    continue;
                }
                if (!names.TryGetValue(target, out var targetName))
                {
                    warnings.Add($"'{ps.name}' sub emitter '{target.name}' is outside this effect");
                    continue;
                }
                var item = new JsonObject { ["emitter"] = targetName, ["type"] = sub.GetSubEmitterType(i).ToString() };
                var props = sub.GetSubEmitterProperties(i);
                if (props != ParticleSystemSubEmitterProperties.InheritNothing) item["inherit"] = props.ToString().Replace(", ", "|");
                var p = sub.GetSubEmitterEmitProbability(i);
                if (!Mathf.Approximately(p, 1f)) item["p"] = VfxCodec.NumNode(p);
                arr.Add(item);
            }
            mo["list"] = arr;
        }

        #endregion

        #region Apply

        public sealed class ApplyResult
        {
            public readonly List<string> Log = new List<string>();
            public readonly List<string> Errors = new List<string>();
        }

        public static void Apply(GameObject root, JsonObject spec, bool replace, ApplyResult result)
        {
            foreach (var key in spec.Select(kv => kv.Key))
                if (key != "root" && key != "emitters" && key != "defaults")
                    result.Errors.Add($"unknown top-level key '{key}'. Valid: root, defaults, emitters");

            var rootPs = root.GetComponent<ParticleSystem>();
            if (rootPs == null)
            {
                rootPs = root.AddComponent<ParticleSystem>();
                ConfigureRoot(rootPs);
            }
            if (spec["root"] is JsonObject rootSpec)
            {
                if (rootSpec["scale"] != null)
                    TryRun(result, "root.scale", () => root.transform.localScale = (Vector3)VfxCodec.Decode(rootSpec["scale"], typeof(Vector3), "root.scale")!);
                ApplySystem(rootPs, rootSpec, true, "root", result);
            }

            if (replace)
            {
                foreach (var ps in Emitters(root).Where(p => p != null && p.transform.parent == root.transform))
                    UnityEngine.Object.DestroyImmediate(ps.gameObject);
                result.Log.Add("replace: cleared existing emitters");
            }

            var defaults = spec["defaults"] as JsonObject;
            var items = spec["emitters"] switch
            {
                JsonArray a => a.OfType<JsonObject>().ToList(),
                null => new List<JsonObject>(),
                _ => throw new FormatException("'emitters' must be an array of emitter objects")
            };

            var created = new List<ParticleSystem>();
            foreach (var item in items)
            {
                var name = (string?)item["name"];
                if (string.IsNullOrEmpty(name))
                {
                    result.Errors.Add("emitter without 'name' skipped");
                    continue;
                }
                var existing = Find(root, name!);
                if (IsRemove(item))
                {
                    if (existing != null)
                    {
                        UnityEngine.Object.DestroyImmediate(existing.gameObject);
                        result.Log.Add($"removed '{name}'");
                    }
                    else result.Errors.Add($"remove: emitter '{name}' not found");
                    continue;
                }
                if (existing == null)
                {
                    var go = new GameObject(name);
                    go.transform.SetParent(root.transform, false);
                    existing = go.AddComponent<ParticleSystem>();
                    ConfigureNewEmitter(existing);
                    if (defaults != null) ApplySystem(existing, defaults, false, name!, result);
                    if (item[MaterialKey] == null)
                        existing.GetComponent<ParticleSystemRenderer>().sharedMaterial = VfxMaterials.Resolve(VfxMaterials.DefaultSpec, result.Log);
                    created.Add(existing);
                    result.Log.Add($"created '{name}'");
                }
            }

            foreach (var item in items)
            {
                var name = (string?)item["name"];
                if (string.IsNullOrEmpty(name) || IsRemove(item)) continue;
                var ps = Find(root, name!);
                if (ps == null) continue;
                foreach (var kv in item)
                    if (!EmitterKeys.Contains(kv.Key) && !Modules.ContainsKey(kv.Key))
                        result.Errors.Add($"'{name}': unknown key '{kv.Key}'. Valid: {string.Join(", ", EmitterKeys.Concat(ModuleNames))}");
                ApplyTransform(root, ps, item, name!, result);
                ApplySystem(ps, item, false, name!, result);
            }

            foreach (var item in items)
            {
                var name = (string?)item["name"];
                if (string.IsNullOrEmpty(name) || IsRemove(item)) continue;
                var ps = Find(root, name!);
                if (ps != null && item["subEmitters"] is JsonObject so && so["list"] is JsonArray list) ApplySubEmitters(root, ps, list, name!, result);
            }
        }

        public static ParticleSystem? Find(GameObject root, string name) =>
            Emitters(root).FirstOrDefault(p => p != null && p.name == name);

        static void ApplyTransform(GameObject root, ParticleSystem ps, JsonObject item, string name, ApplyResult result)
        {
            try
            {
                if (item.ContainsKey("parent"))
                {
                    var parentName = (string?)item["parent"];
                    var parent = string.IsNullOrEmpty(parentName) ? root.transform : Find(root, parentName!)?.transform;
                    if (parent == null) result.Errors.Add($"'{name}': parent '{parentName}' not found");
                    else if (parent == ps.transform || parent.IsChildOf(ps.transform)) result.Errors.Add($"'{name}': parent '{parentName}' would create a cycle");
                    else ps.transform.SetParent(parent, false);
                }
                var t = ps.transform;
                if (item["pos"] != null) t.localPosition = (Vector3)VfxCodec.Decode(item["pos"], typeof(Vector3), name + ".pos")!;
                if (item["rot"] != null) t.localEulerAngles = (Vector3)VfxCodec.Decode(item["rot"], typeof(Vector3), name + ".rot")!;
                if (item["scale"] != null) t.localScale = (Vector3)VfxCodec.Decode(item["scale"], typeof(Vector3), name + ".scale")!;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"'{name}': {ex.Message}");
            }
        }

        static void ApplySystem(ParticleSystem ps, JsonObject spec, bool isRoot, string name, ApplyResult result)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            foreach (var kv in Modules)
            {
                if (!spec.ContainsKey(kv.Key)) continue;
                var node = spec[kv.Key];
                var moduleObj = kv.Value.moduleProp.GetValue(ps);
                if (node is JsonValue v && v.TryGetValue<bool>(out var on))
                {
                    if (HasEnabled(kv.Key)) SetModuleEnabled(moduleObj, on);
                    else result.Errors.Add($"'{name}.{kv.Key}' cannot be disabled");
                    continue;
                }
                if (node is not JsonObject mo)
                {
                    result.Errors.Add($"'{name}.{kv.Key}' expects an object or true/false");
                    continue;
                }
                var enable = !(mo["enabled"] is JsonValue ev && ev.TryGetValue<bool>(out var e) && !e);
                if (HasEnabled(kv.Key)) SetModuleEnabled(moduleObj, enable);
                foreach (var prop in mo)
                {
                    var field = $"{name}.{kv.Key}.{prop.Key}";
                    if (prop.Key == "enabled") continue;
                    if (kv.Key == "emission" && prop.Key == "bursts") { TryRun(result, field, () => ApplyBursts(ps, prop.Value, field)); continue; }
                    if (kv.Key == "subEmitters" && prop.Key == "list") continue;
                    var member = kv.Value.members.FirstOrDefault(m => string.Equals(m.Name, prop.Key, StringComparison.Ordinal));
                    if (member == null)
                    {
                        result.Errors.Add($"unknown property '{field}'. Valid: {string.Join(", ", kv.Value.members.Select(m => m.Name))}");
                        continue;
                    }
                    TryRun(result, field, () => DecodeMember(moduleObj, member, prop.Value, field));
                }
            }

            if (renderer == null) return;
            if (spec.ContainsKey(MaterialKey))
                TryRun(result, name + "." + MaterialKey, () => renderer.sharedMaterial = VfxMaterials.Resolve((string?)spec[MaterialKey], result.Log));
            if (spec.ContainsKey(TrailMaterialKey))
                TryRun(result, name + "." + TrailMaterialKey, () => renderer.trailMaterial = VfxMaterials.Resolve((string?)spec[TrailMaterialKey], result.Log));
            if (spec.ContainsKey(MaterialKey) && !spec.ContainsKey("textureSheetAnimation"))
                VfxMaterials.AutoFlipbook(ps, renderer.sharedMaterial, result.Log);

            switch (spec["renderer"])
            {
                case null: break;
                case JsonValue rv when rv.TryGetValue<bool>(out var ren):
                    renderer.enabled = ren;
                    break;
                case JsonObject ro:
                    renderer.enabled = true;
                    foreach (var prop in ro)
                    {
                        var field = $"{name}.renderer.{prop.Key}";
                        var member = RendererMembers.FirstOrDefault(m => m.Name == prop.Key);
                        if (member == null)
                        {
                            result.Errors.Add($"unknown property '{field}'. Valid: {string.Join(", ", RendererMembers.Select(m => m.Name))}, plus emitter-level material/trailMaterial");
                            continue;
                        }
                        TryRun(result, field, () => DecodeMember(renderer, member, prop.Value, field));
                    }
                    break;
                default:
                    result.Errors.Add($"'{name}.renderer' expects an object or true/false");
                    break;
            }
        }

        static void TryRun(ApplyResult result, string field, Action action)
        {
            try { action(); }
            catch (TargetInvocationException ex) { result.Errors.Add($"'{field}': {ex.InnerException?.Message ?? ex.Message}"); }
            catch (Exception ex) { result.Errors.Add($"'{field}': {ex.Message}"); }
        }

        static void ApplyBursts(ParticleSystem ps, JsonNode? node, string field)
        {
            var arr = node as JsonArray ?? throw new FormatException($"'{field}' expects [[time,count],...]");
            var bursts = arr.Select(n =>
            {
                var b = n as JsonArray ?? throw new FormatException($"'{field}' each burst is [time,count] or [time,count,cycles,interval,prob]");
                var burst = new ParticleSystem.Burst(VfxCodec.ToFloat(b[0], field), VfxCodec.DecodeMinMax(b[1], field));
                if (b.Count > 2) burst.cycleCount = (int)VfxCodec.ToFloat(b[2], field);
                if (b.Count > 3) burst.repeatInterval = VfxCodec.ToFloat(b[3], field);
                if (b.Count > 4) burst.probability = VfxCodec.ToFloat(b[4], field);
                return burst;
            }).ToArray();
            var emission = ps.emission;
            emission.SetBursts(bursts);
        }

        static void ApplySubEmitters(GameObject root, ParticleSystem ps, JsonArray list, string name, ApplyResult result)
        {
            var sub = ps.subEmitters;
            while (sub.subEmittersCount > 0) sub.RemoveSubEmitter(0);
            foreach (var n in list)
            {
                if (n is not JsonObject o)
                {
                    result.Errors.Add($"'{name}.subEmitters.list' items must be objects");
                    continue;
                }
                var targetName = (string?)o["emitter"];
                var target = string.IsNullOrEmpty(targetName) ? null : Find(root, targetName!);
                if (target == null)
                {
                    result.Errors.Add($"'{name}.subEmitters': emitter '{targetName}' not found");
                    continue;
                }
                if (target == ps)
                {
                    result.Errors.Add($"'{name}.subEmitters': an emitter cannot be its own sub emitter");
                    continue;
                }
                try
                {
                    var type = (ParticleSystemSubEmitterType)VfxCodec.Decode(o["type"] ?? "Death", typeof(ParticleSystemSubEmitterType), name + ".subEmitters.type")!;
                    var props = ParticleSystemSubEmitterProperties.InheritNothing;
                    var inherit = (string?)o["inherit"];
                    if (!string.IsNullOrEmpty(inherit))
                        foreach (var part in inherit!.Split('|'))
                            props |= (ParticleSystemSubEmitterProperties)VfxCodec.Decode(part.Trim(), typeof(ParticleSystemSubEmitterProperties), name + ".subEmitters.inherit")!;
                    sub.AddSubEmitter(target, type, props);
                    if (o["p"] != null) sub.SetSubEmitterEmitProbability(sub.subEmittersCount - 1, VfxCodec.ToFloat(o["p"], name + ".subEmitters.p"));
                    if (target.transform.parent == root.transform)
                    {
                        target.transform.SetParent(ps.transform, false);
                        result.Log.Add($"'{target.name}' moved under '{name}' (sub emitter)");
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"'{name}.subEmitters': {ex.Message}");
                }
            }
        }

        #endregion
    }
}
