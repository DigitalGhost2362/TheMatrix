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
		HashSet<string> squeezedAmbiguous = new HashSet<string>(StringComparer.Ordinal);
		Dictionary<string, string> squeezed = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (ParameterInfo item3 in methodParams.Where((ParameterInfo p) => p.Name != null))
		{
			string text2 = Squeeze(item3.Name);
			if (squeezed.TryGetValue(text2, out string existing) && existing != item3.Name)
			{
				squeezedAmbiguous.Add(text2);
			}
			else
			{
				squeezed[text2] = item3.Name;
			}
		}
		foreach (KeyValuePair<string, string> pair in squeezed)
		{
			if (!squeezedAmbiguous.Contains(pair.Key))
			{
				dictionary.TryAdd(SqueezedPrefix + pair.Key, pair.Value);
			}
		}
		if (dictionary.Count <= 0)
		{
			return null;
		}
		return dictionary;
	}

	// squeezed keys live apart from exact lowercase keys, so a shared squeezed name never hits another parameter's exact name
	private const string SqueezedPrefix = "\u0001";

	// game_object, game-object and gameObject all read as "gameobject"
	private static string Squeeze(string name)
	{
		return name.Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
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
		if (paramNameLookup.TryGetValue(SqueezedPrefix + Squeeze(incomingName), out string value2))
		{
			return value2;
		}
		return incomingName;
	}
}
}
