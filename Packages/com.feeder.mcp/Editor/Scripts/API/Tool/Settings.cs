#nullable enable
using System;
using System.Collections.Generic;
using Feeder.ToolHost;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_Settings
    {
        /// <summary>Settings area name to the ProjectSettings file that holds it.</summary>
        public static readonly IReadOnlyDictionary<string, string> Areas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["player"] = "ProjectSettings/ProjectSettings.asset",
            ["quality"] = "ProjectSettings/QualitySettings.asset",
            ["physics"] = "ProjectSettings/DynamicsManager.asset",
            ["physics2d"] = "ProjectSettings/Physics2DSettings.asset",
            ["time"] = "ProjectSettings/TimeManager.asset",
            ["editor"] = "ProjectSettings/EditorSettings.asset",
            ["graphics"] = "ProjectSettings/GraphicsSettings.asset",
            ["tagmanager"] = "ProjectSettings/TagManager.asset",
        };

        public const string AreaList = "player, quality, physics, physics2d, time, editor, graphics, tagmanager";

        public static UnityEngine.Object LoadArea(string area)
        {
            if (string.IsNullOrWhiteSpace(area) || !Areas.TryGetValue(area.Trim(), out var path))
                throw new ArgumentException($"Unknown settings area '{area}'. Use one of: {AreaList}.");
            var objects = AssetDatabase.LoadAllAssetsAtPath(path);
            if (objects == null || objects.Length == 0 || objects[0] == null)
                throw new InvalidOperationException($"Could not load '{path}'.");
            return objects[0];
        }

        /// <summary>Writes a changed settings object to disk.</summary>
        public static void Persist(UnityEngine.Object settings)
        {
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
    }
}
