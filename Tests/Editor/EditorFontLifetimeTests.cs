#nullable enable

using System.Linq;
using System.Reflection;
using Feeder.MCP.Editor.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace Feeder.MCP.Editor.Tests
{
    public sealed class EditorFontLifetimeTests
    {
        const string TestName = "MatrixBridgeMonoTest";
        const string NoFont = "no monospace system font on this machine";

        [TearDown]
        public void RemoveTestAssets()
        {
            foreach (var asset in Resources.FindObjectsOfTypeAll<FontAsset>().Where(font => font.name == TestName).ToArray())
                Object.DestroyImmediate(asset);
        }

        [Test]
        public void Get_FlagsAssetMaterialAndEveryAtlasPage()
        {
            var asset = BridgeFont.Get(TestName);
            Assume.That(asset != null, NoFont);

            Assert.That(asset!.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
            Assert.That(asset.material.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
            Assert.That(asset.atlasTextures.Select(page => page.hideFlags), Has.All.EqualTo(HideFlags.HideAndDontSave));
        }

        [Test]
        public void Get_ReusesAnIntactAsset()
        {
            var first = BridgeFont.Get(TestName);
            Assume.That(first != null, NoFont);

            Assert.That(BridgeFont.Get(TestName), Is.SameAs(first));
        }

        [Test]
        public void Get_ReplacesAnAssetWhoseMaterialWasDestroyed()
        {
            var stale = BridgeFont.Get(TestName);
            Assume.That(stale != null, NoFont);
            Object.DestroyImmediate(stale!.material);
            Assert.That(BridgeFont.IsIntact(stale), Is.False);

            var fresh = BridgeFont.Get(TestName);

            Assert.That(fresh != null, Is.True);
            Assert.That(fresh, Is.Not.SameAs(stale));
            Assert.That(BridgeFont.IsIntact(fresh), Is.True);
            Assert.That(stale == null, Is.True, "the stale asset must not linger");
            Assert.That(Resources.FindObjectsOfTypeAll<FontAsset>().Count(font => font.name == TestName), Is.EqualTo(1));
        }

        [Test]
        public void Get_ReflagsAnAssetThatOnlyCarriesItsOwnFlags()
        {
            var asset = BridgeFont.Get(TestName);
            Assume.That(asset != null, NoFont);
            asset!.material.hideFlags = HideFlags.None;
            foreach (var page in asset.atlasTextures)
                page.hideFlags = HideFlags.None;

            var reused = BridgeFont.Get(TestName);

            Assert.That(reused, Is.SameAs(asset));
            Assert.That(reused!.material.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
            Assert.That(reused.atlasTextures.Select(page => page.hideFlags), Has.All.EqualTo(HideFlags.HideAndDontSave));
        }

        [Test]
        public void RainRenderer_FlagsTheFontMaterialAndAtlasItBinds()
        {
            var rain = new MatrixRainRenderer();
            try
            {
                Assume.That(rain.EnsureResources(), "rain font or shader unavailable");
                var rebuild = typeof(MatrixRainRenderer).GetMethod("RebuildGlyphCache", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(rebuild, Is.Not.Null, "RebuildGlyphCache was renamed; update this test");

                rebuild!.Invoke(rain, null);

                var fontMaterial = rain.UIFont.material;
                Assert.That(fontMaterial.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
                Assert.That(fontMaterial.mainTexture.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
            }
            finally
            {
                rain.Dispose();
            }
        }
    }
}
