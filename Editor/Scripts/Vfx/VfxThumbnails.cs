#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Feeder.MCP.Editor.Vfx
{
    // particle prefabs render as a flat grey square in AssetPreview, so a thumbnail is one contact-sheet frame cached on disk
    sealed class VfxThumbnails : IDisposable
    {
        const int Size = 128;
        const int LabelWidth = 56;
        const int LabelHeight = 20;

        static string Folder => VfxHistory.Root + "/thumbs";

        readonly Dictionary<string, Texture2D?> textures = new Dictionary<string, Texture2D?>();
        readonly Queue<VfxLibrary.Item> pending = new Queue<VfxLibrary.Item>();
        readonly HashSet<string> queued = new HashSet<string>();

        public bool HasPending => pending.Count > 0;

        static string Key(VfxLibrary.Item item) => item.Guid + "-" + item.Hash;

        public static void Prune(IEnumerable<VfxLibrary.Item> items)
        {
            if (!Directory.Exists(Folder)) return;
            var keep = new HashSet<string>(items.Select(Key));
            foreach (var file in Directory.GetFiles(Folder, "*.png"))
                if (!keep.Contains(Path.GetFileNameWithoutExtension(file))) File.Delete(file);
        }

        public Texture2D? Get(VfxLibrary.Item item)
        {
            var key = Key(item);
            if (textures.TryGetValue(key, out var texture)) return texture;
            var file = $"{Folder}/{key}.png";
            if (File.Exists(file)) return textures[key] = Decode(File.ReadAllBytes(file));
            if (queued.Add(key)) pending.Enqueue(item);
            return null;
        }

        public bool Pump()
        {
            if (pending.Count == 0) return false;
            var item = pending.Dequeue();
            queued.Remove(Key(item));
            textures[Key(item)] = Render(item);
            return true;
        }

        public void Forget(VfxLibrary.Item item)
        {
            var key = Key(item);
            if (textures.TryGetValue(key, out var texture) && texture != null) Object.DestroyImmediate(texture);
            textures.Remove(key);
            var file = $"{Folder}/{key}.png";
            if (File.Exists(file)) File.Delete(file);
        }

        public void Dispose()
        {
            foreach (var texture in textures.Values)
                if (texture != null) Object.DestroyImmediate(texture);
            textures.Clear();
            pending.Clear();
            queued.Clear();
        }

        static Texture2D? Render(VfxLibrary.Item item)
        {
            try
            {
                var options = new VfxPreview.Options { Frames = 1, Cols = 1, Cell = Size, Background = VfxUi.Backdrop };
                var texture = Decode(VfxService.Preview(item.Path, options).Png);
                if (texture == null) return null;
                var blank = Enumerable.Repeat(VfxUi.Backdrop, LabelWidth * LabelHeight).ToArray();
                texture.SetPixels(0, texture.height - LabelHeight, LabelWidth, LabelHeight, blank);
                texture.Apply();
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes($"{Folder}/{Key(item)}.png", texture.EncodeToPNG());
                return texture;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Matrix VFX] no thumbnail for {item.Path}: {ex.Message}");
                return null;
            }
        }

        static Texture2D? Decode(byte[]? png)
        {
            if (png == null) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            texture.LoadImage(png);
            return texture;
        }
    }
}
