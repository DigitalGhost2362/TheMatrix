#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Feeder.MCP.Editor.Vfx
{
    // the one entry point shared by the mcp tools and the VFX Studio window
    public static class VfxService
    {
        public sealed class ApplyOutcome
        {
            public int Version;
            public readonly List<string> Log = new List<string>();
            public readonly List<string> Errors = new List<string>();

            public string Summary(string path)
            {
                var sb = new StringBuilder();
                sb.Append("v").Append(Version).Append(' ').Append(path);
                if (Log.Count > 0) sb.Append(" | ").Append(string.Join("; ", Log));
                if (Errors.Count > 0) sb.AppendLine().Append("ERRORS ").Append(Errors.Count).Append(": ").Append(string.Join("; ", Errors));
                return sb.ToString();
            }
        }

        #region API

        public static ApplyOutcome Apply(string path, string specJson, bool replace, string? note)
        {
            ValidatePath(path);
            var spec = ParseObject(specJson, "spec");
            var outcome = new ApplyOutcome();
            var isNew = AssetDatabase.LoadAssetAtPath<GameObject>(path) == null;
            if (isNew) CreateEmptyPrefab(path);

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!isNew)
                {
                    var before = VfxSpec.Read(root, new List<string>());
                    VfxHistory.Snapshot(path, before, VfxHistory.Latest(path) == 0 ? "original" : "manual edit");
                }
                var result = new VfxSpec.ApplyResult();
                VfxSpec.Apply(root, spec, replace, result);
                outcome.Log.AddRange(result.Log);
                outcome.Errors.AddRange(result.Errors);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                var after = VfxSpec.Read(root, new List<string>());
                outcome.Version = VfxHistory.Snapshot(path, after, string.IsNullOrEmpty(note) ? (isNew ? "created" : "apply") : note!);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return outcome;
        }

        public static string Read(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(LoadPath(path));
            try
            {
                var warnings = new List<string>();
                var spec = VfxSpec.Read(root, warnings);
                var json = spec.ToJsonString();
                return warnings.Count == 0 ? json : json + "\nwarn " + string.Join("; ", warnings);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static string ReadPretty(string path)
        {
            var json = Read(path);
            var cut = json.IndexOf("\nwarn ", StringComparison.Ordinal);
            var body = cut < 0 ? json : json.Substring(0, cut);
            var pretty = JsonNode.Parse(body)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            return cut < 0 ? pretty : pretty + json.Substring(cut);
        }

        public static VfxPreview.Result Preview(string path, VfxPreview.Options options)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LoadPath(path))!;
            return VfxPreview.Run(prefab, options);
        }

        public static ApplyOutcome Restore(string path, int version)
        {
            var spec = VfxHistory.Spec(LoadPath(path), version);
            return Apply(path, spec.ToJsonString(), true, $"restore v{version}");
        }

        public static JsonObject ParseObject(string? json, string field)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException($"'{field}' is empty");
            try
            {
                return JsonNode.Parse(json!, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
                       ?? throw new ArgumentException($"'{field}' must be a json object");
            }
            catch (JsonException ex)
            {
                throw new ArgumentException($"'{field}' is not valid json: {ex.Message}");
            }
        }

        #endregion

        #region Logic

        static void ValidatePath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/") || !path.EndsWith(".prefab"))
                throw new ArgumentException($"path must look like 'Assets/.../Name.prefab', got '{path}'");
        }

        static string LoadPath(string path)
        {
            ValidatePath(path);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) throw new ArgumentException($"prefab not found '{path}'");
            return path;
        }

        static void CreateEmptyPrefab(string path)
        {
            var dir = Path.GetDirectoryName(path)!.Replace('\\', '/');
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
            var go = new GameObject(Path.GetFileNameWithoutExtension(path));
            try
            {
                var ps = go.AddComponent<ParticleSystem>();
                VfxSpec.ConfigureRoot(ps);
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        #endregion
    }
}
