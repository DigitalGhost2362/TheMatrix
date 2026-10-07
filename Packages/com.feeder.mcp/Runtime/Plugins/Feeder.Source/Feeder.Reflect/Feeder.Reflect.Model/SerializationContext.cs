using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Feeder.Reflect.Model
{
public class SerializationContext
{
	private class ReferenceEqualityComparer : IEqualityComparer<object>
	{
		bool IEqualityComparer<object>.Equals(object? x, object? y)
		{
			return x == y;
		}

		int IEqualityComparer<object>.GetHashCode(object obj)
		{
			return RuntimeHelpers.GetHashCode(obj);
		}
	}

	private readonly Dictionary<object, string> _visited;

	private readonly Stack<string> _pathStack;

	public SerializationContext()
	{
		_visited = new Dictionary<object, string>(new ReferenceEqualityComparer());
		_pathStack = new Stack<string>();
		_pathStack.Push("#");
	}

	public bool Enter(object obj, string? segment)
	{
		if (!string.IsNullOrEmpty(segment))
		{
			_pathStack.Push(segment);
		}
		if (_visited.ContainsKey(obj))
		{
			return false;
		}
		_visited[obj] = BuildCurrentPath();
		return true;
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

	public void Exit(object obj, string? segment)
	{
		_visited.Remove(obj);
		if (!string.IsNullOrEmpty(segment))
		{
			_pathStack.Pop();
		}
	}

	public string GetPath(object obj)
	{
		if (!_visited.TryGetValue(obj, out string value))
		{
			throw new InvalidOperationException("Object of type '" + obj.GetType().GetTypeShortName() + "' was not found in the serialization context. GetPath should only be called for objects that have been visited.");
		}
		return value;
	}
}
}
