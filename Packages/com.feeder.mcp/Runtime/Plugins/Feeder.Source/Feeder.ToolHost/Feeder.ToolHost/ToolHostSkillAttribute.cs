using System;

namespace Feeder.ToolHost
{
[Obsolete("Use [AiSkill] instead. This alias will be removed in a future major release.")]
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ToolHostSkillAttribute : AiSkillAttribute
{
	public ToolHostSkillAttribute(string name, string? description = null)
		: base(name, description)
	{
	}
}
}
