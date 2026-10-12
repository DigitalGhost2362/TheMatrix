using System;
using Feeder.ToolHost.Common.Model;

namespace Feeder.ToolHost
{
[AttributeUsage(AttributeTargets.Method)]
public class AiPromptAttribute : Attribute
{
	private bool _enabled = true;

	private bool _enabledSet;

	public string Name { get; set; } = string.Empty;

	public Role Role { get; set; }

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
}
}
