#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Physics
    {
        public sealed class PhysicsHit
        {
            public string Name { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public int InstanceId { get; set; }
            public string Layer { get; set; } = string.Empty;
            public string Tag { get; set; } = string.Empty;
            public bool IsTrigger { get; set; }
            public float? Distance { get; set; }
            public float[]? Point { get; set; }
            public float[]? Normal { get; set; }
        }

        public sealed class PhysicsQueryResult
        {
            public string Shape { get; set; } = string.Empty;
            public int Count { get; set; }
            public bool Truncated { get; set; }
            public List<PhysicsHit> Hits { get; set; } = new List<PhysicsHit>();
        }

        [AiTool
        (
            PhysicsQueryToolId,
            Title = "Physics / Query",
            ReadOnlyHint = true,
            IdempotentHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Raycast, spherecast, boxcast and sphere or box overlap against the colliders of the open scenes, in Edit mode, with no Play needed.")]
        [AiSkillBody("Physics queries against the colliders of the open scenes, without entering Play mode. Nothing is changed.\n\n" +
            "## Shapes\n\n" +
            "- `raycast` - first hit along `direction` from `origin`.\n" +
            "- `raycast-all` - every hit along the ray, nearest first.\n" +
            "- `spherecast` - a sphere of `radius` swept along `direction`.\n" +
            "- `boxcast` - a box of `size` (full extents) swept along `direction`, turned by `rotation` (euler degrees).\n" +
            "- `overlap-sphere` - colliders touching a sphere at `origin` with `radius`.\n" +
            "- `overlap-box` - colliders touching a box at `origin` with `size` and `rotation`.\n\n" +
            "## Inputs\n\n" +
            "Vectors are `[x, y, z]` in world space. `maxDistance` defaults to 1000. `layers` is a list of layer names (default: all). " +
            "`includeTriggers` (default false) also hits trigger colliders. `limit` caps the hits (default 50).\n\n" +
            "Colliders whose object is inactive, or whose scene is not loaded, are not hit. Meshes need a MeshCollider to be hit.")]
        [Description("Physics query in Edit mode: shape raycast, raycast-all, spherecast, boxcast, overlap-sphere or overlap-box. " +
            "Vectors are [x,y,z] in world space. Returns the hit objects with path, layer, tag, distance, point and normal. Changes nothing.")]
        public PhysicsQueryResult Query
        (
            [Description("raycast, raycast-all, spherecast, boxcast, overlap-sphere or overlap-box.")]
            string shape,
            [Description("World position [x,y,z]: ray start, sweep start or overlap center.")]
            float[] origin,
            [Description("Direction [x,y,z] for raycast, raycast-all, spherecast and boxcast.")]
            float[]? direction = null,
            [Description("Maximum distance for casts. Default: 1000")]
            float? maxDistance = 1000f,
            [Description("Radius for spherecast and overlap-sphere. Default: 0.5")]
            float? radius = 0.5f,
            [Description("Full box size [x,y,z] for boxcast and overlap-box. Default: [1,1,1]")]
            float[]? size = null,
            [Description("Box rotation as euler degrees [x,y,z]. Default: no rotation")]
            float[]? rotation = null,
            [Description("Layer names to test against (default: all layers).")]
            string[]? layers = null,
            [Description("Also hit trigger colliders. Default: false")]
            bool? includeTriggers = false,
            [Description("Maximum hits returned. Default: 50")]
            int? limit = 50
        )
        {
            var kind = (shape ?? string.Empty).Trim().ToLowerInvariant();
            var start = ToVector(origin, nameof(origin));
            var distance = maxDistance ?? 1000f;
            var rays = Math.Max(1, limit ?? 50);
            var trigger = (includeTriggers ?? false) ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;

            return MainThread.Instance.Run(() =>
            {
                var mask = MaskOf(layers);
                Physics.SyncTransforms();
                var result = new PhysicsQueryResult { Shape = kind };
                Vector3 Aim()
                {
                    var d = ToVector(direction, nameof(direction));
                    if (d.sqrMagnitude < 1e-12f)
                        throw new ArgumentException("direction cannot be zero.");
                    return d.normalized;
                }
                Vector3 Half() => ToVector(size, nameof(size), Vector3.one) * 0.5f;
                Quaternion Turn() => Quaternion.Euler(ToVector(rotation, nameof(rotation), Vector3.zero));

                switch (kind)
                {
                    case "raycast":
                        if (Physics.Raycast(start, Aim(), out var first, distance, mask, trigger))
                            result.Hits.Add(FromHit(first));
                        break;
                    case "raycast-all":
                        result.Hits.AddRange(Sorted(Physics.RaycastAll(start, Aim(), distance, mask, trigger)));
                        break;
                    case "spherecast":
                        result.Hits.AddRange(Sorted(Physics.SphereCastAll(start, radius ?? 0.5f, Aim(), distance, mask, trigger)));
                        break;
                    case "boxcast":
                        result.Hits.AddRange(Sorted(Physics.BoxCastAll(start, Half(), Aim(), Turn(), distance, mask, trigger)));
                        break;
                    case "overlap-sphere":
                        result.Hits.AddRange(Physics.OverlapSphere(start, radius ?? 0.5f, mask, trigger).Select(FromCollider));
                        break;
                    case "overlap-box":
                        result.Hits.AddRange(Physics.OverlapBox(start, Half(), Turn(), mask, trigger).Select(FromCollider));
                        break;
                    default:
                        throw new ArgumentException("shape must be raycast, raycast-all, spherecast, boxcast, overlap-sphere or overlap-box.");
                }

                result.Count = result.Hits.Count;
                if (result.Hits.Count > rays)
                {
                    result.Hits.RemoveRange(rays, result.Hits.Count - rays);
                    result.Truncated = true;
                }
                return result;
            });
        }

        static IEnumerable<PhysicsHit> Sorted(RaycastHit[] hits)
            => hits.OrderBy(h => h.distance).Select(FromHit);

        static PhysicsHit FromHit(RaycastHit hit)
        {
            var info = FromCollider(hit.collider);
            info.Distance = hit.distance;
            info.Point = new[] { hit.point.x, hit.point.y, hit.point.z };
            info.Normal = new[] { hit.normal.x, hit.normal.y, hit.normal.z };
            return info;
        }

        static PhysicsHit FromCollider(Collider collider)
        {
            var go = collider.gameObject;
            return new PhysicsHit
            {
                Name = go.name,
                Path = PathOf(go.transform),
                InstanceId = go.GetInstanceID(),
                Layer = LayerMask.LayerToName(go.layer),
                Tag = go.tag,
                IsTrigger = collider.isTrigger,
            };
        }
    }
}
