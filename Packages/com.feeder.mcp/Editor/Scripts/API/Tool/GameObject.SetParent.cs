#nullable enable
using System.ComponentModel;
using System.Text;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.Utils;
using Feeder.MCP.Data;
using Feeder.MCP.Runtime.Extensions;
using UnityEditor.SceneManagement;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_GameObject
    {
        public const string GameObjectSetParentToolId = "gameobject-set-parent";
        [AiTool
        (
            GameObjectSetParentToolId,
            Title = "GameObject / Set Parent",
            IdempotentHint = true
        )]
        [AiSkillDescription("Reparent a batch of GameObjects under a new parent in the currently opened Prefab " +
            "or active Scene. Per-item failures are reported in the returned status string instead of aborting the batch. " +
            "Use '" + GameObjectFindToolId + "' to locate the GameObjects first.")]
        [AiSkillBody("Set parent GameObject to list of GameObjects in opened Prefab or in a Scene. " +
            "Use '" + GameObjectFindToolId + "' tool to find the target GameObjects first.\n\n" +
            "## Inputs\n\n" +
            "- `gameObjectRefs` — list of children to reparent.\n" +
            "- `parentGameObjectRef` — new parent. Must resolve, otherwise the call returns early with an error string.\n" +
            "- `worldPositionStays` (default `true`) — preserve world-space transform when reparenting (passed to " +
            "`Transform.SetParent`).\n\n" +
            "## Behavior\n\n" +
            "Iterates `gameObjectRefs` and reparents each one independently; per-item resolve errors are appended to " +
            "the returned status string instead of throwing. After the loop, if at least one reparent succeeded, marks " +
            "the active scene dirty and repaints editor windows.")]
        [Description("Set parent GameObject to list of GameObjects in opened Prefab or in a Scene. " +
            "Use '" + GameObjectFindToolId + "' tool to find the target GameObjects first.")]
        public string SetParent
        (
            [Description("List of references to the GameObjects to set new parent.")]
            GameObjectRefList gameObjectRefs,
            [Description("Reference to the parent GameObject.")]
            GameObjectRef parentGameObjectRef,
            [Description("A boolean flag indicating whether the GameObject's world position should remain unchanged when setting its parent.")]
            bool worldPositionStays = true
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var stringBuilder = new StringBuilder();
                int changedCount = 0;

                var parentGo = parentGameObjectRef.FindGameObject(out var error);
                if (error != null)
                {
                    stringBuilder.AppendLine(error);
                    return stringBuilder.ToString();
                }
                if (parentGo == null)
                {
                    stringBuilder.AppendLine($"[Error] GameObject by {nameof(parentGameObjectRef)} not found.");
                    return stringBuilder.ToString();
                }

                for (var i = 0; i < gameObjectRefs.Count; i++)
                {
                    var targetGo = gameObjectRefs[i].FindGameObject(out error);
                    if (error != null)
                    {
                        stringBuilder.AppendLine(error);
                        continue;
                    }
                    if (targetGo == null)
                    {
                        stringBuilder.AppendLine($"[Error] GameObject by {nameof(gameObjectRefs)}[{i}] not found.");
                        continue;
                    }

                    targetGo.transform.SetParent(parentGo.transform, worldPositionStays: worldPositionStays);
                    changedCount++;

                    stringBuilder.AppendLine(@$"[Success] Set parent of {gameObjectRefs[i]} to {parentGameObjectRef}.");
                }

                if (changedCount > 0)
                {
                    EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                    EditorUtils.RepaintAllEditorWindows();
                }

                return stringBuilder.ToString();
            });
        }
    }
}
