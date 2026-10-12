using System;

namespace Feeder.ToolHost
{
[Obsolete("Use [AiSkillDescription] instead. This alias will be removed in a future major release.")]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ToolHostSkillDescriptionAttribute : AiSkillDescriptionAttribute
{
	public ToolHostSkillDescriptionAttribute(string description)
		: base(description)
	{
	}
}
}
