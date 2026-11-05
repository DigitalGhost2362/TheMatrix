#nullable enable
using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Rendering
    {
        public const string RenderingFeaturesListToolId = "rendering-features-list";
        public const string RenderingFeaturesSetToolId = "rendering-features-set";

        [AiTool
        (
            RenderingFeaturesListToolId,
            Title = "Rendering / Features List",
            ReadOnlyHint = true,
            IdempotentHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Show the active render pipeline asset, its renderers and each renderer's features (URP Renderer Features) with their active flag.")]
        [Description("Reports the current render pipeline asset (type and path), then each renderer data in its list (default marked) with its renderer features and whether each is active. " +
            "Renderer features are a URP concept; other pipelines only report the asset.")]
        public string FeaturesList()
            => MainThread.Instance.Run(() => DescribePipeline(null));

        [AiTool
        (
            RenderingFeaturesSetToolId,
            Title = "Rendering / Features Set",
            Enabled = false
        )]
        [AiSkillDescription("Turn a URP Renderer Feature on or off by name (use 'rendering-features-list' for the names). The renderer data asset is saved; undoable.")]
        [Description("Sets the active flag of one renderer feature. feature is its name (case-insensitive) as listed by 'rendering-features-list'. " +
            "rendererIndex picks the renderer in the pipeline asset's list (default: the default renderer). Editor-changing.")]
        public string FeaturesSet
        (
            [Description("Renderer feature name, e.g. 'Screen Space Ambient Occlusion'.")]
            string feature,
            [Description("true to enable, false to disable.")]
            bool active,
            [Description("Index in the pipeline asset's renderer list. Default: the default renderer.")]
            int? rendererIndex = null
        )
        {
            if (string.IsNullOrWhiteSpace(feature))
                throw new ArgumentException("feature is required.");

            return MainThread.Instance.Run(() =>
            {
                var rendererData = ResolveRenderer(rendererIndex, out var index);
                var features = new SerializedObject(rendererData).FindProperty("m_RendererFeatures");
                if (features == null || !features.isArray)
                    throw new InvalidOperationException($"'{rendererData.name}' has no renderer features.");

                UnityEngine.Object? hit = null;
                for (var i = 0; i < features.arraySize; i++)
                {
                    var f = features.GetArrayElementAtIndex(i).objectReferenceValue;
                    if (f != null && string.Equals(f.name, feature.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        hit = f;
                        break;
                    }
                }
                if (hit == null)
                    throw new ArgumentException($"No feature '{feature}' on renderer {index}. Features: {string.Join(", ", Enumerable.Range(0, features.arraySize).Select(i => features.GetArrayElementAtIndex(i).objectReferenceValue?.name ?? "(missing)"))}.");

                Undo.IncrementCurrentGroup();
                var group = Undo.GetCurrentGroup();
                var so = new SerializedObject(hit);
                var prop = so.FindProperty("m_Active") ?? throw new InvalidOperationException("The feature has no m_Active field.");
                prop.boolValue = active;
                so.ApplyModifiedProperties();
                Undo.SetCurrentGroupName("Matrix renderer feature set");
                Undo.CollapseUndoOperations(group);
                EditorUtility.SetDirty(hit);
                EditorUtility.SetDirty(rendererData);
                AssetDatabase.SaveAssetIfDirty(rendererData);
                return "updated\n" + DescribePipeline(index);
            });
        }

        static UnityEngine.Object ResolveRenderer(int? rendererIndex, out int index)
        {
            var asset = GraphicsSettings.currentRenderPipeline
                ?? throw new InvalidOperationException("No render pipeline asset is active (built-in pipeline).");
            var so = new SerializedObject(asset);
            var list = so.FindProperty("m_RendererDataList")
                ?? throw new InvalidOperationException($"{asset.GetType().Name} has no renderer list (renderer features are URP only).");
            index = rendererIndex ?? so.FindProperty("m_DefaultRendererIndex")?.intValue ?? 0;
            if (index < 0 || index >= list.arraySize)
                throw new ArgumentException($"rendererIndex {index} is out of range 0..{list.arraySize - 1}.");
            return list.GetArrayElementAtIndex(index).objectReferenceValue
                ?? throw new InvalidOperationException($"Renderer slot {index} is empty.");
        }

        static string DescribePipeline(int? only)
        {
            var sb = new StringBuilder();
            var asset = GraphicsSettings.currentRenderPipeline;
            if (asset == null)
                return "pipeline=Built-in (no render pipeline asset)";
            sb.AppendLine($"pipeline={asset.name} type={asset.GetType().Name} path={AssetDatabase.GetAssetPath(asset)}");
            var so = new SerializedObject(asset);
            var list = so.FindProperty("m_RendererDataList");
            if (list == null)
                return sb.ToString().TrimEnd();
            var def = so.FindProperty("m_DefaultRendererIndex")?.intValue ?? 0;
            for (var i = 0; i < list.arraySize; i++)
            {
                if (only.HasValue && only.Value != i)
                    continue;
                var data = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (data == null)
                {
                    sb.AppendLine($"renderer[{i}] (empty)");
                    continue;
                }
                sb.AppendLine($"renderer[{i}] {data.name} type={data.GetType().Name} default={i == def}");
                var features = new SerializedObject(data).FindProperty("m_RendererFeatures");
                if (features == null)
                    continue;
                for (var j = 0; j < features.arraySize; j++)
                {
                    var f = features.GetArrayElementAtIndex(j).objectReferenceValue;
                    if (f == null)
                    {
                        sb.AppendLine($"  feature[{j}] (missing script)");
                        continue;
                    }
                    var activeProp = new SerializedObject(f).FindProperty("m_Active");
                    sb.AppendLine($"  feature[{j}] {f.name} type={f.GetType().Name} active={(activeProp == null ? "?" : activeProp.boolValue.ToString())}");
                }
            }
            return sb.ToString().TrimEnd();
        }
    }
}
