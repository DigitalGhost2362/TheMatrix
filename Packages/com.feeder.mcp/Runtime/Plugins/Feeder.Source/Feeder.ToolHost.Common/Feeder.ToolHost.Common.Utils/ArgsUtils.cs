using System;
using System.Collections.Generic;

namespace Feeder.ToolHost.Common.Utils
{
public static class ArgsUtils
{
	public static Dictionary<string, string> ParseLineArguments(string[] args)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		for (int i = 0; i < args.Length; i++)
		{
			string text = args[i];
			if (text.Contains('='))
			{
				string[] array = text.Split('=', 2);
				if (array.Length == 2)
				{
					string key = ExtractArgumentName(array[0]);
					dictionary[key] = array[1];
					continue;
				}
			}
			string key2 = ExtractArgumentName(text);
			string value = string.Empty;
			if (i + 1 < args.Length)
			{
				string text2 = args[i + 1];
				if (!IsArgumentName(text2))
				{
					value = text2;
					i++;
				}
			}
			dictionary[key2] = value;
		}
		return dictionary;
	}

	private static string ExtractArgumentName(string arg)
	{
		if (arg.StartsWith("--"))
		{
			return arg.Substring(2);
		}
		if (arg.StartsWith("-"))
		{
			return arg.Substring(1);
		}
		return arg;
	}

	private static bool IsArgumentName(string arg)
	{
		if (!arg.StartsWith("-"))
		{
			return false;
		}
		if (arg.Length > 1 && char.IsDigit(arg[1]))
		{
			return false;
		}
		return true;
	}

	public static Dictionary<string, string> ParseCommandLineArguments()
	{
		return ParseLineArguments(Environment.GetCommandLineArgs());
	}
}
}
