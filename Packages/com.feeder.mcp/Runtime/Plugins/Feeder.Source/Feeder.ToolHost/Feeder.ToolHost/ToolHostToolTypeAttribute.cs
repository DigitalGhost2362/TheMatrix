using System;

namespace Feeder.ToolHost
{
[Obsolete("Use [AiToolType] instead. This alias will be removed in a future major release.")]
[AttributeUsage(AttributeTargets.Class)]
public sealed class ToolHostToolTypeAttribute : AiToolTypeAttribute
{
}
}
