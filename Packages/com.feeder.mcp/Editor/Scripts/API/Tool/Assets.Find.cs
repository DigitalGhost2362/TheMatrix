#nullable enable
using System.Collections.Generic;
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Data;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Assets
    {
        public const string AssetsFindToolId = "assets-find";
        [AiTool
        (
            AssetsFindToolId,
            Title = "Assets / Find",
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [AiSkillDescription("Search the Unity asset database using a search filter string. " +
            "The filter accepts names, labels (`l:`), types (`t:`), AssetBundles (`b:`), areas (`a:`), and globs (`glob:`). " +
            "See the body for the full filter syntax. For scene GameObjects use '" +
            Tool_GameObject.GameObjectFindToolId + "' instead.")]
        [AiSkillBody("Search the asset database using the search filter string. " +
            "Allows you to search for Assets. The string argument can provide names, labels or types (classnames).\n\n" +
            "## When to use / when not\n\n" +
            "- Use to locate project assets by name, label, type, bundle, area or glob.\n" +
            "- Do NOT use to locate scene GameObjects — use '" + Tool_GameObject.GameObjectFindToolId + "'. " +
            "Once you have an asset, read its data with '" + AssetsGetDataToolId + "'.\n\n" +
            "## Filter syntax\n\n" +
            "- **Name** — filter assets by their filename (without extension). Words separated by whitespace are " +
            "treated as separate name searches.\n" +
            "- **Labels (`l:`)** — assets can have labels attached. Use `l:` before each label.\n" +
            "- **Types (`t:`)** — find assets based on explicitly identified types. Available types include " +
            "AnimationClip, AudioClip, AudioMixer, ComputeShader, Font, GUISkin, Material, Mesh, Model, " +
            "PhysicMaterial, Prefab, Scene, Script, Shader, Sprite, Texture, VideoClip, VisualEffectAsset, " +
            "VisualEffectSubgraph.\n" +
            "- **AssetBundles (`b:`)** — find assets which are part of an Asset bundle.\n" +
            "- **Area (`a:`)** — find assets in a specific area. Valid values: `all`, `assets`, `packages`.\n" +
            "- **Globbing (`glob:`)** — use globbing to match specific rules.\n\n" +
            "Searching is case-insensitive. Use `searchInFolders` to restrict the search scope. " +
            "`maxResults` caps the returned list (default 10) — results beyond it are truncated.")]
        [Description("Search the asset database using the search filter string. " +
            "Allows you to search for Assets. The string argument can provide names, labels or types (classnames).")]
        public List<AssetObjectRef> Find
        (
            // <ref>https://docs.unity3d.com/ScriptReference/AssetDatabase.FindAssets.html</ref>
            [Description("The filter string can contain search data. Could be empty. " +
                "Name: Filter assets by their filename (without extension). Words separated by whitespace are treated as separate name searches. " +
                "Labels (l:): Assets can have labels attached to them. Use 'l:' before each label. " +
                "Types (t:): Find assets based on explicitly identified types. Use 't:' keyword. Available types: AnimationClip, AudioClip, AudioMixer, ComputeShader, Font, GUISkin, Material, Mesh, Model, PhysicMaterial, Prefab, Scene, Script, Shader, Sprite, Texture, VideoClip, VisualEffectAsset, VisualEffectSubgraph. " +
                "AssetBundles (b:): Find assets which are part of an Asset bundle. " +
                "Area (a:): Find assets in a specific area. Valid values are 'all', 'assets', and 'packages'. " +
                "Globbing (glob:): Use globbing to match specific rules. " +
                "Note: Searching is case insensitive.")]
            string? filter = null,
            [Description("The folders where the search will start. If null, the search will be performed in all folders.")]
            string[]? searchInFolders = null,
            [Description("Maximum number of assets to return. If the number of found assets exceeds this limit, the result will be truncated.")]
            int maxResults = 10
        )
        {
            if (maxResults <= 0)
                throw new System.ArgumentException($"{nameof(maxResults)} must be greater than zero.");

            return MainThread.Instance.Run(() =>
            {
                var assetGuids = (searchInFolders?.Length ?? 0) == 0
                    ? AssetDatabase.FindAssets(filter ?? string.Empty)
                    : AssetDatabase.FindAssets(filter ?? string.Empty, searchInFolders);

                var response = new List<AssetObjectRef>();

                for (var i = 0; i < assetGuids.Length && i < maxResults; i++)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(assetGuids[i]);
                    var assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
                    var assetObject = AssetDatabase.LoadAssetAtPath(assetPath, assetType);
                    if (assetObject == null)
                        continue;

                    response.Add(new AssetObjectRef(assetObject));
                }

                return response;
            });
        }
    }
}