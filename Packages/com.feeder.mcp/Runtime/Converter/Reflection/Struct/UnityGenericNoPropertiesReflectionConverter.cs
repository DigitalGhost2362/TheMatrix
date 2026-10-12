#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Feeder.Reflect;
using ILogger = Feeder.Logging.ILogger;

namespace Feeder.MCP.Reflection.Converter
{
    public partial class UnityGenericNoPropertiesReflectionConverter<T> : UnityGenericReflectionConverter<T>
    {
        protected override IEnumerable<PropertyInfo>? GetSerializablePropertiesInternal(
            Reflector reflector,
            Type objType,
            BindingFlags flags,
            ILogger? logger = null)
        {
            return null;
        }
    }
}
