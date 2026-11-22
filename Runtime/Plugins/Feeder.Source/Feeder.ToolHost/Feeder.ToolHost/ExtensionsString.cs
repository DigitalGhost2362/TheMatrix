using System.Collections.Generic;
using System.Linq;

namespace Feeder.ToolHost
{
public static class ExtensionsString
{
	public static string Join(this IEnumerable<string> strings, string separator = ", ")
	{
		return string.Join(separator, strings);
	}

	public static string JoinExcept(this IEnumerable<string> strings, string except, string separator = ", ")
	{
		return string.Join(separator, strings.Where((string s) => s != except));
	}

	public static string JoinEnclose(this IEnumerable<string> strings, string separator = ", ", string enclose = "'")
	{
		return string.Join(separator, strings.Select((string s) => enclose + s + enclose));
	}

	public static string JoinEncloseExcept(this IEnumerable<string> strings, string except, string separator = ", ", string enclose = "'")
	{
		return string.Join(separator, from s in strings
			where s != except
			select enclose + s + enclose);
	}
}
}
