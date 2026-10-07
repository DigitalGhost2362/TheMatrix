using System;

namespace Feeder.ToolHost
{
[AttributeUsage(AttributeTargets.Method)]
public class AiResourceAttribute : Attribute
{
	private bool _enabled = true;

	private bool _enabledSet;

	public string Route { get; set; } = string.Empty;

	public string? Name { get; set; }

	public string? Description { get; set; }

	public string? MimeType { get; set; }

	public string ListResources { get; set; } = string.Empty;

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
