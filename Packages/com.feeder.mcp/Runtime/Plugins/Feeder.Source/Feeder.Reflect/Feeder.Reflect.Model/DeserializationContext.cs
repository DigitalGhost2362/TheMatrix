using System;
using System.Collections.Generic;

namespace Feeder.Reflect.Model
{
public class DeserializationContext
{
	private readonly Dictionary<string, object> _resolvedObjects;

	private readonly Stack<string> _pathStack;

	public DeserializationContext()
	{
		_resolvedObjects = new Dictionary<string, object>();
		_pathStack = new Stack<string>();
		_pathStack.Push("#");
	}

	public void Enter(string? segment)
	{
		if (!string.IsNullOrEmpty(segment))
		{
			_pathStack.Push(segment);
		}
	}

	public void Exit(string? segment)
	{
		if (!string.IsNullOrEmpty(segment))
		{
			_pathStack.Pop();
		}
	}

	public void Register(object obj)
	{
		string key = BuildCurrentPath();
		_resolvedObjects[key] = obj;
	}

	public bool TryResolve(string refPath, out object? result)
	{
		if (_resolvedObjects.TryGetValue(refPath, out object value))
		{
			result = value;
			return true;
		}
		result = null;
		return false;
	}

	public string GetCurrentPath()
	{
		return BuildCurrentPath();
	}

	public string BuildCurrentPath()
	{
		if (_pathStack.Count == 1)
		{
			return "#";
		}
		string[] array = new string[_pathStack.Count];
		_pathStack.CopyTo(array, 0);
		Array.Reverse(array);
		return string.Join("/", array);
	}
}
}
