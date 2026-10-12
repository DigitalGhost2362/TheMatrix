#nullable enable
#if !UNITY_6000_5_OR_NEWER
using System;
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.Utils;
using Feeder.MCP.Data;
using Feeder.MCP.Runtime.Extensions;
using Feeder.MCP.Runtime.Utils;
using Feeder.MCP.Utils;
using Feeder.Logging;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_GameObject
    {
        public const string GameObjectDestroyToolId = "gameobject-destroy";
        [AiTool
        (
            GameObjectDestroyToolId,
            Title = "GameObject / Destroy",
            DestructiveHint = true
        )]
        [AiSkillDescription(DestroySkill.Description)]
        [AiSkillBody(DestroySkill.Body)]
        [Description("Destroy GameObject and all nested GameObjects recursively in opened Prefab or in a Scene. " +
            "Use '" + GameObjectFindToolId + "' tool to find the target GameObject first.")]
        public DestroyGameObjectResult Destroy(GameObjectRef gameObjectRef)
        {
            if (gameObjectRef == null)
                throw new ArgumentNullException(nameof(gameObjectRef), "No GameObject reference provided.");

            if (!gameObjectRef.IsValid(out var gameObjectValidationError))
                throw new ArgumentException(gameObjectValidationError, nameof(gameObjectRef));

            return MainThread.Instance.Run(() =>
            {
                var logger = UnityLoggerFactory.LoggerFactory.CreateLogger<Tool_GameObject>();

                var go = gameObjectRef.FindGameObject(out var error);
                if (error != null)
                    throw new Exception(error);

                var destroyedName = go!.name;
                var destroyedPath = go.GetPath();
                var destroyedInstanceId = go.GetInstanceID();

                logger.LogInformation("Destroying GameObject '{Name}' (InstanceID: {InstanceId}) at path '{Path}'",
                    destroyedName, destroyedInstanceId, destroyedPath);

                UnityEngine.Object.DestroyImmediate(go);

                logger.LogInformation("Successfully destroyed GameObject '{Name}' (InstanceID: {InstanceId})",
                    destroyedName, destroyedInstanceId);

                EditorUtils.RepaintAllEditorWindows();

                return new DestroyGameObjectResult
                {
                    DestroyedName = destroyedName,
                    DestroyedPath = destroyedPath,
                    DestroyedInstanceId = destroyedInstanceId
                };
            });
        }

    }
}
#endif
