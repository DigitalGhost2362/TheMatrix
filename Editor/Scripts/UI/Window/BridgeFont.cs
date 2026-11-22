#nullable enable
using System.Linq;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Feeder.MCP.Editor.UI
{
    internal static class BridgeFont
    {
        internal const string AssetName = "MatrixBridgeMono";

        static readonly string[] _families = { "Consolas", "Cascadia Mono", "Menlo", "DejaVu Sans Mono", "Courier New" };

        internal static void Apply(VisualElement root, string? className = null)
        {
            var asset = Get();
            if (asset == null)
                return;

            var definition = new StyleFontDefinition(FontDefinition.FromSDFFont(asset));
            var query = className == null
                ? root.Query<UnityEngine.UIElements.TextElement>()
                : root.Query<UnityEngine.UIElements.TextElement>(className: className);
            foreach (var text in query.ToList())
                text.style.unityFontDefinition = definition;
        }

        internal static FontAsset? Get(string name = AssetName)
        {
            foreach (var found in Resources.FindObjectsOfTypeAll<FontAsset>().Where(font => font.name == name))
            {
                if (IsIntact(found))
                    return Protect(found);
                Object.DestroyImmediate(found);
            }

            foreach (var family in _families)
            {
                var asset = FontAsset.CreateFontAsset(family, "Regular");
                if (asset == null)
                    continue;
                asset.name = name;
                return Protect(asset);
            }

            return null;
        }

        internal static bool IsIntact(FontAsset? asset) =>
            asset != null && asset.material != null && asset.atlasTextures != null && asset.atlasTextures.All(page => page != null);

        // play mode destroys unflagged edit-time objects and the asset's own flags do not reach its material or atlas pages
        internal static FontAsset Protect(FontAsset asset)
        {
            asset.hideFlags = HideFlags.HideAndDontSave;
            asset.material.hideFlags = HideFlags.HideAndDontSave;
            foreach (var page in asset.atlasTextures)
                page.hideFlags = HideFlags.HideAndDontSave;
            return asset;
        }
    }
}
