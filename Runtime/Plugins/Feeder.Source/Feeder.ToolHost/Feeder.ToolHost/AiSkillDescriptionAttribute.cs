using System;

namespace Feeder.ToolHost
{
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public class AiSkillDescriptionAttribute : Attribute
{
	public string Description { get; }

	public AiSkillDescriptionAttribute(string description)
	{
		Description = description ?? string.Empty;
	}
}
}
