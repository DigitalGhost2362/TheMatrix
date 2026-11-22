namespace Feeder.ToolHost.Common
{
public class ThreadSafeBool
{
	private bool _value;

	private readonly object _lock = new object();

	public bool Value
	{
		get
		{
			lock (_lock)
			{
				return _value;
			}
		}
	}

	public ThreadSafeBool(bool initialValue = false)
	{
		_value = initialValue;
	}

	public bool TrySetTrue()
	{
		lock (_lock)
		{
			if (_value)
			{
				return false;
			}
			_value = true;
			return true;
		}
	}

	public bool TrySetFalse()
	{
		lock (_lock)
		{
			if (!_value)
			{
				return false;
			}
			_value = false;
			return true;
		}
	}

	public static implicit operator bool(ThreadSafeBool threadSafeBool)
	{
		return threadSafeBool.Value;
	}
}
}
