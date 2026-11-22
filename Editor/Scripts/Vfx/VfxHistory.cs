#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Feeder.Json.Nodes;
using UnityEditor;

namespace Feeder.MCP.Editor.Vfx
{
    // one json snapshot per version under Library/MatrixVfx/<prefab guid>/, so rollback and
    // best-of-n selection survive domain reloads without touching version control.
    public static class VfxHistory
    {
        internal const string Root = "Library/MatrixVfx";
        const int MaxEntries = 50;

        public sealed class Entry
        {
            public int Id;
            public string Time = "";
            public string Note = "";
            public float? Score;
        }

        static string Dir(string prefabPath)
        {
            var guid = AssetDatabase.AssetPathToGUID(prefabPath);
            if (string.IsNullOrEmpty(guid)) throw new ArgumentException($"prefab not found '{prefabPath}'");
            return Path.Combine(Root, guid);
        }

        static string FileOf(string dir, int id) => Path.Combine(dir, id.ToString("0000") + ".json");

        public static int Snapshot(string prefabPath, JsonObject spec, string note)
        {
            var dir = Dir(prefabPath);
            Directory.CreateDirectory(dir);
            var ids = Ids(dir);
            var specJson = spec.ToJsonString();
            if (ids.Count > 0)
            {
                var lastSpec = Load(dir, ids[ids.Count - 1])?["spec"]?.ToJsonString();
                if (lastSpec == specJson) return ids[ids.Count - 1];
            }
            var id = ids.Count == 0 ? 1 : ids[ids.Count - 1] + 1;
            var doc = new JsonObject
            {
                ["time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ["note"] = note,
                ["spec"] = JsonNode.Parse(specJson)
            };
            File.WriteAllText(FileOf(dir, id), doc.ToJsonString());
            foreach (var old in ids.Take(Math.Max(0, ids.Count + 1 - MaxEntries)))
                if (!(Load(dir, old)?["score"] is JsonValue)) File.Delete(FileOf(dir, old));
            return id;
        }

        public static List<Entry> List(string prefabPath)
        {
            var dir = Dir(prefabPath);
            if (!Directory.Exists(dir)) return new List<Entry>();
            return Ids(dir).Select(id =>
            {
                var doc = Load(dir, id);
                return new Entry
                {
                    Id = id,
                    Time = (string?)doc?["time"] ?? "",
                    Note = (string?)doc?["note"] ?? "",
                    Score = doc?["score"] is JsonValue s && s.TryGetValue<double>(out var d) ? (float)d : (float?)null
                };
            }).ToList();
        }

        public static JsonObject Spec(string prefabPath, int id)
        {
            var doc = Load(Dir(prefabPath), id) ?? throw new ArgumentException($"history version {id} not found for '{prefabPath}'");
            return (JsonObject)JsonNode.Parse(doc["spec"]!.ToJsonString())!;
        }

        public static void Rate(string prefabPath, int id, float score, string? note)
        {
            var dir = Dir(prefabPath);
            var doc = Load(dir, id) ?? throw new ArgumentException($"history version {id} not found for '{prefabPath}'");
            doc["score"] = score;
            if (!string.IsNullOrEmpty(note)) doc["note"] = note;
            File.WriteAllText(FileOf(dir, id), doc.ToJsonString());
        }

        public static int Latest(string prefabPath)
        {
            var dir = Dir(prefabPath);
            if (!Directory.Exists(dir)) return 0;
            var ids = Ids(dir);
            return ids.Count == 0 ? 0 : ids[ids.Count - 1];
        }

        static List<int> Ids(string dir) =>
            Directory.GetFiles(dir, "*.json")
                .Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out var id) ? id : -1)
                .Where(id => id > 0)
                .OrderBy(id => id)
                .ToList();

        static JsonObject? Load(string dir, int id)
        {
            var file = FileOf(dir, id);
            return File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file)) as JsonObject : null;
        }
    }
}
