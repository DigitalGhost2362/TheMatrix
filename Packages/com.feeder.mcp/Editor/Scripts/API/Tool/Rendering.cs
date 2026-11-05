#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Feeder.ToolHost;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_Rendering
    {
    }

    // finds a scene object by hierarchy path in every loaded scene; two matches are an error, never a silent pick
    internal static class SceneLookup
    {
        public static GameObject? Find(string? path)
        {
            var parts = (path ?? string.Empty).Trim().Trim('/').Split('/');
            if (parts.Length == 0 || parts[0].Length == 0)
                return null;
            var hits = new List<GameObject>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name != parts[0])
                        continue;
                    var current = root.transform;
                    foreach (var part in parts.Skip(1))
                    {
                        current = current == null ? null : current.Find(part);
                        if (current == null)
                            break;
                    }
                    if (current != null)
                        hits.Add(current.gameObject);
                }
            }
            if (hits.Count > 1)
                throw new ArgumentException($"'{path}' matches {hits.Count} objects in the loaded scenes ({string.Join(", ", hits.Select(h => h.scene.name))}); use a path that is unique.");
            return hits.Count == 1 ? hits[0] : null;
        }
    }
}
