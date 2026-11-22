using System;
using System.Collections.Concurrent;

namespace Feeder.Reflect.Utils
{
public static class StringUtils
{
	public const string Null = "null";

	public const string NA = "N/A";

	private static readonly ConcurrentDictionary<int, string> _paddingCache = new ConcurrentDictionary<int, string>();

	public static string GetPadding(int depth)
	{
		if (depth < 0)
		{
			return string.Empty;
		}
		return _paddingCache.GetOrAdd(depth, (int d) => new string(' ', d * 2));
	}

	public static bool IsNullOrEmpty(string? value)
	{
		if (!string.IsNullOrEmpty(value))
		{
			return value == "null";
		}
		return true;
	}

	public static bool IsNullOrWhiteSpace(string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value == "null";
		}
		return true;
	}

	public static string? TrimPath(string? path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return path;
		}
		ReadOnlySpan<char> span = path.AsSpan();
		span = span.Trim('/');
		if (!span.IsEmpty)
		{
			return span.ToString();
		}
		return string.Empty;
	}

	public static bool Path_ParseParent(string? path, out string? parentPath, out string? name)
	{
		if (string.IsNullOrEmpty(path))
		{
			parentPath = null;
			name = null;
			return false;
		}
		ReadOnlySpan<char> span = path.AsSpan().Trim('/');
		if (span.IsEmpty)
		{
			parentPath = null;
			name = null;
			return false;
		}
		int num = span.LastIndexOf('/');
		if (num >= 0)
		{
			parentPath = span.Slice(0, num).ToString();
			name = span.Slice(num + 1).ToString();
			return true;
		}
		parentPath = null;
		name = span.ToString();
		return false;
	}

	public static string? Path_GetParentFolderPath(string? path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return null;
		}
		ReadOnlySpan<char> span = path.AsSpan().TrimEnd('/');
		int num = span.LastIndexOf('/');
		if (num < 0)
		{
			return span.ToString();
		}
		return span.Slice(0, num).ToString();
	}

	public static string? Path_GetLastName(string? path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return path;
		}
		ReadOnlySpan<char> span = path.AsSpan().TrimEnd('/');
		int num = span.LastIndexOf('/');
		if (num < 0)
		{
			return span.ToString();
		}
		return span.Slice(num + 1).ToString();
	}

	public static object? ConvertParameterStringToEnum(object? value, Type enumType, string parameterName)
	{
		if (value is string text && enumType.IsEnum)
		{
			if (Enum.TryParse(enumType, text, ignoreCase: true, out object result))
			{
				if (Enum.IsDefined(enumType, result))
				{
					return result;
				}
				throw new ArgumentException("Value '" + text + "' for parameter '" + parameterName + "' was parsed but is not a defined member of '" + enumType.GetTypeId() + "'. Valid values are: " + string.Join(", ", Enum.GetNames(enumType)));
			}
			throw new ArgumentException("Value '" + text + "' for parameter '" + parameterName + "' could not be parsed as '" + enumType.GetTypeId() + "'. Valid values are: " + string.Join(", ", Enum.GetNames(enumType)));
		}
		throw new ArgumentException($"Parameter '{parameterName}' type mismatch. Expected '{enumType.GetTypeId()}', but got '{value?.GetType()}'.");
	}
}
}
