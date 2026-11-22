#nullable enable

using System.Collections.Generic;

namespace Feeder.MCP.Reflection.Converter
{
    public partial class UnityEngine_Transform_ReflectionConverter : UnityEngine_GenericComponent_ReflectionConverter<UnityEngine.Transform>
    {
        protected override IEnumerable<string> GetIgnoredProperties()
        {
            foreach (var property in base.GetIgnoredProperties())
                yield return property;

            yield return "parentInternal";
        }
    }
}
