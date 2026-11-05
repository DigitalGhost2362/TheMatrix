#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Settings
    {
        public const string SettingsGetToolId = "settings-get";
        public const string SettingsSetToolId = "settings-set";
        public const int MaxDepth = 6;

        public sealed class SettingsReadResult
        {
            public string Area { get; set; } = string.Empty;
            public string? Under { get; set; }
            public List<SerializedEntry> Properties { get; set; } = new List<SerializedEntry>();
            public bool Truncated { get; set; }
        }

        public sealed class SettingsWriteResult
        {
            public string Area { get; set; } = string.Empty;
            public int Changed { get; set; }
            public int Failed { get; set; }
            public List<string> Lines { get; set; } = new List<string>();
        }

        [AiTool
        (
            SettingsGetToolId,
            Title = "Settings / Get",
            ReadOnlyHint = true,
            IdempotentHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Read a project settings area (player, quality, physics, physics2d, time, editor, graphics, tagmanager) as property paths with types and values.")]
        [AiSkillBody("Reads ProjectSettings files through SerializedObject, so the names are the serialized ones (for example `m_Gravity`, " +
            "`Fixed Timestep`, `productName`).\n\n" +
            "## Inputs\n\n" +
            "- `area` - " + AreaList + ".\n" +
            "- `path` - optional property path to read only that branch, for example `m_Gravity` or `productName`.\n" +
            "- `maxDepth` - how deep to list below the path (default 2, at most 6).\n" +
            "- `limit` - maximum properties returned (default 200).\n\n" +
            "Values are text: numbers, true/false, enum names, 'x,y,z' vectors, '#RRGGBBAA' colors, asset paths.")]
        [Description("Read a project settings area (" + AreaList + ") as property paths with types and values. " +
            "Pass path to read one property or branch, for example 'm_Gravity'.")]
        public SettingsReadResult Get
        (
            [Description("Settings area: " + AreaList + ".")]
            string area,
            [Description("Property path to read (optional; the whole area when omitted).")]
            string? path = null,
            [Description("Levels listed below the path, 0 to 6. Default: 2")]
            int? maxDepth = 2,
            [Description("Maximum properties returned. Default: 200")]
            int? limit = SerializedValues.DefaultLimit
        )
        {
            var depth = Math.Max(0, Math.Min(maxDepth ?? 2, MaxDepth));
            var max = Math.Max(1, limit ?? SerializedValues.DefaultLimit);
            return MainThread.Instance.Run(() =>
            {
                var settings = LoadArea(area);
                using var so = new SerializedObject(settings);
                var result = new SettingsReadResult { Area = area.Trim().ToLowerInvariant(), Under = string.IsNullOrWhiteSpace(path) ? null : path!.Trim() };
                result.Properties = SerializedValues.Dump(so, path, depth, max, out var truncated);
                result.Truncated = truncated;
                return result;
            });
        }

        [AiTool
        (
            SettingsSetToolId,
            Title = "Settings / Set",
            Enabled = false
        )]
        [AiSkillDescription("Set one or more project settings values (player, quality, physics, physics2d, time, editor, graphics) by property path, with undo.")]
        [AiSkillBody("Writes ProjectSettings values through SerializedObject and saves them. One undo step (Edit > Undo, or 'editor-undo').\n\n" +
            "## Inputs\n\n" +
            "- `area` - " + AreaList + ". Tags and layers have their own tool.\n" +
            "- `values` - entries `propertyPath=value`, for example `m_Gravity=0,-20,0`, `Fixed Timestep=0.01`, `productName=My Game`.\n\n" +
            "Find the paths with `" + SettingsGetToolId + "`. Entries apply in order; a bad entry is reported and the others still apply.")]
        [Description("Set project settings values by property path ('propertyPath=value' entries), saved to disk, one undo step. " +
            "Areas: " + AreaList + ". Find paths with '" + SettingsGetToolId + "'.")]
        public SettingsWriteResult Set
        (
            [Description("Settings area: " + AreaList + ".")]
            string area,
            [Description("Entries 'propertyPath=value', for example 'm_Gravity=0,-20,0'.")]
            string[] values
        )
        {
            if (values == null || values.Length == 0)
                throw new ArgumentException("values is empty. Give entries like 'm_Gravity=0,-20,0'.");
            return MainThread.Instance.Run(() =>
            {
                var settings = LoadArea(area);
                using var so = new SerializedObject(settings);
                var group = SerializedValues.BeginUndo("Matrix settings set");
                var lines = SerializedValues.Apply(so, values, out var failed);
                so.ApplyModifiedProperties();
                SerializedValues.EndUndo(group);
                Persist(settings);
                return new SettingsWriteResult
                {
                    Area = area.Trim().ToLowerInvariant(),
                    Changed = values.Length - failed,
                    Failed = failed,
                    Lines = lines,
                };
            });
        }
    }
}
