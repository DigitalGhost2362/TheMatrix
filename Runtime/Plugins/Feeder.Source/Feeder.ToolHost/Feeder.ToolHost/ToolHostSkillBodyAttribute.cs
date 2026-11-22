using System;

namespace Feeder.ToolHost
{
[Obsolete("Use [AiSkillBody] instead. This alias will be removed in a future major release.")]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ToolHostSkillBodyAttribute : AiSkillBodyAttribute
{
	public ToolHostSkillBodyAttribute(string body)
		: base(body)
	{
	}
}
}
