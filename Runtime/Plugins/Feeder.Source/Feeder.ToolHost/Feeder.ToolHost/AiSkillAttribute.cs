using System;

namespace Feeder.ToolHost
{
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class AiSkillAttribute : Attribute
{
	private bool _enabled = true;

	private bool _enabledSet;

	public string Name { get; set; }

	public string? Description { get; set; }

	public string? SkillDescription { get; set; }

	public bool Enabled
	{
		get
		{
			return _enabled;
		}
		set
		{
			_enabled = value;
			_enabledSet = true;
		}
	}

	public bool? EnabledValue
	{
		get
		{
			if (!_enabledSet)
			{
				return null;
			}
			return _enabled;
		}
	}

	public AiSkillAttribute(string name, string? description = null)
	{
		Name = name;
		Description = description;
	}
}
}
