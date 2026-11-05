#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityEditor;
using UnityEditorInternal;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Settings
    {
        public const string SettingsTagsToolId = "settings-tags";
        public const int FirstUserLayer = 8;
        public const int LayerCount = 32;

        public sealed class LayerInfo
        {
            public int Index { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        public sealed class TagLayerResult
        {
            public string Action { get; set; } = string.Empty;
            public string? Message { get; set; }
            public List<string> Tags { get; set; } = new List<string>();
            public List<LayerInfo> Layers { get; set; } = new List<LayerInfo>();
            public List<string> SortingLayers { get; set; } = new List<string>();
        }

        [AiTool
        (
            SettingsTagsToolId,
            Title = "Settings / Tags and Layers",
            Enabled = false
        )]
        [AiSkillDescription("List, add and remove tags, user layers (8-31) and sorting layers of the project, with undo.")]
        [AiSkillBody("Edits `ProjectSettings/TagManager.asset` through SerializedObject (undo recorded, saved to disk).\n\n" +
            "## Actions\n\n" +
            "- `list` - tags, named layers and sorting layers.\n" +
            "- `add-tag` / `remove-tag` - `name` is the tag. Built-in tags cannot be removed. Objects that still use a removed tag keep the text but it no longer resolves.\n" +
            "- `set-layer` - names a user layer. `index` is 8-31; omit it to use the first free one.\n" +
            "- `clear-layer` - empties a user layer by `name` or `index`.\n" +
            "- `add-sorting-layer` / `remove-sorting-layer` - by `name`. 'Default' cannot be removed.\n\n" +
            "Every action returns the resulting lists.")]
        [Description("List, add or remove tags, user layers (8-31) and sorting layers. " +
            "Actions: list, add-tag, remove-tag, set-layer, clear-layer, add-sorting-layer, remove-sorting-layer. " +
            "Undo recorded; saved to ProjectSettings/TagManager.asset.")]
        public TagLayerResult Tags
        (
            [Description("list, add-tag, remove-tag, set-layer, clear-layer, add-sorting-layer or remove-sorting-layer.")]
            string action,
            [Description("Tag, layer or sorting layer name (not needed for list, or for clear-layer with index).")]
            string? name = null,
            [Description("Layer index 8-31 for set-layer or clear-layer (set-layer picks the first free one when omitted).")]
            int? index = null
        )
        {
            var kind = (action ?? string.Empty).Trim().ToLowerInvariant();
            return MainThread.Instance.Run(() =>
            {
                var manager = LoadArea("tagmanager");
                using var so = new SerializedObject(manager);
                var group = SerializedValues.BeginUndo("Matrix " + kind);
                string? message;
                try
                {
                    message = Run(so, kind, name?.Trim(), index);
                }
                finally
                {
                    so.ApplyModifiedProperties();
                    SerializedValues.EndUndo(group);
                }
                if (kind != "list")
                    Persist(manager);
                return Describe(so, kind, message);
            });
        }

        static string? Run(SerializedObject so, string kind, string? name, int? index)
        {
            switch (kind)
            {
                case "list":
                    return null;
                case "add-tag":
                    Require(name, "name");
                    if (InternalEditorUtility.tags.Contains(name!))
                        return $"Tag '{name}' already exists.";
                    var tags = so.FindProperty("tags");
                    tags.InsertArrayElementAtIndex(tags.arraySize);
                    tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = name;
                    return $"Added tag '{name}'.";
                case "remove-tag":
                    Require(name, "name");
                    var list = so.FindProperty("tags");
                    for (var i = 0; i < list.arraySize; i++)
                    {
                        if (list.GetArrayElementAtIndex(i).stringValue != name)
                            continue;
                        list.DeleteArrayElementAtIndex(i);
                        return $"Removed tag '{name}'.";
                    }
                    if (InternalEditorUtility.tags.Contains(name!))
                        throw new ArgumentException($"'{name}' is a built-in tag and cannot be removed.");
                    return $"Tag '{name}' does not exist.";
                case "set-layer":
                    Require(name, "name");
                    var layers = so.FindProperty("layers");
                    for (var i = 0; i < layers.arraySize; i++)
                        if (layers.GetArrayElementAtIndex(i).stringValue == name && i != index)
                            return $"Layer '{name}' already exists at index {i}.";
                    var slot = index ?? FirstFree(layers);
                    CheckLayerIndex(slot);
                    layers.GetArrayElementAtIndex(slot).stringValue = name;
                    return $"Layer {slot} is now '{name}'.";
                case "clear-layer":
                    var all = so.FindProperty("layers");
                    var target = index ?? IndexOfLayer(all, name);
                    CheckLayerIndex(target);
                    var old = all.GetArrayElementAtIndex(target).stringValue;
                    all.GetArrayElementAtIndex(target).stringValue = string.Empty;
                    return $"Cleared layer {target} ('{old}').";
                case "add-sorting-layer":
                    Require(name, "name");
                    var sorting = so.FindProperty("m_SortingLayers");
                    if (FindSorting(sorting, name!) >= 0)
                        return $"Sorting layer '{name}' already exists.";
                    sorting.InsertArrayElementAtIndex(sorting.arraySize);
                    var added = sorting.GetArrayElementAtIndex(sorting.arraySize - 1);
                    added.FindPropertyRelative("name").stringValue = name;
                    added.FindPropertyRelative("uniqueID").longValue = NewSortingId(sorting);
                    added.FindPropertyRelative("locked").boolValue = false;
                    return $"Added sorting layer '{name}'.";
                case "remove-sorting-layer":
                    Require(name, "name");
                    if (name == "Default")
                        throw new ArgumentException("The 'Default' sorting layer cannot be removed.");
                    var existing = so.FindProperty("m_SortingLayers");
                    var at = FindSorting(existing, name!);
                    if (at < 0)
                        return $"Sorting layer '{name}' does not exist.";
                    existing.DeleteArrayElementAtIndex(at);
                    return $"Removed sorting layer '{name}'.";
                default:
                    throw new ArgumentException("action must be list, add-tag, remove-tag, set-layer, clear-layer, add-sorting-layer or remove-sorting-layer.");
            }
        }

        static void Require(string? value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"{parameter} is required for this action.");
        }

        static void CheckLayerIndex(int slot)
        {
            if (slot < FirstUserLayer || slot >= LayerCount)
                throw new ArgumentException($"Layer index must be {FirstUserLayer}-{LayerCount - 1} (0-7 are built in), got {slot}.");
        }

        static int FirstFree(SerializedProperty layers)
        {
            for (var i = FirstUserLayer; i < LayerCount && i < layers.arraySize; i++)
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                    return i;
            throw new InvalidOperationException("All user layers (8-31) are in use.");
        }

        static int IndexOfLayer(SerializedProperty layers, string? name)
        {
            Require(name, "name or index");
            for (var i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == name)
                    return i;
            throw new ArgumentException($"No layer named '{name}'.");
        }

        static int FindSorting(SerializedProperty sorting, string name)
        {
            for (var i = 0; i < sorting.arraySize; i++)
                if (sorting.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == name)
                    return i;
            return -1;
        }

        static long NewSortingId(SerializedProperty sorting)
        {
            var used = new HashSet<long>();
            for (var i = 0; i < sorting.arraySize; i++)
                used.Add(sorting.GetArrayElementAtIndex(i).FindPropertyRelative("uniqueID").longValue);
            var random = new System.Random();
            long id;
            do
            {
                id = (uint)random.Next(1, int.MaxValue);
            } while (used.Contains(id));
            return id;
        }

        static TagLayerResult Describe(SerializedObject so, string kind, string? message)
        {
            so.Update();
            var result = new TagLayerResult { Action = kind, Message = message };
            result.Tags = InternalEditorUtility.tags.ToList();
            var layers = so.FindProperty("layers");
            for (var i = 0; i < layers.arraySize; i++)
            {
                var layerName = layers.GetArrayElementAtIndex(i).stringValue;
                if (!string.IsNullOrEmpty(layerName))
                    result.Layers.Add(new LayerInfo { Index = i, Name = layerName });
            }
            var sorting = so.FindProperty("m_SortingLayers");
            for (var i = 0; i < sorting.arraySize; i++)
                result.SortingLayers.Add(sorting.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue);
            return result;
        }
    }
}
