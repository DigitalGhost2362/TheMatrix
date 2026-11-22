#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Feeder.ToolHost;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_Physics
    {
        public const string PhysicsQueryToolId = "physics-query";
        public const string PhysicsSimulateToolId = "physics-simulate";

        public static string PathOf(Transform transform)
        {
            var names = new Stack<string>();
            for (var t = transform; t != null; t = t.parent)
                names.Push(t.name);
            return string.Join("/", names);
        }

        public static Vector3 ToVector(float[]? values, string name, Vector3? fallback = null)
        {
            if (values == null || values.Length == 0)
            {
                if (fallback.HasValue)
                    return fallback.Value;
                throw new ArgumentException($"{name} is required: give [x, y, z].");
            }
            if (values.Length != 3)
                throw new ArgumentException($"{name} must have 3 numbers [x, y, z], got {values.Length}.");
            return new Vector3(values[0], values[1], values[2]);
        }

        /// <summary>Builds a layer mask from layer names; null or empty means every layer.</summary>
        public static int MaskOf(string[]? layers)
        {
            if (layers == null || layers.Length == 0)
                return Physics.DefaultRaycastLayers;
            var mask = 0;
            foreach (var layerName in layers)
            {
                var layer = LayerMask.NameToLayer((layerName ?? string.Empty).Trim());
                if (layer < 0)
                    throw new ArgumentException($"Unknown layer '{layerName}'.");
                mask |= 1 << layer;
            }
            return mask;
        }
    }
}
