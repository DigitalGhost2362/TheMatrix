#nullable enable
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.Utils;
using Feeder.MCP.Data;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Assets
    {
        public const string AssetsMaterialCreateToolId = "assets-material-create";
        [AiTool
        (
            AssetsMaterialCreateToolId,
            Title = "Assets / Create Material",
            Enabled = false
        )]
        [AiSkillDescription("Create a Material asset from a shader name at a new `.mat` path under Assets/.")]
        [AiSkillBody("- `assetPath` - new path under `Assets/` ending in `.mat`; missing folders are created, an existing file is never overwritten.\n" +
            "- `shaderName` - a name `Shader.Find` resolves, e.g. `Universal Render Pipeline/Lit`. List shaders with `rql-query` `assets(\"t:Shader\")`.\n\n" +
            "Creating the file is not undoable.")]
        [Description("Create a Material asset at a new .mat path under Assets/ from a shader name.")]
        public AssetObjectRef CreateMaterial
        (
            [Description("New asset path, e.g. 'Assets/Materials/Red.mat'.")]
            string assetPath,
            [Description("Shader name for Shader.Find, e.g. 'Universal Render Pipeline/Lit'.")]
            string shaderName
        )
        {
            var path = MxGuard.AssetPath(assetPath, ".mat");
            var name = MxGuard.NotEmpty(shaderName, "shaderName", "Universal Render Pipeline/Lit");
            return MainThread.Instance.Run(() =>
            {
                var shader = UnityEngine.Shader.Find(name)
                    ?? throw MxGuard.Fail($"No shader named '{name}'. List shaders with rql-query: assets(\"t:Shader\").");
                var material = new UnityEngine.Material(shader);
                Mx.CreateAsset(material, path);
                EditorUtils.RepaintAllEditorWindows();
                return new AssetObjectRef(material);
            });
        }
    }
}
