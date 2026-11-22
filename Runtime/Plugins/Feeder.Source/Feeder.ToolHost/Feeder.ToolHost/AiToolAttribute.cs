using System;

namespace Feeder.ToolHost
{
[AttributeUsage(AttributeTargets.Method)]
public class AiToolAttribute : Attribute
{
	private bool _readOnlyHint;

	private bool _readOnlyHintSet;

	private bool _destructiveHint;

	private bool _destructiveHintSet;

	private bool _idempotentHint;

	private bool _idempotentHintSet;

	private bool _openWorldHint;

	private bool _openWorldHintSet;

	private bool _enabled = true;

	private bool _enabledSet;

	public string Name { get; set; }

	public string? Title { get; set; }

	public McpToolType ToolType { get; set; }

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

	public bool ReadOnlyHint
	{
		get
		{
			return _readOnlyHint;
		}
		set
		{
			_readOnlyHint = value;
			_readOnlyHintSet = true;
		}
	}

	public bool DestructiveHint
	{
		get
		{
			return _destructiveHint;
		}
		set
		{
			_destructiveHint = value;
			_destructiveHintSet = true;
		}
	}

	public bool IdempotentHint
	{
		get
		{
			return _idempotentHint;
		}
		set
		{
			_idempotentHint = value;
			_idempotentHintSet = true;
		}
	}

	public bool OpenWorldHint
	{
		get
		{
			return _openWorldHint;
		}
		set
		{
			_openWorldHint = value;
			_openWorldHintSet = true;
		}
	}

	public bool? ReadOnlyHintValue
	{
		get
		{
			if (!_readOnlyHintSet)
			{
				return null;
			}
			return _readOnlyHint;
		}
	}

	public bool? DestructiveHintValue
	{
		get
		{
			if (!_destructiveHintSet)
			{
				return null;
			}
			return _destructiveHint;
		}
	}

	public bool? IdempotentHintValue
	{
		get
		{
			if (!_idempotentHintSet)
			{
				return null;
			}
			return _idempotentHint;
		}
	}

	public bool? OpenWorldHintValue
	{
		get
		{
			if (!_openWorldHintSet)
			{
				return null;
			}
			return _openWorldHint;
		}
	}

	public AiToolAttribute(string name, string? title = null)
	{
		Name = name;
		Title = title;
	}
}
}
