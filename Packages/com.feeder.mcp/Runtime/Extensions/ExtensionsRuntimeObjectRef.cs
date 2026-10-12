#nullable enable
#if UNITY_6000_5_OR_NEWER
using Feeder.MCP.Data;

namespace Feeder.MCP.Runtime.Extensions
{
    public static class ExtensionsRuntimeObjectRef
    {
        public static UnityEngine.Object? FindObject(this ObjectRef? objectRef)
        {
            if (objectRef == null)
                return null;

#if UNITY_EDITOR
            if (objectRef.InstanceID != UnityEngine.EntityId.None)
            {
                return UnityEditor.EditorUtility.EntityIdToObject(objectRef.InstanceID);
            }
#endif
            return null;
        }
        public static ObjectRef? ToObjectRef(this UnityEngine.Object? obj)
        {
            return new ObjectRef(obj);
        }
    }
}
#endif
