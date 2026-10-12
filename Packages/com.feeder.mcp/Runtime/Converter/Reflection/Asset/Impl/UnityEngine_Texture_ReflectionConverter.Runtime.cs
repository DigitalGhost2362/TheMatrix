#nullable enable
#if !UNITY_EDITOR
using System;
using System.Reflection;
using System.Text;
using Feeder.Reflect;
using Feeder.Reflect.Model;
using Feeder.Reflect.Utils;
using Feeder.Logging;
using ILogger = Feeder.Logging.ILogger;
using LogLevel = Feeder.Logging.LogLevel;

namespace Feeder.MCP.Reflection.Converter
{
    public partial class UnityEngine_Texture_ReflectionConverter : UnityEngine_Asset_ReflectionConverter<UnityEngine.Texture>
    {
        // public override bool TryModify(
        //     Reflector reflector,
        //     ref object? obj,
        //     SerializedMember data,
        //     Type type,
        //     int depth = 0,
        //     Logs? logs = null,
        //     BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        //     ILogger? logger = null)
        // {
        //     var padding = StringUtils.GetPadding(depth);

        //     if (logger?.IsEnabled(LogLevel.Trace) == true)
        //         logger.LogTrace($"{StringUtils.GetPadding(depth)}Modify sprite from data. Converter='{GetType().GetTypeShortName()}'.");

        //     if (logger?.IsEnabled(LogLevel.Error) == true)
        //         logger.LogError($"{padding}Operation is not supported in runtime. Converter: {GetType().GetTypeShortName()}");

        //     if (stringBuilder != null)
        //         stringBuilder.AppendLine($"{padding}[Error] Operation is not supported in runtime. Converter: {GetType().GetTypeShortName()}");

        //     return false;
        // }
    }
}
#endif
