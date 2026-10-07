#nullable enable
#if UNITY_6000_5_OR_NEWER
using Feeder.Reflect.Model;
using Feeder.MCP.Data;
using Feeder.Rx;

namespace Feeder.MCP.Editor.Extensions
{
    public static class ExtensionsSerializedMember
    {
        public static bool TryGetInstanceID(this SerializedMember member, out UnityEngine.EntityId entityId)
        {
            var reflector = MatrixPluginEditor.Instance.Reflector;
            if (reflector == null)
            {
                entityId = UnityEngine.EntityId.None;
                return false;
            }

            try
            {
                var objectRef = member.GetValue<ObjectRef>(reflector);
                if (objectRef != null)
                {
                    entityId = objectRef.InstanceID;
                    return true;
                }
            }
            catch
            {
                // Ignore exceptions, fallback to instanceID field
            }

            try
            {
                var fieldValue = member.GetField(ObjectRef.ObjectRefProperty.InstanceID);
                if (fieldValue != null)
                {
                    entityId = fieldValue.GetValue<UnityEngine.EntityId>(reflector);
                    return true;
                }
            }
            catch
            {
                // Ignore exceptions, fallback to instanceID field
            }

            entityId = UnityEngine.EntityId.None;
            return false;
        }
        public static bool TryGetGameObjectInstanceID(this SerializedMember member, out UnityEngine.EntityId entityId)
        {
            var reflector = MatrixPluginEditor.Instance.Reflector;
            if (reflector == null)
            {
                entityId = UnityEngine.EntityId.None;
                return false;
            }

            try
            {
                var objectRef = member.GetValue<GameObjectRef>(reflector);
                if (objectRef != null)
                {
                    entityId = objectRef.InstanceID;
                    return true;
                }
            }
            catch
            {
                // Ignore exceptions, fallback to instanceID field
            }

            try
            {
                var fieldValue = member.GetField(ObjectRef.ObjectRefProperty.InstanceID);
                if (fieldValue != null)
                {
                    entityId = fieldValue.GetValue<UnityEngine.EntityId>(reflector);
                    return true;
                }
            }
            catch
            {
                // Ignore exceptions, fallback to instanceID field
            }

            entityId = UnityEngine.EntityId.None;
            return false;
        }
    }
}
#endif
