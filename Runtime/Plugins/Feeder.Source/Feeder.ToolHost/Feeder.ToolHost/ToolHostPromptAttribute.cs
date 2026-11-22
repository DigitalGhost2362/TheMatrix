using System;

namespace Feeder.ToolHost
{
[Obsolete("Use [AiPrompt] instead. This alias will be removed in a future major release.")]
[AttributeUsage(AttributeTargets.Method)]
public sealed class ToolHostPromptAttribute : AiPromptAttribute
{
}
}
