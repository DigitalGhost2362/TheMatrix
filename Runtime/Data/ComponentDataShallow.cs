#nullable enable
#if UNITY_6000_5_OR_NEWER
using Feeder.Reflect;

namespace Feeder.MCP.Data
{
    [System.Serializable]
    public class ComponentDataShallow
    {
        public UnityEngine.EntityId instanceID { get; set; } = UnityEngine.EntityId.None;
        public string typeName { get; set; } = string.Empty;
        public Enabled isEnabled { get; set; }

        public ComponentDataShallow() { }
        public ComponentDataShallow(UnityEngine.Component component)
        {
            instanceID = component.GetEntityId();
            typeName = component.GetType().GetTypeId();
            isEnabled = component switch
            {
                UnityEngine.Behaviour behaviour => behaviour.enabled ? Enabled.True : Enabled.False,
                UnityEngine.Collider collider => collider.enabled ? Enabled.True : Enabled.False,
                UnityEngine.Renderer renderer => renderer.enabled ? Enabled.True : Enabled.False,
                _ => Enabled.NA,
            };
        }

        public enum Enabled
        {
            NA = -1,
            False = 0,
            True = 1
        }
    }
    public static class ComponentDataLightExtension
    {
        public static bool IsEnabled(this ComponentDataShallow componentData)
            => componentData.isEnabled == ComponentDataShallow.Enabled.True;
    }
    public static class ComponentDataLightEnabledExtension
    {
        public static bool ToBool(this ComponentDataShallow.Enabled enabled)
            => enabled == ComponentDataShallow.Enabled.True;
    }
}
#endif
