#nullable enable
using System;
using System.IO;
using System.Linq;
using Feeder.ToolHost;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_UiToolkit
    {
        internal static PanelSettings NewPanelSettings()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var theme = AssetDatabase.FindAssets("t:ThemeStyleSheet")
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderByDescending(p => p.IndexOf("DefaultRuntimeTheme", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>)
                .FirstOrDefault(t => t != null);
            if (theme != null)
                settings.themeStyleSheet = theme;
            return settings;
        }
    }
}
