#nullable enable
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.SceneManagement;

namespace Feeder.MCP.Player
{
    /// <summary>Tools every player offers; they also show how to write your own with <see cref="MatrixRuntimeToolAttribute"/>.</summary>
    static class MatrixRuntimeSampleTools
    {
        [Preserve]
        [MatrixRuntimeTool("runtime-ping", Title = "Runtime / Ping", ReadOnly = true,
            Description = "Answers from the running game (a build or Play mode), not from the Unity Editor. Use it to check that a player is linked.")]
        static string Ping([Description("Text to echo back.")] string? message = null)
            => string.IsNullOrEmpty(message) ? "pong" : message!;

        [Preserve]
        [MatrixRuntimeTool("runtime-scene-info", Title = "Runtime / Scene Info", ReadOnly = true,
            Description = "Reports the running game: active scene, its root objects, frame, time and platform.")]
        static SceneInfo SceneInfoNow()
        {
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var names = new string[roots.Length];
            for (var i = 0; i < roots.Length; i++)
                names[i] = roots[i].name;
            return new SceneInfo
            {
                Scene = scene.name,
                SceneCount = SceneManager.sceneCount,
                RootObjects = names,
                Frame = UnityEngine.Time.frameCount,
                Time = UnityEngine.Time.realtimeSinceStartup,
                Platform = Application.platform.ToString(),
                IsEditor = Application.isEditor,
                ProductName = Application.productName,
            };
        }

        public sealed class SceneInfo
        {
            public string Scene { get; set; } = string.Empty;
            public int SceneCount { get; set; }
            public string[] RootObjects { get; set; } = new string[0];
            public int Frame { get; set; }
            public float Time { get; set; }
            public string Platform { get; set; } = string.Empty;
            public bool IsEditor { get; set; }
            public string ProductName { get; set; } = string.Empty;
        }
    }
}
