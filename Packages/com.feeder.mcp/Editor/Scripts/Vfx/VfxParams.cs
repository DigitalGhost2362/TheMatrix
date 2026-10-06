#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using UnityEngine;
using MinMaxCurve = UnityEngine.ParticleSystem.MinMaxCurve;
using MinMaxGradient = UnityEngine.ParticleSystem.MinMaxGradient;

namespace Feeder.MCP.Editor.Vfx
{
    enum ParamKind
    {
        Curve,
        Float,
        Color,
        Gradient,
        Enum,
        Material
    }

    readonly struct ParamGroup
    {
        public ParamGroup(string name, int start, int count)
        {
            Name = name;
            Start = start;
            Count = count;
        }

        public string Name { get; }
        public int Start { get; }
        public int Count { get; }
    }

    // encode tells whether a row changed; patch is what save sends when the spec stores more than the row shows
    sealed class Param
    {
        public string Group = "";
        public string Label = "";
        public string Module = "";
        public string Member = "";
        public string Unit = "";
        public ParamKind Kind;
        public float Min;
        public float Max;
        public float Step = 0.01f;
        public Func<Component, object?> Get = null!;
        public Action<Component, object?> Set = null!;
        public Func<Component, string?>? Locked;
        public Func<Component, JsonNode?> Encode = null!;
        public Func<Component, JsonNode?>? Patch;

        public JsonNode? PatchOf(Component target) => (Patch ?? Encode)(target);

        public string Key(Component target)
        {
            try
            {
                return Encode(target)?.ToJsonString() ?? "";
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Matrix VFX] cannot read '{Label}' on '{target.name}': {ex.Message}");
                return "?";
            }
        }
    }

    static class VfxParams
    {
        public static readonly Param[] Emitter = BuildEmitter();
        public static readonly Param[] Root = BuildRoot();

        public static List<ParamGroup> Groups(Param[] list)
        {
            var groups = new List<ParamGroup>();
            for (var i = 0; i < list.Length; i++)
            {
                var last = groups.Count - 1;
                if (last >= 0 && groups[last].Name == list[i].Group) groups[last] = new ParamGroup(list[i].Group, groups[last].Start, groups[last].Count + 1);
                else groups.Add(new ParamGroup(list[i].Group, i, 1));
            }
            return groups;
        }

        static Param[] BuildEmitter() => new[]
        {
            Curve("Main", "Start Delay", "startDelay", 0f, 2f, 0.01f, "s",
                ps => ps.main.startDelay, (ps, v) => { var m = ps.main; m.startDelay = v; }),
            Curve("Main", "Start Lifetime", "startLifetime", 0.05f, 5f, 0.01f, "s",
                ps => ps.main.startLifetime, (ps, v) => { var m = ps.main; m.startLifetime = v; }),
            Curve("Main", "Start Speed", "startSpeed", 0f, 20f, 0.1f, "",
                ps => ps.main.startSpeed, (ps, v) => { var m = ps.main; m.startSpeed = v; }),
            Curve("Main", "Start Size", "startSize", 0.01f, 10f, 0.01f, "",
                ps => ps.main.startSize, (ps, v) => { var m = ps.main; m.startSize = v; }, ps => ps.main.startSize3D ? "3D" : null),
            StartColor(),
            Curve("Main", "Gravity Modifier", "gravityModifier", -5f, 10f, 0.1f, "",
                ps => ps.main.gravityModifier, (ps, v) => { var m = ps.main; m.gravityModifier = v; }),
            BurstCount(),
            BurstTime(),
            ShapeType(),
            ShapeAngle(),
            SizeEnd(),
            ColorEnd(),
            Opacity(),
            MaterialParam()
        };

        static Param[] BuildRoot() => new[]
        {
            RootFloat("Duration", "duration", 0.2f, 10f, 0.1f, "s", ps => ps.main.duration, (ps, v) => { var m = ps.main; m.duration = v; }),
            RootFloat("Simulation Speed", "simulationSpeed", 0.1f, 3f, 0.05f, "×", ps => ps.main.simulationSpeed, (ps, v) => { var m = ps.main; m.simulationSpeed = v; }),
            new Param
            {
                Group = "Chung", Label = "Scale", Member = "scale", Unit = "×", Min = 0.2f, Max = 3f, Step = 0.05f, Kind = ParamKind.Float,
                Get = c => c.transform.localScale.x,
                Set = (c, v) => c.transform.localScale = Vector3.one * (float)v!,
                Encode = c => VfxCodec.Encode(c.transform.localScale, typeof(Vector3))
            }
        };

        static Param Curve(string group, string label, string member, float min, float max, float step, string unit,
            Func<ParticleSystem, MinMaxCurve> get, Action<ParticleSystem, MinMaxCurve> set, Func<ParticleSystem, string?>? locked = null) =>
            new Param
            {
                Group = group, Label = label, Module = "main", Member = member, Unit = unit, Min = min, Max = max, Step = step, Kind = ParamKind.Curve,
                Get = c => get((ParticleSystem)c),
                Set = (c, v) => set((ParticleSystem)c, (MinMaxCurve)v!),
                Locked = locked == null ? null : c => locked((ParticleSystem)c),
                Encode = c => VfxCodec.EncodeMinMax(get((ParticleSystem)c))
            };

        static Param RootFloat(string label, string member, float min, float max, float step, string unit,
            Func<ParticleSystem, float> get, Action<ParticleSystem, float> set) =>
            new Param
            {
                Group = "Chung", Label = label, Module = "main", Member = member, Unit = unit, Min = min, Max = max, Step = step, Kind = ParamKind.Float,
                Get = c => RootSystem(c) is ParticleSystem ps ? get(ps) : 0f,
                Set = (c, v) =>
                {
                    if (RootSystem(c) is ParticleSystem ps) set(ps, (float)v!);
                },
                Locked = c => RootSystem(c) == null ? "không có PS" : null,
                Encode = c => VfxCodec.NumNode(RootSystem(c) is ParticleSystem ps ? get(ps) : 0f)
            };

        static ParticleSystem? RootSystem(Component c) => c.GetComponent<ParticleSystem>();

        static ParticleSystemRenderer Renderer(Component c) => c.GetComponent<ParticleSystemRenderer>();

        static Param StartColor() => new Param
        {
            Group = "Main", Label = "Start Color", Module = "main", Member = "startColor", Kind = ParamKind.Gradient,
            Get = c => ((ParticleSystem)c).main.startColor,
            Set = (c, v) =>
            {
                var main = ((ParticleSystem)c).main;
                main.startColor = (MinMaxGradient)v!;
            },
            Encode = c => StartColorRgb(((ParticleSystem)c).main.startColor),
            Patch = c => VfxCodec.EncodeMinMaxGradient(((ParticleSystem)c).main.startColor)
        };

        static JsonNode StartColorRgb(MinMaxGradient gradient)
        {
            switch (gradient.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return JsonValue.Create(VfxCodec.EncodeColor(Opaque(gradient.color)))!;
                case ParticleSystemGradientMode.TwoColors:
                    return new JsonArray(VfxCodec.EncodeColor(Opaque(gradient.colorMin)), VfxCodec.EncodeColor(Opaque(gradient.colorMax)));
                default:
                    return VfxCodec.EncodeMinMaxGradient(gradient);
            }
        }

        static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);

        static ParticleSystem.Burst FirstBurst(ParticleSystem ps) => ps.emission.burstCount > 0 ? ps.emission.GetBurst(0) : default;

        static string? BurstLock(ParticleSystem ps) => !ps.emission.enabled ? "tắt" : ps.emission.burstCount == 0 ? "dùng rate" : null;

        static void EditFirstBurst(ParticleSystem ps, Func<ParticleSystem.Burst, ParticleSystem.Burst> edit)
        {
            var emission = ps.emission;
            emission.SetBurst(0, edit(emission.GetBurst(0)));
        }

        static Param BurstCount() => new Param
        {
            Group = "Emission", Label = "Burst Count", Module = "emission", Member = "bursts", Kind = ParamKind.Curve, Max = 200f, Step = 1f,
            Get = c => FirstBurst((ParticleSystem)c).count,
            Set = (c, v) => EditFirstBurst((ParticleSystem)c, b =>
            {
                b.count = (MinMaxCurve)v!;
                return b;
            }),
            Locked = c => BurstLock((ParticleSystem)c),
            Encode = c => VfxCodec.EncodeMinMax(FirstBurst((ParticleSystem)c).count),
            Patch = c => VfxSpec.EncodeBursts((ParticleSystem)c)
        };

        static Param BurstTime() => new Param
        {
            Group = "Emission", Label = "Thời gian phát", Module = "emission", Member = "bursts", Unit = "s", Kind = ParamKind.Float, Max = 2f,
            Get = c => FirstBurst((ParticleSystem)c).time,
            Set = (c, v) => EditFirstBurst((ParticleSystem)c, b =>
            {
                b.time = (float)v!;
                return b;
            }),
            Locked = c => BurstLock((ParticleSystem)c),
            Encode = c => VfxCodec.NumNode(FirstBurst((ParticleSystem)c).time),
            Patch = c => VfxSpec.EncodeBursts((ParticleSystem)c)
        };

        static Param ShapeType() => new Param
        {
            Group = "Shape", Label = "Shape", Module = "shape", Member = "shapeType", Kind = ParamKind.Enum,
            Get = c => ((ParticleSystem)c).shape.shapeType,
            Set = (c, v) =>
            {
                var shape = ((ParticleSystem)c).shape;
                shape.shapeType = (ParticleSystemShapeType)v!;
            },
            Locked = c => ((ParticleSystem)c).shape.enabled ? null : "tắt",
            Encode = c => JsonValue.Create(((ParticleSystem)c).shape.shapeType.ToString())
        };

        static Param ShapeAngle() => new Param
        {
            Group = "Shape", Label = "Angle", Module = "shape", Member = "angle", Unit = "°", Kind = ParamKind.Float, Max = 90f, Step = 1f,
            Get = c => ((ParticleSystem)c).shape.angle,
            Set = (c, v) =>
            {
                var shape = ((ParticleSystem)c).shape;
                shape.angle = (float)v!;
            },
            Locked = c => ((ParticleSystem)c).shape.enabled ? null : "tắt",
            Encode = c => VfxCodec.NumNode(((ParticleSystem)c).shape.angle)
        };

        static Param SizeEnd() => new Param
        {
            Group = "Over Lifetime", Label = "Size cuối đời", Module = "sizeOverLifetime", Member = "size", Unit = "×", Kind = ParamKind.Float, Max = 3f,
            Get = c => SizeEndOf((ParticleSystem)c),
            Set = (c, v) => SetSizeEnd((ParticleSystem)c, (float)v!),
            Locked = c => SizeLock((ParticleSystem)c),
            Encode = c => VfxCodec.EncodeMinMax(((ParticleSystem)c).sizeOverLifetime.size)
        };

        static string? SizeLock(ParticleSystem ps)
        {
            var module = ps.sizeOverLifetime;
            if (!module.enabled) return "tắt";
            if (module.separateAxes) return "xyz";
            var mode = module.size.mode;
            return mode == ParticleSystemCurveMode.Constant || mode == ParticleSystemCurveMode.Curve ? null : "tuỳ chỉnh";
        }

        static float SizeEndOf(ParticleSystem ps)
        {
            var size = ps.sizeOverLifetime.size;
            switch (size.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return size.constant;
                case ParticleSystemCurveMode.Curve:
                {
                    var keys = size.curve.keys;
                    return keys.Length == 0 ? 1f : keys[keys.Length - 1].value * size.curveMultiplier;
                }
                default:
                    return 1f;
            }
        }

        static void SetSizeEnd(ParticleSystem ps, float value)
        {
            var module = ps.sizeOverLifetime;
            var size = module.size;
            switch (size.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    size.constant = value;
                    break;
                case ParticleSystemCurveMode.Curve:
                {
                    var curve = size.curve;
                    var keys = curve.keys;
                    if (keys.Length == 0) return;
                    keys[keys.Length - 1].value = size.curveMultiplier == 0f ? value : value / size.curveMultiplier;
                    curve.keys = keys;
                    size.curve = curve;
                    break;
                }
                default:
                    return;
            }
            module.size = size;
        }

        static Param ColorEnd() => new Param
        {
            Group = "Over Lifetime", Label = "Color cuối đời", Module = "colorOverLifetime", Member = "color", Kind = ParamKind.Color,
            Get = c => ColorEndOf((ParticleSystem)c),
            Set = (c, v) => SetColorEnd((ParticleSystem)c, (Color)v!),
            Locked = c => ColorLock((ParticleSystem)c),
            Encode = c => VfxCodec.EncodeMinMaxGradient(((ParticleSystem)c).colorOverLifetime.color)
        };

        static string? ColorLock(ParticleSystem ps)
        {
            var module = ps.colorOverLifetime;
            if (!module.enabled) return "tắt";
            var mode = module.color.mode;
            return mode == ParticleSystemGradientMode.Color || mode == ParticleSystemGradientMode.Gradient ? null : "tuỳ chỉnh";
        }

        static Color ColorEndOf(ParticleSystem ps)
        {
            var color = ps.colorOverLifetime.color;
            switch (color.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return color.color;
                case ParticleSystemGradientMode.Gradient:
                {
                    var keys = color.gradient.colorKeys;
                    return keys.Length == 0 ? Color.white : keys[keys.Length - 1].color;
                }
                default:
                    return Color.white;
            }
        }

        static void SetColorEnd(ParticleSystem ps, Color value)
        {
            var module = ps.colorOverLifetime;
            var color = module.color;
            switch (color.mode)
            {
                case ParticleSystemGradientMode.Color:
                    color.color = new Color(value.r, value.g, value.b, color.color.a);
                    break;
                case ParticleSystemGradientMode.Gradient:
                {
                    var gradient = color.gradient;
                    var keys = gradient.colorKeys;
                    if (keys.Length == 0) return;
                    keys[keys.Length - 1].color = Opaque(value);
                    gradient.SetKeys(keys, gradient.alphaKeys);
                    color.gradient = gradient;
                    break;
                }
                default:
                    return;
            }
            module.color = color;
        }

        static Param Opacity() => new Param
        {
            Group = "Over Lifetime", Label = "Opacity", Module = "main", Member = "startColor", Kind = ParamKind.Float, Max = 1f,
            Get = c => ((ParticleSystem)c).main.startColor.color.a,
            Set = (c, v) =>
            {
                var main = ((ParticleSystem)c).main;
                var gradient = main.startColor;
                var color = gradient.color;
                color.a = (float)v!;
                gradient.color = color;
                main.startColor = gradient;
            },
            Locked = c => ((ParticleSystem)c).main.startColor.mode == ParticleSystemGradientMode.Color ? null : "tuỳ chỉnh",
            Encode = c => VfxCodec.NumNode(((ParticleSystem)c).main.startColor.color.a),
            Patch = c => VfxCodec.EncodeMinMaxGradient(((ParticleSystem)c).main.startColor)
        };

        static Param MaterialParam() => new Param
        {
            Group = "Renderer", Label = "Material", Member = "material", Kind = ParamKind.Material,
            Get = c => Renderer(c).sharedMaterial,
            Set = (c, v) => Renderer(c).sharedMaterial = (Material?)v,
            Encode = c => JsonValue.Create(VfxMaterials.Describe(Renderer(c).sharedMaterial))
        };
    }
}
