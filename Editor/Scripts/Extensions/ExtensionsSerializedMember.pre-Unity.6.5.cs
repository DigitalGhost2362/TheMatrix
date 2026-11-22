#nullable enable
#if !UNITY_6000_5_OR_NEWER
using Feeder.Reflect.Model;
using Feeder.MCP.Data;

namespace Feeder.MCP.Editor.Extensions
{
    public static class ExtensionsSerializedMember
    {
        public static bool TryGetInstanceID(this SerializedMember member, out int instanceID)
        {
            var reflector = MatrixPluginEditor.Instance.Reflector;
            if (reflector == null)
            {
                instanceID = 0;
                return false;
            }

            try
            {
                var objectRef = member.GetValue<ObjectRef>(reflector);
                if (objectRef != null)
                {
                    instanceID = objectRef.InstanceID;
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
                    instanceID = fieldValue.GetValue<int>(reflector);
                    return true;
                }
            }
            catch
            {
                // Ignore exceptions, fallback to instanceID field
            }

            instanceID = 0;
            return false;
        }
        public static bool TryGetGameObjectInstanceID(this SerializedMember member, out int instanceID)
        {
            var reflector = MatrixPluginEditor.Instance.Reflector;
            if (reflector == null)
            {
                instanceID = 0;
                return false;
            }

            try
            {
                var objectRef = member.GetValue<GameObjectRef>(reflector);
                if (objectRef != null)
                {
                    instanceID = objectRef.InstanceID;
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
                    instanceID = fieldValue.GetValue<int>(reflector);
                    return true;
                }
            }
            catch
            {
                // Ignore exceptions, fallback to instanceID field
            }

            instanceID = 0;
            return false;
        }
    }
}
#endif
