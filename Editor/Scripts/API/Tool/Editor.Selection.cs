#nullable enable
#if UNITY_6000_5_OR_NEWER
using System.ComponentModel;
using System.Linq;
using Feeder.ToolHost;
using Feeder.MCP.Data;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_Editor_Selection
    {
        public static class Error
        {
            public static string ScriptPathIsEmpty()
                => "Script path is empty. Please provide a valid path. Sample: \"Assets/Scripts/MyScript.cs\".";
        }

        // Shared skill metadata for `editor-selection-set`, identical for both Unity builds.
        internal static class SetSkill
        {
            public const string Description =
                "Set the current Selection in the Unity Editor to the provided objects. " +
                "All `ObjectRef`s must resolve to existing Unity objects; otherwise the call throws. " +
                "Read the current selection first with 'rql-query' (`selection`).";

            public const string Body =
                "Set the current Selection in the Unity Editor to the provided objects. " +
                "Read the current selection first with 'rql-query' (`selection`).\n\n" +
                "## Inputs\n\n" +
                "- `select` — array of `ObjectRef`. Every entry MUST resolve via `FindObject()`; otherwise the tool throws " +
                "before touching `Selection.objects`.\n\n" +
                "## Behavior\n\n" +
                "Assigns the resolved array to `Selection.objects`, then calls " +
                "`UnityEditorInternal.InternalEditorUtility.RepaintAllViews()` so Hierarchy/Inspector reflect the change. " +
                "Returns the post-change `SelectionData` snapshot.";
        }
    }
}
#endif
