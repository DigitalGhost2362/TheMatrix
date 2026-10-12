#nullable enable
#if !UNITY_EDITOR && UNITY_6000_5_OR_NEWER
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Feeder.ToolHost.Common;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Feeder.MCP.Runtime.Utils
{
    public static partial class GameObjectUtils
    {
        public static GameObject[]? FindRootGameObjects(Scene? scene = null)
        {
            if (scene == null)
            {
                // Not supported in runtime build
                return null;
            }
            else
            {
                return scene.Value.GetRootGameObjects();
            }
        }
        public static GameObject? FindByInstanceID(EntityId instanceID)
        {
            if (instanceID == EntityId.None)
                return null;

            // Not supported in runtime build
            return null;
        }
    }
}
#endif
