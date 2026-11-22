#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Feeder.MCP.Editor.Vfx
{
    // the disk cache is keyed by guid + dependency hash, so a rescan only loads the prefabs that changed
    public sealed class VfxLibrary
    {
        public const string RootFolder = "Assets";
        const int CacheVersion = 1;
        const int ProgressFrom = 20;

        [Serializable]
        internal sealed class Entry
        {
            public string guid = "";
            public string path = "";
            public string hash = "";
            public int systems;
            public int emitters;
        }

        [Serializable]
        sealed class CacheFile
        {
            public int version = CacheVersion;
            public List<Entry> entries = new List<Entry>();
        }

        public sealed class Item
        {
            public string Guid = "";
            public string Path = "";
            public string Hash = "";
            public string Name = "";
            public string Folder = "";
            public int Emitters;
        }

        public sealed class FolderRow
        {
            public string Path = "";
            public int Depth;
            public int Count;
        }

        static readonly List<string> NoFolders = new List<string>();

        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        readonly Dictionary<string, List<string>> children = new Dictionary<string, List<string>>();
        bool loaded;

        public List<Item> Items { get; } = new List<Item>();
        public List<FolderRow> Folders { get; } = new List<FolderRow>();
        public float ScanSeconds { get; private set; }
        public int Inspected { get; private set; }

        static string CachePath => VfxHistory.Root + "/library.json";

        public int CountIn(string folder) => counts.TryGetValue(folder, out var n) ? n : 0;

        public IReadOnlyList<string> SubFolders(string folder) => children.TryGetValue(folder, out var list) ? list : NoFolders;

        public IEnumerable<Item> ItemsIn(string folder) => Items.Where(i => i.Folder == folder);

        public Item? Find(string path) => Items.FirstOrDefault(i => i.Path == path);

        public void Scan()
        {
            var timer = Stopwatch.StartNew();
            Load();
            var seen = new HashSet<string>();
            var stale = new List<Entry>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { RootFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var hash = AssetDatabase.GetAssetDependencyHash(path).ToString();
                seen.Add(guid);
                if (entries.TryGetValue(guid, out var known) && known.hash == hash) known.path = path;
                else stale.Add(new Entry { guid = guid, path = path, hash = hash });
            }
            var removed = entries.Keys.Where(g => !seen.Contains(g)).ToList();
            foreach (var guid in removed) entries.Remove(guid);
            InspectAll(stale);
            Rebuild();
            if (stale.Count > 0 || removed.Count > 0) Save();
            Inspected = stale.Count;
            ScanSeconds = (float)timer.Elapsed.TotalSeconds;
        }

        public void Refresh(string path)
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) return;
            var entry = new Entry { guid = guid, path = path, hash = AssetDatabase.GetAssetDependencyHash(path).ToString() };
            Inspect(entry);
            entries[guid] = entry;
            Rebuild();
            Save();
        }

        void InspectAll(List<Entry> stale)
        {
            try
            {
                for (var i = 0; i < stale.Count; i++)
                {
                    if (stale.Count >= ProgressFrom && i % 10 == 0)
                        EditorUtility.DisplayProgressBar("VFX Studio", $"Quét prefab {i}/{stale.Count}", i / (float)stale.Count);
                    Inspect(stale[i]);
                    entries[stale[i].guid] = stale[i];
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static void Inspect(Entry entry)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.path);
            if (prefab == null)
            {
                Debug.LogWarning($"[Matrix VFX] could not load prefab '{entry.path}', left out of the library");
                return;
            }
            var systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
            var rootSystem = prefab.GetComponent<ParticleSystem>();
            entry.systems = systems.Length;
            entry.emitters = systems.Length - (rootSystem != null && !rootSystem.emission.enabled ? 1 : 0);
        }

        void Load()
        {
            if (loaded) return;
            loaded = true;
            if (!File.Exists(CachePath)) return;
            try
            {
                var file = JsonUtility.FromJson<CacheFile>(File.ReadAllText(CachePath));
                if (file.version != CacheVersion) return;
                foreach (var entry in file.entries) entries[entry.guid] = entry;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Matrix VFX] library cache unreadable, scanning everything: {ex.Message}");
            }
        }

        void Save()
        {
            Directory.CreateDirectory(VfxHistory.Root);
            File.WriteAllText(CachePath, JsonUtility.ToJson(new CacheFile { entries = entries.Values.ToList() }));
        }

        void Rebuild()
        {
            Items.Clear();
            Folders.Clear();
            counts.Clear();
            children.Clear();
            foreach (var entry in entries.Values.Where(e => e.systems > 0))
            {
                var folder = Path.GetDirectoryName(entry.path)!.Replace('\\', '/');
                Items.Add(new Item
                {
                    Guid = entry.guid, Path = entry.path, Hash = entry.hash, Folder = folder,
                    Name = Path.GetFileNameWithoutExtension(entry.path), Emitters = entry.emitters
                });
                for (var f = folder; ; f = f.Substring(0, f.LastIndexOf('/')))
                {
                    counts[f] = CountIn(f) + 1;
                    if (f == RootFolder) break;
                }
            }
            Items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var folder in counts.Keys.Where(f => f != RootFolder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var parent = folder.Substring(0, folder.LastIndexOf('/'));
                if (!children.TryGetValue(parent, out var list)) children[parent] = list = new List<string>();
                list.Add(folder);
            }
            Walk(RootFolder, 0);
        }

        void Walk(string folder, int depth)
        {
            Folders.Add(new FolderRow { Path = folder, Depth = depth, Count = CountIn(folder) });
            foreach (var child in SubFolders(folder)) Walk(child, depth + 1);
        }
    }
}
