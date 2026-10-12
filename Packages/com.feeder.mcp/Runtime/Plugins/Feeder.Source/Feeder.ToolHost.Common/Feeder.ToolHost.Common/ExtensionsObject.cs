using System.Collections.Generic;
using System.Threading.Tasks;

namespace Feeder.ToolHost.Common
{
public static class ExtensionsObject
{
	public static Task<T> TaskFromResult<T>(this T response)
	{
		return Task.FromResult(response);
	}

	public static T[] MakeArray<T>(this T item)
	{
		return new T[1] { item };
	}

	public static List<T> MakeList<T>(this T item)
	{
		return new List<T> { item };
	}

	public static string JoinString(this IEnumerable<string> items, string separator)
	{
		return string.Join(separator, items);
	}

	public static string JoinString(this IEnumerable<int> items, string separator)
	{
		return string.Join(separator, items);
	}
}
}
