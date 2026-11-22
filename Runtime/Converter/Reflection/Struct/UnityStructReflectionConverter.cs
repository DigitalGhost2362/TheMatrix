#nullable enable

namespace Feeder.MCP.Reflection.Converter
{
    public partial class UnityStructReflectionConverter<T> : UnityGenericNoPropertiesReflectionConverter<T>
    {
        public override bool AllowCascadeSerialization => false;
    }
}
