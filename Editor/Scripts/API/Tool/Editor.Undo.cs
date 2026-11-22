#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Editor
    {
        public const string EditorUndoToolId = "editor-undo";
        public const int MaxUndoSteps = 50;

        public sealed class UndoResult
        {
            public string Action { get; set; } = string.Empty;
            public int Requested { get; set; }
            public int Performed { get; set; }
            public List<string> Groups { get; set; } = new List<string>();
            public string NextUndo { get; set; } = string.Empty;
            public string NextRedo { get; set; } = string.Empty;
            public string? Note { get; set; }
        }

        static readonly System.Reflection.MethodInfo? GetRecords = typeof(UnityEditor.Undo).GetMethod("GetRecords",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            null, new[] { typeof(List<string>), typeof(List<string>) }, null);

        // Unity exposes the redo stack only internally; the first record is the group the next redo restores
        internal static string NextRedoName()
        {
            if (GetRecords == null)
                return string.Empty;
            var redo = new List<string>();
            GetRecords.Invoke(null, new object[] { new List<string>(), redo });
            return redo.Count > 0 ? redo[0] : string.Empty;
        }

        [AiTool
        (
            EditorUndoToolId,
            Title = "Editor / Undo",
            Enabled = false
        )]
        [AiSkillDescription("Undo or redo the last Editor undo groups (like Edit > Undo / Redo) and name each group. " +
            "Pairs with rql-mutate and scene edits that record Undo.")]
        [AiSkillBody("## Inputs\n\n" +
            "- `action` - `undo` or `redo`.\n" +
            "- `steps` (default 1, max 50) - how many groups.\n\n" +
            "## Limits\n\n" +
            "Only changes in the undo stack are reverted. Files already written to disk (saved scenes, saved assets, " +
            "ProjectSettings) keep their saved content even when the in-memory change is undone; save again after an undo if needed. " +
            "In a shared editor an undo or redo can touch another person's change: check `NextUndo` / `NextRedo` from the previous answer first.")]
        [Description("Undoes or redoes the last Editor undo groups, like Edit > Undo / Redo, and returns the name of each group. " +
            "Only changes recorded in the undo stack are reverted (not disk writes or saved assets). " +
            "Editor-changing: it can undo another person's work, so it follows the editor lock.")]
        public UndoResult UndoRedo
        (
            [Description("'undo' or 'redo'.")]
            string action,
            [Description("How many groups to undo or redo, 1 to 50. Default: 1")]
            int? steps = 1
        )
        {
            var kind = (action ?? string.Empty).Trim().ToLowerInvariant();
            if (kind != "undo" && kind != "redo")
                throw new ArgumentException("action must be 'undo' or 'redo'.");
            var count = steps ?? 1;
            if (count < 1 || count > MaxUndoSteps)
                throw new ArgumentException($"steps must be between 1 and {MaxUndoSteps}.");

            return MainThread.Instance.Run(() =>
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    throw new InvalidOperationException("The Editor is compiling or importing: try again when it is done.");

                var result = new UndoResult { Action = kind, Requested = count };
                for (var i = 0; i < count; i++)
                {
                    string name;
                    if (kind == "undo")
                    {
                        name = UnityEditor.Undo.GetCurrentGroupName();
                        if (string.IsNullOrEmpty(name))
                            break;
                        UnityEditor.Undo.PerformUndo();
                    }
                    else
                    {
                        var before = UnityEditor.Undo.GetCurrentGroup();
                        var beforeName = UnityEditor.Undo.GetCurrentGroupName();
                        UnityEditor.Undo.PerformRedo();
                        if (UnityEditor.Undo.GetCurrentGroup() == before && UnityEditor.Undo.GetCurrentGroupName() == beforeName)
                            break;
                        name = UnityEditor.Undo.GetCurrentGroupName();
                    }
                    result.Groups.Add(name);
                    result.Performed++;
                }
                result.NextUndo = UnityEditor.Undo.GetCurrentGroupName();
                result.NextRedo = NextRedoName();
                if (result.Performed < count)
                    result.Note = $"Nothing more to {kind}.";
                return result;
            });
        }
    }
}
