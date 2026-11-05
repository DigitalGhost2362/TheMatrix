#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_ScriptableObject
    {
        public const string ScriptableObjectCreateToolId = "scriptableobject-create";
        public const string ScriptableObjectGetToolId = "scriptableobject-get";
        public const string ScriptableObjectSetToolId = "scriptableobject-set";

        public sealed class ScriptableObjectCreateResult
        {
            public string Path { get; set; } = string.Empty;
            public string Guid { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public int Failed { get; set; }
            public List<string> Lines { get; set; } = new List<string>();
        }

        public sealed class ScriptableObjectReadResult
        {
            public string Path { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public List<SerializedEntry> Properties { get; set; } = new List<SerializedEntry>();
            public bool Truncated { get; set; }
        }

        public sealed class ScriptableObjectWriteResult
        {
            public string Path { get; set; } = string.Empty;
            public int Changed { get; set; }
            public int Failed { get; set; }
            public List<string> Lines { get; set; } = new List<string>();
        }

        const string ValuesHelp = "Entries 'propertyPath=value'. Numbers, true/false, enum names, 'x,y,z' vectors, '#RRGGBB' colors, asset paths or 'null' for references. " +
            "Resize an array with 'items.Array.size=3' (before its elements), set an element with 'items.Array.data[0]=5' or 'items.Array.data[0].field=5'.";

        /// <summary>Finds a non-abstract ScriptableObject type by full or simple name.</summary>
        public static Type ResolveType(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                throw new ArgumentException("typeName is empty.");
            var wanted = typeName!.Trim();
            var candidates = TypeCache.GetTypesDerivedFrom<ScriptableObject>()
                .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition)
                .ToList();
            var exact = candidates.Where(t => t.FullName == wanted).ToList();
            if (exact.Count == 0)
                exact = candidates.Where(t => string.Equals(t.Name, wanted, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count == 1)
                return exact[0];
            if (exact.Count > 1)
                throw new ArgumentException($"'{wanted}' matches several types: {string.Join(", ", exact.Select(t => t.FullName))}. Give the full name.");
            var near = candidates.Where(t => t.Name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0).Select(t => t.FullName).Take(8).ToList();
            throw new ArgumentException($"No ScriptableObject type '{wanted}'." + (near.Count > 0 ? $" Similar: {string.Join(", ", near)}." : string.Empty));
        }

        static UnityEngine.Object LoadAsset(string? path, bool forWrite)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("assetPath is empty.");
            var clean = path!.Trim().Replace('\\', '/');
            if (forWrite && !clean.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException($"'{clean}' is not under 'Assets/' and cannot be edited.");
            var asset = AssetDatabase.LoadMainAssetAtPath(clean);
            if (asset == null)
                throw new ArgumentException($"No asset at '{clean}'.");
            return asset;
        }

        [AiTool
        (
            ScriptableObjectCreateToolId,
            Title = "ScriptableObject / Create",
            Enabled = false
        )]
        [AiSkillDescription("Create a ScriptableObject asset of a given type at an 'Assets/' path, optionally filling properties, with undo.")]
        [AiSkillBody("Creates a new `.asset` of a ScriptableObject type, creating missing folders, and can fill properties in the same call.\n\n" +
            "## Inputs\n\n" +
            "- `typeName` - full or simple name of a non-abstract ScriptableObject class.\n" +
            "- `assetPath` - `Assets/.../Name.asset`. An existing asset is never overwritten.\n" +
            "- `values` - optional entries `propertyPath=value` (same format as `" + ScriptableObjectSetToolId + "`).\n\n" +
            "The creation is recorded for undo.")]
        [Description("Create a ScriptableObject asset of a type at an 'Assets/...asset' path (folders created, existing asset never overwritten), " +
            "optionally filling properties. Undo recorded. " + ValuesHelp)]
        public ScriptableObjectCreateResult Create
        (
            [Description("Full or simple class name of a ScriptableObject type.")]
            string typeName,
            [Description("Asset path under 'Assets/', ending in '.asset'.")]
            string assetPath,
            [Description("Optional entries 'propertyPath=value' applied after creation.")]
            string[]? values = null
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var type = ResolveType(typeName);
                var path = (assetPath ?? string.Empty).Trim().Replace('\\', '/');
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".asset", StringComparison.Ordinal))
                    throw new ArgumentException($"assetPath must start with 'Assets/' and end with '.asset', got '{assetPath}'.");
                if (File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) != null)
                    throw new ArgumentException($"'{path}' already exists. Use {ScriptableObjectSetToolId} to change it.");

                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }

                var instance = ScriptableObject.CreateInstance(type);
                AssetDatabase.CreateAsset(instance, path);
                Undo.RegisterCreatedObjectUndo(instance, "Matrix create " + type.Name);

                var result = new ScriptableObjectCreateResult
                {
                    Path = path,
                    Guid = AssetDatabase.AssetPathToGUID(path),
                    Type = type.FullName ?? type.Name,
                };
                if (values != null && values.Length > 0)
                {
                    using var so = new SerializedObject(instance);
                    result.Lines = SerializedValues.Apply(so, values, out var failed);
                    result.Failed = failed;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(instance);
                }
                AssetDatabase.SaveAssets();
                return result;
            });
        }

        [AiTool
        (
            ScriptableObjectGetToolId,
            Title = "ScriptableObject / Get",
            ReadOnlyHint = true,
            IdempotentHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Read the serialized properties of a ScriptableObject (or any asset) as property paths, types and values, including private [SerializeField] fields and array sizes.")]
        [Description("Read the serialized properties of an asset as property paths, types and values (private [SerializeField] fields and arrays included). " +
            "Pass propertyPath to read one branch, for example 'items'.")]
        public ScriptableObjectReadResult Get
        (
            [Description("Asset path, for example 'Assets/Data/Items.asset'.")]
            string assetPath,
            [Description("Property path to read (optional; the whole asset when omitted).")]
            string? propertyPath = null,
            [Description("Levels listed below the path, 0 to 6. Default: 2")]
            int? maxDepth = 2,
            [Description("Maximum properties returned. Default: 200")]
            int? limit = SerializedValues.DefaultLimit
        )
        {
            var depth = Math.Max(0, Math.Min(maxDepth ?? 2, Tool_Settings.MaxDepth));
            var max = Math.Max(1, limit ?? SerializedValues.DefaultLimit);
            return MainThread.Instance.Run(() =>
            {
                var asset = LoadAsset(assetPath, false);
                using var so = new SerializedObject(asset);
                var result = new ScriptableObjectReadResult { Path = assetPath.Trim(), Type = asset.GetType().FullName ?? asset.GetType().Name };
                result.Properties = SerializedValues.Dump(so, propertyPath, depth, max, out var truncated);
                result.Truncated = truncated;
                return result;
            });
        }

        [AiTool
        (
            ScriptableObjectSetToolId,
            Title = "ScriptableObject / Set",
            Enabled = false
        )]
        [AiSkillDescription("Set properties of a ScriptableObject asset by SerializedProperty path, including private [SerializeField] fields and array resize ('items.Array.size=3'), with undo.")]
        [AiSkillBody("Edits an asset through SerializedObject: the change is marked dirty, recorded for undo and saved.\n\n" +
            "## Inputs\n\n" +
            "- `assetPath` - `Assets/.../Name.asset`.\n" +
            "- `values` - entries `propertyPath=value`, applied in order. Resize first, then fill: " +
            "`items.Array.size=3`, `items.Array.data[0].name=Sword`, `items.Array.data[0].icon=Assets/Icons/sword.png`.\n\n" +
            "A bad entry is reported and the others still apply. Read the paths with `" + ScriptableObjectGetToolId + "`.")]
        [Description("Set properties of an asset by SerializedProperty path (private [SerializeField] fields too), resize arrays with 'name.Array.size=N'. " +
            "Marked dirty, undo recorded, saved. " + ValuesHelp)]
        public ScriptableObjectWriteResult Set
        (
            [Description("Asset path under 'Assets/', for example 'Assets/Data/Items.asset'.")]
            string assetPath,
            [Description("Entries 'propertyPath=value'.")]
            string[] values
        )
        {
            if (values == null || values.Length == 0)
                throw new ArgumentException("values is empty. Give entries like 'items.Array.size=3'.");
            return MainThread.Instance.Run(() =>
            {
                var asset = LoadAsset(assetPath, true);
                using var so = new SerializedObject(asset);
                var group = SerializedValues.BeginUndo("Matrix scriptableobject set");
                var lines = SerializedValues.Apply(so, values, out var failed);
                so.ApplyModifiedProperties();
                SerializedValues.EndUndo(group);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
                return new ScriptableObjectWriteResult { Path = assetPath.Trim(), Changed = values.Length - failed, Failed = failed, Lines = lines };
            });
        }
    }
}
