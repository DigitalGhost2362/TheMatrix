using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Feeder.ToolHost.Utils
{
public static class ParameterNameUtils
{
	public static Dictionary<string, string>? BuildParameterNameLookup(ParameterInfo[]? methodParams)
	{
		if (methodParams == null || methodParams.Length == 0)
		{
			return null;
		}
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		foreach (ParameterInfo item in methodParams.Where((ParameterInfo p) => p.Name != null))
		{
			string text = item.Name.ToLowerInvariant();
			if (dictionary.ContainsKey(text))
			{
				hashSet.Add(text);
			}
			else
			{
				dictionary[text] = item.Name;
			}
		}
		foreach (string item2 in hashSet)
		{
			dictionary.Remove(item2);
		}
		if (dictionary.Count <= 0)
		{
			return null;
		}
		return dictionary;
	}

	public static string NormalizeParameterName(string incomingName, Dictionary<string, string>? paramNameLookup)
	{
		if (paramNameLookup == null || string.IsNullOrEmpty(incomingName))
		{
			return incomingName;
		}
		string key = incomingName.ToLowerInvariant();
		if (paramNameLookup.TryGetValue(key, out string value))
		{
			return value;
		}
		return incomingName;
	}
}
}
