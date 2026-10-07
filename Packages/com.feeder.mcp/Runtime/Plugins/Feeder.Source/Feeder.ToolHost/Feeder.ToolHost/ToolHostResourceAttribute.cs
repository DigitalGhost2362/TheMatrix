using System;

namespace Feeder.ToolHost
{
[Obsolete("Use [AiResource] instead. This alias will be removed in a future major release.")]
[AttributeUsage(AttributeTargets.Method)]
public sealed class ToolHostResourceAttribute : AiResourceAttribute
{
}
}
