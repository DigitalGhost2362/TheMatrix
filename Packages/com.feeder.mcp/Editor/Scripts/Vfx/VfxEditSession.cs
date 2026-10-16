#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Feeder.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Vfx
{
    // edits land on the in-memory copy; save sends only the changed values through VfxService.Apply
    sealed class VfxEditSession : IDisposable
    {
        public const int OverallSlot = 0;
        public const int SoloSlot = 1;

        public sealed class Node
        {
            public Transform Transform = null!;
            public string Path = "";
            public int Depth;
            public bool HasChildren;
            public ParticleSystem? System;
        }

        readonly Dictionary<Component, string[]> baseline = new Dictionary<Component, string[]>();
        readonly Dictionary<Component, bool[]> changed = new Dictionary<Component, bool[]>();
        readonly Dictionary<ParticleSystem, string> paths = new Dictionary<ParticleSystem, string>();
        GameObject asset;

        public readonly string PrefabPath;
        public readonly string Name;
        public readonly VfxLivePreview Preview;
        public readonly HashSet<string> Hidden = new HashSet<string>();
        public string Selected = "";

        public List<Node> Nodes { get; private set; } = new List<Node>();
        public int Version { get; private set; }
        public int Changes { get; private set; }
        public string? Blocked { get; private set; }

        public Transform Root => Preview.Instance!.transform;

        public ParticleSystem? SelectedSystem => Nodes.FirstOrDefault(n => n.Path == Selected)?.System;

        public VfxEditSession(string prefabPath)
        {
            asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) ?? throw new ArgumentException($"prefab not found '{prefabPath}'");
            PrefabPath = prefabPath;
            Name = asset.name;
            Preview = new VfxLivePreview();
            try
            {
                Rebuild();
            }
            catch
            {
                Preview.Dispose();
                throw;
            }
        }

        public bool IsChanged(Component target, int index) => changed.TryGetValue(target, out var flags) && flags[index];

        public bool IsShown(ParticleSystem ps) => !paths.TryGetValue(ps, out var path) || !Hidden.Contains(path);

        public string PathOf(Node node) => $"{PrefabPath} > {node.Path}";

        public IEnumerable<(Component Target, Param[] Params)> Targets()
        {
            yield return (Root, VfxParams.Root);
            foreach (var node in Nodes)
                if (node.System != null) yield return (node.System, VfxParams.Emitter);
        }

        public void Touch()
        {
            Changes = 0;
            foreach (var (target, list) in Targets())
            {
                var flags = changed[target];
                var before = baseline[target];
                for (var i = 0; i < list.Length; i++)
                {
                    flags[i] = list[i].Key(target) != before[i];
                    if (flags[i]) Changes++;
                }
            }
            Preview.Resimulate();
        }

        public void Revert() => Rebuild();

        public VfxService.ApplyOutcome Save()
        {
            if (Blocked != null) throw new InvalidOperationException(Blocked);
            var outcome = VfxService.Apply(PrefabPath, BuildPatch().ToJsonString(), false, "studio");
            asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Rebuild();
            return outcome;
        }

        public void Dispose() => Preview.Dispose();

        void Rebuild()
        {
            Preview.Rebuild(asset);
            BuildNodes();
            Capture();
            Version = VfxHistory.Latest(PrefabPath);
            if (Selected.Length > 0 && !Nodes.Any(n => n.Path == Selected && n.System != null)) Selected = "";
            Hidden.RemoveWhere(p => !Nodes.Any(n => n.Path == p && n.System != null));
            var duplicates = Nodes.Where(n => n.System != null).GroupBy(n => n.Transform.name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Blocked = duplicates.Count == 0 ? null : $"Tên emitter trùng: {string.Join(", ", duplicates)}. Lưu địa chỉ emitter theo tên, hãy đổi tên trong prefab.";
            Touch();
        }

        void BuildNodes()
        {
            Nodes = new List<Node>();
            paths.Clear();
            Visit(Root, "", 0);
        }

        void Visit(Transform transform, string path, int depth)
        {
            var system = depth == 0 ? null : transform.GetComponent<ParticleSystem>();
            Nodes.Add(new Node { Transform = transform, Path = path, Depth = depth, HasChildren = transform.childCount > 0, System = system });
            if (system != null) paths[system] = path;
            foreach (Transform child in transform) Visit(child, path.Length == 0 ? child.name : path + "/" + child.name, depth + 1);
        }

        void Capture()
        {
            baseline.Clear();
            changed.Clear();
            foreach (var (target, list) in Targets())
            {
                baseline[target] = list.Select(p => p.Key(target)).ToArray();
                changed[target] = new bool[list.Length];
            }
        }

        JsonObject BuildPatch()
        {
            var patch = new JsonObject();
            var root = new JsonObject();
            Place(root, Root, VfxParams.Root);
            if (root.Count > 0) patch["root"] = root;
            var emitters = new JsonArray();
            foreach (var node in Nodes.Where(n => n.System != null))
            {
                var item = new JsonObject { ["name"] = node.Transform.name };
                Place(item, node.System!, VfxParams.Emitter);
                if (item.Count > 1) emitters.Add(item);
            }
            if (emitters.Count > 0) patch["emitters"] = emitters;
            return patch;
        }

        void Place(JsonObject into, Component target, Param[] list)
        {
            for (var i = 0; i < list.Length; i++)
            {
                if (!IsChanged(target, i)) continue;
                var value = list[i].PatchOf(target);
                if (value == null) continue;
                var owner = into;
                if (list[i].Module.Length > 0)
                {
                    if (!(into[list[i].Module] is JsonObject module)) into[list[i].Module] = module = new JsonObject();
                    owner = module;
                }
                owner[list[i].Member] = value;
            }
        }
    }
}
