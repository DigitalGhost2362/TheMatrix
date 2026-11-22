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
    public partial class Tool_Serialized
    {
        public const string SerializedGetToolId = "serialized-get";
        public const string SerializedSetToolId = "serialized-set";

        public sealed class SerializedReadResult
        {
            public string Target { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public List<SerializedEntry> Properties { get; set; } = new List<SerializedEntry>();
            public bool Truncated { get; set; }
        }

        public sealed class SerializedWriteResult
        {
            public string Target { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public bool Created { get; set; }
            public int Changed { get; set; }
            public int Failed { get; set; }
            public List<string> Lines { get; set; } = new List<string>();
        }

        const string TargetHelp = "Asset path ('Assets/Data/Items.asset') or a project settings area (" + Tool_Settings.AreaList + ").";

        const string PathsHelp = "Property paths are SerializedProperty paths, private [SerializeField] fields included (names as in the .asset file, e.g. 'm_Gravity', 'hidden'). " +
            "Arrays: 'items.Array.size' is the length, 'items.Array.data[0]' an element, 'items.Array.data[0].field' a field of an element. Nested: 'stats.speed'. " +
            "Values: numbers, true/false, enum names, 'x,y,z' vectors, '#RRGGBB' colors, asset paths or 'null' for references.";

        /// <summary>Finds a non-abstract ScriptableObject type by full or simple name.</summary>
        public static Type ResolveType(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                throw new ArgumentException("createType is empty.");
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

        static bool IsArea(string? target) => !string.IsNullOrWhiteSpace(target) && Tool_Settings.Areas.ContainsKey(target!.Trim());

        static string CleanPath(string? target)
        {
            if (string.IsNullOrWhiteSpace(target))
                throw new ArgumentException("target is empty.");
            return target!.Trim().Replace('\\', '/');
        }

        [AiTool
        (
            SerializedGetToolId,
            Title = "Serialized / Get",
            ReadOnlyHint = true,
            IdempotentHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Read the serialized properties of an asset (ScriptableObject or any asset) or a project settings area as property paths, types and values, including private [SerializeField] fields and array sizes.")]
        [AiSkillBody("Reads through SerializedObject, so names are the serialized ones.\n\n" +
            "## Inputs\n\n" +
            "- `target` - an asset path (`Assets/Data/Items.asset`) or a project settings area: " + Tool_Settings.AreaList + ".\n" +
            "- `path` - optional property path to read only that branch, e.g. `items` or `m_Gravity`.\n" +
            "- `maxDepth` - levels listed below the path (default 2, at most 6).\n" +
            "- `limit` - maximum properties returned (default 200).\n\n" +
            "## Property paths\n\n" + PathsHelp + " Lists show an array as `[n]`; read `items.Array.size` or `items` with maxDepth to see its elements.\n\n" +
            "Tags and layers have their own tool.")]
        [Description("Read the serialized properties of an asset or a settings area as property paths, types and values. " + TargetHelp + " Pass path to read one branch.")]
        public SerializedReadResult Get
        (
            [Description(TargetHelp)]
            string target,
            [Description("Property path to read (optional; everything when omitted), e.g. 'items' or 'm_Gravity'.")]
            string? path = null,
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
                UnityEngine.Object asset;
                string name;
                if (IsArea(target))
                {
                    asset = Tool_Settings.LoadArea(target);
                    name = target.Trim().ToLowerInvariant();
                }
                else
                {
                    name = CleanPath(target);
                    asset = AssetDatabase.LoadMainAssetAtPath(name) ?? throw new ArgumentException($"No asset at '{name}'.");
                }
                using var so = new SerializedObject(asset);
                var result = new SerializedReadResult { Target = name, Type = asset.GetType().FullName ?? asset.GetType().Name };
                result.Properties = SerializedValues.Dump(so, path, depth, max, out var truncated);
                result.Truncated = truncated;
                return result;
            });
        }

        [AiTool
        (
            SerializedSetToolId,
            Title = "Serialized / Set",
            Enabled = false
        )]
        [AiSkillDescription("Set properties of an asset or a project settings area by SerializedProperty path (private fields and array resize 'items.Array.size=3' included), optionally creating a ScriptableObject asset first. Undo recorded.")]
        [AiSkillBody("Edits through SerializedObject: marked dirty, recorded for undo (one step) and saved. A bad entry is reported and the others still apply.\n\n" +
            "## Inputs\n\n" +
            "- `target` - an asset under `Assets/` or a project settings area: " + Tool_Settings.AreaList + " (tags and layers have their own tool).\n" +
            "- `values` - entries `propertyPath=value`, applied in order. Resize first, then fill: " +
            "`items.Array.size=3`, `items.Array.data[0].name=Sword`, `items.Array.data[0].icon=Assets/Icons/sword.png`. Settings: `m_Gravity=0,-20,0`, `Fixed Timestep=0.01`, `productName=My Game`.\n" +
            "- `createType` - optional full or simple name of a non-abstract ScriptableObject class. When `target` is an asset path that does not exist, the `.asset` is created (missing folders too) and then filled. " +
            "An existing asset of another type is never overwritten or changed; an existing asset of that type is just edited. " +
            "Creating the file is not undoable; Undo only reverts the values set afterwards.\n\n" +
            "## Property paths\n\n" + PathsHelp + "\n\n" +
            "Find the paths with `" + SerializedGetToolId + "`.")]
        [Description("Set properties of an asset or settings area by SerializedProperty path ('propertyPath=value' entries; 'name.Array.size=N' resizes), saved, undo recorded. " +
            "createType creates the ScriptableObject asset when the path does not exist. " + TargetHelp)]
        public SerializedWriteResult Set
        (
            [Description(TargetHelp)]
            string target,
            [Description("Entries 'propertyPath=value', e.g. 'items.Array.size=3'. Optional only when createType creates an empty asset.")]
            string[]? values = null,
            [Description("ScriptableObject type to create when the asset path does not exist (full or simple class name).")]
            string? createType = null
        )
        {
            var hasValues = values != null && values.Length > 0;
            if (!hasValues && string.IsNullOrWhiteSpace(createType))
                throw new ArgumentException("values is empty. Give entries like 'items.Array.size=3'.");
            return MainThread.Instance.Run(() =>
            {
                var result = new SerializedWriteResult();
                UnityEngine.Object asset;
                if (IsArea(target))
                {
                    if (!string.IsNullOrWhiteSpace(createType))
                        throw new ArgumentException("createType only applies to asset paths, not settings areas.");
                    asset = Tool_Settings.LoadArea(target);
                    result.Target = target.Trim().ToLowerInvariant();
                }
                else
                {
                    var path = MxGuard.AssetPath(CleanPath(target));
                    result.Target = path;
                    var existing = AssetDatabase.LoadMainAssetAtPath(path);
                    if (existing != null)
                    {
                        if (!string.IsNullOrWhiteSpace(createType) && existing.GetType() != ResolveType(createType))
                            throw new ArgumentException($"'{path}' already exists as {existing.GetType().FullName}, not {createType!.Trim()}; it is not overwritten.");
                        asset = existing;
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(createType))
                            throw new ArgumentException($"No asset at '{path}'. Pass createType to create a ScriptableObject there.");
                        var type = ResolveType(createType);
                        if (!path.EndsWith(".asset", StringComparison.Ordinal))
                            throw new ArgumentException($"A created asset path must end with '.asset', got '{path}'.");
                        // the file is not undoable: an undone creation left a broken asset on disk
                        asset = ScriptableObject.CreateInstance(type);
                        Mx.CreateAsset(asset, path);
                        result.Created = true;
                    }
                }
                result.Type = asset.GetType().FullName ?? asset.GetType().Name;
                if (hasValues)
                {
                    using var so = new SerializedObject(asset);
                    var group = SerializedValues.BeginUndo("Matrix serialized set");
                    result.Lines = SerializedValues.Apply(so, values, out var failed);
                    so.ApplyModifiedProperties();
                    SerializedValues.EndUndo(group);
                    result.Failed = failed;
                    result.Changed = values!.Length - failed;
                }
                if (IsArea(target))
                    Tool_Settings.Persist(asset);
                else
                {
                    EditorUtility.SetDirty(asset);
                    AssetDatabase.SaveAssets();
                }
                return result;
            });
        }
    }
}
