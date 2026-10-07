#nullable enable
#if UNITY_6000_5_OR_NEWER
using System.ComponentModel;
using System.Linq;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Data;
using Feeder.MCP.Runtime.Extensions;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Editor_Selection
    {
        public const string EditorSelectionSetToolId = "editor-selection-set";
        [AiTool
        (
            EditorSelectionSetToolId,
            Title = "Editor / Selection / Set",
            IdempotentHint = true,
            Enabled = false
        )]
        [AiSkillDescription(SetSkill.Description)]
        [AiSkillBody(SetSkill.Body)]
        [Description("Set the current Selection in the Unity Editor to the provided objects. " +
            "Use '" + EditorSelectionGetToolId + "' tool to get the current selection first.")]
        public SelectionData Set(ObjectRef[] select)
        {
            return MainThread.Instance.Run(() =>
            {
                var objects = select.Select(o => o.FindObject()).ToArray();
                if (objects.Any(o => o == null))
                    throw new System.Exception("One or more objects could not be found. Please ensure all provided ObjectRefs are valid.");

                Selection.objects = objects;

                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

                return SelectionData.FromSelection();
            });
        }
    }
}
#endif
