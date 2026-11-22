#nullable enable
#if !UNITY_6000_5_OR_NEWER
using System.Collections.Generic;
using System.ComponentModel;
using Feeder.Json.Serialization;

namespace Feeder.MCP.Data
{
    [Description("Scene reference. " +
        "Used to find a Scene.")]
    public class SceneRef : ObjectRef
    {
        public static partial class SceneRefProperty
        {
            public const string Path = "path";
            public const string BuildIndex = "buildIndex";

            public static IEnumerable<string> All => new[] { Path, BuildIndex };
        }

        [JsonInclude, JsonPropertyName(SceneRefProperty.Path)]
        [Description("Path to the Scene within the project. Starts with 'Assets/'")]
        public string Path { get; set; } = string.Empty;

        [JsonInclude, JsonPropertyName(SceneRefProperty.BuildIndex)]
        [Description("Build index of the Scene in the Build Settings.")]
        public int BuildIndex { get; set; } = -1;

        public SceneRef() { }
        public SceneRef(int instanceID)
        {
            this.InstanceID = instanceID;
        }
        public SceneRef(UnityEngine.SceneManagement.Scene scene)
        {
            this.InstanceID = scene.GetHashCode();
            this.Path = scene.path;
            this.BuildIndex = scene.buildIndex;
        }
    }
}
#endif
