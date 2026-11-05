#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Runtime.Utils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_UiToolkit
    {
        public const string UiToolkitAttachToolId = "uitoolkit-document-attach";
        const string DefaultPanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";

        [AiTool
        (
            UiToolkitAttachToolId,
            Title = "UI Toolkit / Attach Document",
            Enabled = false
        )]
        [AiSkillDescription("Put a UIDocument on a scene object (created when missing) with a UXML source and a PanelSettings asset (an existing one, or a new one with the default theme). One undo step.")]
        [Description("Adds or updates a UIDocument on the scene object at 'gameObject' (a new root object is created when the path does not exist), " +
            "sets its UXML and PanelSettings, and marks the scene dirty. panelSettingsPath: an existing PanelSettings asset, or a new '.asset' path to create; " +
            "when omitted the first PanelSettings in the project is used, or 'Assets/UI Toolkit/PanelSettings.asset' is created. Editor-changing.")]
        public string AttachDocument
        (
            [Description("Scene path of the object, e.g. 'UI/Hud'. Missing objects are created (the parent must exist).")]
            string gameObject,
            [Description("Project path of the .uxml to show (optional).")]
            string? uxmlPath = null,
            [Description("PanelSettings asset to use or create (optional).")]
            string? panelSettingsPath = null,
            [Description("Sorting order of the document (optional).")]
            float? sortingOrder = null
        )
        {
            if (string.IsNullOrWhiteSpace(gameObject))
                throw new ArgumentException("gameObject is required.");
            var uxml = string.IsNullOrWhiteSpace(uxmlPath) ? null : RequireAssetPath(uxmlPath, ".uxml");
            var psPath = string.IsNullOrWhiteSpace(panelSettingsPath) ? null : RequireAssetPath(panelSettingsPath, ".asset");

            return MainThread.Instance.Run(() =>
            {
                VisualTreeAsset? tree = null;
                if (uxml != null)
                {
                    tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxml)
                        ?? throw new ArgumentException($"uxmlPath: no UXML asset at '{uxml}'.");
                }

                Undo.IncrementCurrentGroup();
                var group = Undo.GetCurrentGroup();

                var settings = ResolvePanelSettings(psPath);
                var path = gameObject.Trim().Trim('/');
                var go = SceneLookup.Find(path);
                var created = false;
                if (go == null)
                {
                    var cut = path.LastIndexOf('/');
                    var name = cut < 0 ? path : path.Substring(cut + 1);
                    GameObject? parent = null;
                    if (cut >= 0)
                        parent = SceneLookup.Find(path.Substring(0, cut))
                            ?? throw new ArgumentException($"gameObject: the parent '{path.Substring(0, cut)}' does not exist.");
                    go = new GameObject(name);
                    Undo.RegisterCreatedObjectUndo(go, "Matrix attach UIDocument");
                    if (parent != null)
                        Undo.SetTransformParent(go.transform, parent.transform, "Matrix attach UIDocument");
                    created = true;
                }

                var doc = go.GetComponent<UIDocument>();
                if (doc == null)
                    doc = Undo.AddComponent<UIDocument>(go);
                Undo.RecordObject(doc, "Matrix attach UIDocument");
                doc.panelSettings = settings;
                if (tree != null)
                    doc.visualTreeAsset = tree;
                if (sortingOrder.HasValue)
                    doc.sortingOrder = sortingOrder.Value;
                EditorUtility.SetDirty(doc);
                EditorSceneManager.MarkSceneDirty(go.scene);
                Undo.SetCurrentGroupName("Matrix attach UIDocument");
                Undo.CollapseUndoOperations(group);

                return $"{(created ? "created" : "updated")} {go.GetPath()} UIDocument uxml={(doc.visualTreeAsset == null ? "(none)" : AssetDatabase.GetAssetPath(doc.visualTreeAsset))} " +
                    $"panelSettings={AssetDatabase.GetAssetPath(settings)} theme={(settings.themeStyleSheet == null ? "(none)" : settings.themeStyleSheet.name)} sortingOrder={doc.sortingOrder}";
            });
        }

        static PanelSettings ResolvePanelSettings(string? path)
        {
            if (path != null)
            {
                var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
                return existing != null ? existing : CreatePanelSettings(path);
            }

            var guid = AssetDatabase.FindAssets("t:PanelSettings").FirstOrDefault();
            if (guid != null)
            {
                var found = AssetDatabase.LoadAssetAtPath<PanelSettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (found != null)
                    return found;
            }
            return CreatePanelSettings(DefaultPanelSettingsPath);
        }

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

        static PanelSettings CreatePanelSettings(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var settings = NewPanelSettings();
            AssetDatabase.CreateAsset(settings, path);
            Undo.RegisterCreatedObjectUndo(settings, "Matrix attach UIDocument");
            return settings;
        }
    }
}
