#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Feeder.Reflect;
using Feeder.Reflect.Converter;
using UnityEngine;
using ILogger = Feeder.Logging.ILogger;

namespace Feeder.MCP.Reflection.Converter
{
    public partial class UnityArrayReflectionConverter : ArrayReflectionConverter
    {
        protected override IEnumerable<FieldInfo>? GetSerializableFieldsInternal(Reflector reflector, Type objType, BindingFlags flags, ILogger? logger = null)
        {
            return objType.GetFields(flags)
                .Where(field => field.GetCustomAttribute<ObsoleteAttribute>() == null)
                .Where(field => field.GetCustomAttribute<NonSerializedAttribute>() == null)
                .Where(field => field.IsPublic || field.IsPrivate && field.GetCustomAttribute<SerializeField>() != null);
        }
    }
}
