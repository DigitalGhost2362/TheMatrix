using System;

namespace Feeder.ToolHost
{
[Obsolete("Use [AiTool] instead. This alias will be removed in a future major release.")]
[AttributeUsage(AttributeTargets.Method)]
public sealed class ToolHostToolAttribute : AiToolAttribute
{
	public ToolHostToolAttribute(string name, string? title = null)
		: base(name, title)
	{
	}
}
}
