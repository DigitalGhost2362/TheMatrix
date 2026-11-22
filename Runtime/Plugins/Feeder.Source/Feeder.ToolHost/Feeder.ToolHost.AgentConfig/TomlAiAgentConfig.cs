using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Feeder.Logging;

namespace Feeder.ToolHost.AgentConfig
{
public class TomlAiAgentConfig : AiAgentConfig
{
	private sealed class RawTomlValue
	{
		public string Value { get; }

		public RawTomlValue(string value)
		{
			Value = value;
		}
	}

	private readonly Dictionary<string, (object value, bool required, ValueComparisonMode comparison)> _properties = new Dictionary<string, (object, bool, ValueComparisonMode)>();

	private readonly HashSet<string> _propertiesToRemove = new HashSet<string>();

	public override string ExpectedFileContent
	{
		get
		{
			string text = base.BodyPath + ".Feeder-MCP";
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("[" + text + "]");
			foreach (string item in _properties.Keys.OrderBy<string, string>((string k) => k, StringComparer.Ordinal))
			{
				stringBuilder.AppendLine(FormatTomlProperty(item, _properties[item].value));
			}
			return stringBuilder.ToString();
		}
	}

	public TomlAiAgentConfig(string name, string configPath, string bodyPath = "mcpServers", ILogger? logger = null)
		: base(name, configPath, bodyPath, logger)
	{
	}

	public TomlAiAgentConfig SetProperty(string key, object value, bool requiredForConfiguration = false, ValueComparisonMode comparison = ValueComparisonMode.Exact)
	{
		_properties[key] = (value, requiredForConfiguration, comparison);
		return this;
	}

	public TomlAiAgentConfig SetProperty(string key, object[] values, bool requiredForConfiguration = false, ValueComparisonMode comparison = ValueComparisonMode.Exact)
	{
		_properties[key] = (values, requiredForConfiguration, comparison);
		return this;
	}

	public TomlAiAgentConfig SetPropertyToRemove(string key)
	{
		_propertiesToRemove.Add(key);
		return this;
	}

	public new TomlAiAgentConfig AddIdentityKey(string key)
	{
		base.AddIdentityKey(key);
		return this;
	}

	public override void ApplyHttpAuthorization(bool isRequired, string? token)
	{
	}

	public override void ApplyStdioAuthorization(bool isRequired, string? token)
	{
		if (_properties.TryGetValue("args", out (object, bool, ValueComparisonMode) value) && value.Item1 is string[] source)
		{
			string tokenPrefix = "token=";
			List<string> list = source.Where((string arg) => !arg.StartsWith(tokenPrefix, StringComparison.Ordinal)).ToList();
			if (isRequired && !string.IsNullOrEmpty(token))
			{
				list.Add(tokenPrefix + token);
			}
			object[] values = list.ToArray();
			SetProperty("args", values, value.Item2, value.Item3);
		}
	}

	public override bool Configure()
	{
		if (string.IsNullOrEmpty(base.ConfigPath))
		{
			return false;
		}
		_logger?.LogInformation("Configuring MCP client TOML with path: {ConfigPath} and bodyPath: {BodyPath}", base.ConfigPath, base.BodyPath);
		try
		{
			string text = base.BodyPath + ".Feeder-MCP";
			if (!File.Exists(base.ConfigPath))
			{
				string directoryName = Path.GetDirectoryName(base.ConfigPath);
				if (!string.IsNullOrEmpty(directoryName))
				{
					Directory.CreateDirectory(directoryName);
				}
				WriteAllTextAtomic(base.ConfigPath, ExpectedFileContent);
				return true;
			}
			List<string> list = ReadLines(base.ConfigPath, out string newline, out bool finalNewline);
			string[] deprecatedMcpServerNames = AiAgentConfig.DeprecatedMcpServerNames;
			foreach (string text2 in deprecatedMcpServerNames)
			{
				string sectionName = base.BodyPath + "." + text2;
				int num = FindTomlSection(list, sectionName);
				if (num >= 0)
				{
					int num2 = FindSectionEnd(list, num);
					list.RemoveRange(num, num2 - num);
				}
			}
			RemoveDuplicateServerSections(list, text);
			int num3 = FindTomlSection(list, text);
			if (num3 >= 0)
			{
				int num4 = FindOwnSectionEnd(list, num3);
				Dictionary<string, object> dictionary = ParseSectionProperties(list, num3 + 1, num4);
				foreach (string item in _propertiesToRemove)
				{
					dictionary.Remove(item);
				}
				foreach (KeyValuePair<string, (object, bool, ValueComparisonMode)> property in _properties)
				{
					dictionary[property.Key] = property.Value.Item1;
				}
				list.RemoveRange(num3, num4 - num3);
				string text3 = GenerateTomlSectionFromDict(text, dictionary);
				list.InsertRange(num3, SplitLines(text3.TrimEnd()));
			}
			else
			{
				if (list.Count > 0)
				{
					if (!string.IsNullOrWhiteSpace(list[list.Count - 1]))
					{
						list.Add("");
					}
				}
				Dictionary<string, object> properties = _properties.ToDictionary<KeyValuePair<string, (object, bool, ValueComparisonMode)>, string, object>((KeyValuePair<string, (object value, bool required, ValueComparisonMode comparison)> p) => p.Key, (KeyValuePair<string, (object value, bool required, ValueComparisonMode comparison)> p) => p.Value.value);
				list.AddRange(SplitLines(GenerateTomlSectionFromDict(text, properties).TrimEnd()));
			}
			WriteAllTextAtomic(base.ConfigPath, string.Join(newline, list) + (finalNewline ? newline : string.Empty));
			return IsConfigured();
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error configuring TOML file: {Message}", ex.Message);
			return false;
		}
	}

	public override bool Unconfigure()
	{
		if (string.IsNullOrEmpty(base.ConfigPath) || !File.Exists(base.ConfigPath))
		{
			return false;
		}
		try
		{
			string text = base.BodyPath + ".Feeder-MCP";
			List<string> list = ReadLines(base.ConfigPath, out string newline, out bool finalNewline);
			bool flag = false;
			int num = FindTomlSection(list, text);
			if (num >= 0)
			{
				int num2 = FindOwnSectionEnd(list, num);
				list.RemoveRange(num, num2 - num);
				if (num > 0 && num < list.Count && string.IsNullOrWhiteSpace(list[num - 1]) && string.IsNullOrWhiteSpace(list[num]))
				{
					list.RemoveAt(num);
				}
				flag = true;
			}
			string[] deprecatedMcpServerNames = AiAgentConfig.DeprecatedMcpServerNames;
			foreach (string text2 in deprecatedMcpServerNames)
			{
				string sectionName = base.BodyPath + "." + text2;
				int num3 = FindTomlSection(list, sectionName);
				if (num3 >= 0)
				{
					int num4 = FindSectionEnd(list, num3);
					list.RemoveRange(num3, num4 - num3);
					flag = true;
				}
			}
			int count = list.Count;
			RemoveDuplicateServerSections(list, text);
			if (list.Count != count)
			{
				flag = true;
			}
			if (!flag)
			{
				return false;
			}
			WriteAllTextAtomic(base.ConfigPath, string.Join(newline, list) + (finalNewline ? newline : string.Empty));
			return true;
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error unconfiguring TOML MCP client: {Message}", ex.Message);
			return false;
		}
	}

	public override bool IsDetected()
	{
		if (string.IsNullOrEmpty(base.ConfigPath) || !File.Exists(base.ConfigPath))
		{
			return false;
		}
		try
		{
			List<string> lines = File.ReadAllLines(base.ConfigPath).ToList();
			string text = base.BodyPath + ".Feeder-MCP";
			if (FindTomlSection(lines, text) >= 0)
			{
				return true;
			}
			string[] deprecatedMcpServerNames = AiAgentConfig.DeprecatedMcpServerNames;
			foreach (string text2 in deprecatedMcpServerNames)
			{
				if (FindTomlSection(lines, base.BodyPath + "." + text2) >= 0)
				{
					return true;
				}
			}
			return FindDuplicateServerSectionIndices(lines, text).Count > 0;
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error reading TOML config file: {Message}", ex.Message);
			return false;
		}
	}

	public override bool IsConfigured()
	{
		if (string.IsNullOrEmpty(base.ConfigPath) || !File.Exists(base.ConfigPath))
		{
			return false;
		}
		try
		{
			List<string> lines = File.ReadAllLines(base.ConfigPath).ToList();
			string sectionName = base.BodyPath + ".Feeder-MCP";
			int num = FindTomlSection(lines, sectionName);
			if (num < 0)
			{
				return false;
			}
			int endIndex = FindSectionEnd(lines, num);
			Dictionary<string, object> existingProps = ParseSectionProperties(lines, num + 1, endIndex);
			return AreRequiredPropertiesMatching(existingProps) && !HasPropertiesToRemove(existingProps);
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error reading TOML config file: {Message}", ex.Message);
			return false;
		}
	}

	private bool AreRequiredPropertiesMatching(Dictionary<string, object> existingProps)
	{
		foreach (KeyValuePair<string, (object, bool, ValueComparisonMode)> property in _properties)
		{
			if (property.Value.Item2)
			{
				if (!existingProps.TryGetValue(property.Key, out object value))
				{
					return false;
				}
				if (!ValuesMatch(property.Value.Item3, property.Value.Item1, value))
				{
					return false;
				}
			}
		}
		return true;
	}

	private bool HasPropertiesToRemove(Dictionary<string, object> existingProps)
	{
		if (_propertiesToRemove.Count == 0)
		{
			return false;
		}
		return _propertiesToRemove.Any((string key) => existingProps.ContainsKey(key));
	}

	private static bool ValuesMatch(ValueComparisonMode comparison, object expected, object actual)
	{
		if (expected is string expected2)
		{
			if (actual is string actual2)
			{
				return AreStringValuesEquivalent(comparison, expected2, actual2);
			}
		}
		else if (expected is string[] array)
		{
			if (actual is string[] array2)
			{
				return array.Length == array2.Length && array.Zip(array2, (string x, string y) => AreStringValuesEquivalent(comparison, x, y)).All((bool match) => match);
			}
		}
		else if (expected is bool flag)
		{
			if (actual is bool flag2)
			{
				return flag == flag2;
			}
		}
		else if (expected is bool[] array3)
		{
			if (actual is bool[] array4)
			{
				return array3.Length == array4.Length && array3.Zip(array4, (bool x, bool y) => x == y).All((bool match) => match);
			}
		}
		else if (expected is int num)
		{
			if (actual is int num2)
			{
				return num == num2;
			}
		}
		else if (expected is int[] array5)
		{
			if (actual is int[] array6)
			{
				return array5.Length == array6.Length && array5.Zip(array6, (int x, int y) => x == y).All((bool match) => match);
			}
		}
		else if (expected is Dictionary<string, string> dictionary)
		{
			Dictionary<string, string> dictionary2 = actual as Dictionary<string, string>;
			if (dictionary2 != null)
			{
				return dictionary.Count == dictionary2.Count && dictionary.All((KeyValuePair<string, string> kv) => dictionary2.TryGetValue(kv.Key, out string value) && AreStringValuesEquivalent(comparison, kv.Value, value));
			}
		}
		return false;
	}

	private static bool AreStringValuesEquivalent(ValueComparisonMode comparison, string expected, string actual)
	{
		return comparison switch
		{
			ValueComparisonMode.Path => NormalizePath(expected) == NormalizePath(actual), 
			ValueComparisonMode.Url => string.Equals(NormalizeUrl(expected), NormalizeUrl(actual), StringComparison.OrdinalIgnoreCase), 
			_ => expected == actual, 
		};
	}

	private static string NormalizePath(string path)
	{
		return path.Replace('\\', '/').TrimEnd('/');
	}

	private static string NormalizeUrl(string url)
	{
		if (Uri.TryCreate(url, UriKind.Absolute, out Uri result))
		{
			string text = result.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
			string text2 = result.AbsolutePath.TrimEnd('/');
			return text + text2 + result.Query;
		}
		return url.TrimEnd('/');
	}

	private static string GenerateTomlSectionFromDict(string sectionName, Dictionary<string, object> properties)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("[" + sectionName + "]");
		foreach (string item in properties.Keys.OrderBy<string, string>((string k) => k, StringComparer.Ordinal))
		{
			stringBuilder.AppendLine(FormatTomlProperty(item, properties[item]));
		}
		return stringBuilder.ToString();
	}

	private static string FormatTomlProperty(string key, object value)
	{
		if (!(value is string value2))
		{
			if (!(value is string[] source))
			{
				if (!(value is int num))
				{
					if (!(value is int[] values))
					{
						if (!(value is bool flag))
						{
							if (!(value is bool[] source2))
							{
								if (!(value is Dictionary<string, string> dict))
								{
									if (value is RawTomlValue rawTomlValue)
									{
										return key + " = " + rawTomlValue.Value;
									}
									throw new InvalidOperationException($"Unsupported TOML value type: {value.GetType()}");
								}
								return FormatTomlInlineTable(key, dict);
							}
							return key + " = [" + string.Join(",", source2.Select((bool v) => v.ToString().ToLower())) + "]";
						}
						return key + " = " + flag.ToString().ToLower();
					}
					return key + " = [" + string.Join(",", values) + "]";
				}
				return $"{key} = {num}";
			}
			return key + " = [" + string.Join(",", source.Select((string v) => "\"" + EscapeTomlString(v) + "\"")) + "]";
		}
		return key + " = \"" + EscapeTomlString(value2) + "\"";
	}

	private static Dictionary<string, object> ParseSectionProperties(List<string> lines, int startIndex, int endIndex)
	{
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		for (int i = startIndex; i < endIndex; i++)
		{
			string text = lines[i].Trim();
			if (string.IsNullOrWhiteSpace(text) || text.StartsWith("#"))
			{
				continue;
			}
			string[] array = text.Split('=', 2);
			if (array.Length != 2)
			{
				continue;
			}
			string key = array[0].Trim();
			string text2 = array[1].Trim();
			if (text2.StartsWith("["))
			{
				string rawValue = StripArrayInlineComment(text2);
				dictionary[key] = ParseTypedTomlArrayValue(rawValue);
				continue;
			}
			if (text2.StartsWith("\""))
			{
				string text3 = ParseTomlStringValue(text);
				if (text3 != null)
				{
					dictionary[key] = text3;
				}
				continue;
			}
			if (text2.StartsWith("{"))
			{
				Dictionary<string, string> dictionary2 = ParseTomlInlineTable(text2);
				if (dictionary2 != null)
				{
					dictionary[key] = dictionary2;
				}
				else
				{
					dictionary[key] = new RawTomlValue(text2);
				}
				continue;
			}
			string text4 = StripInlineComment(text2);
			int result;
			if (text4 == "true" || text4 == "false")
			{
				dictionary[key] = text4 == "true";
			}
			else if (int.TryParse(text4, out result))
			{
				dictionary[key] = result;
			}
			else if (text4.Length > 0)
			{
				dictionary[key] = new RawTomlValue(text4);
			}
		}
		return dictionary;
	}

	private static string FormatTomlInlineTable(string key, Dictionary<string, string> dict)
	{
		string text = string.Join(", ", dict.Select<KeyValuePair<string, string>, string>((KeyValuePair<string, string> kv) => "\"" + EscapeTomlString(kv.Key) + "\" = \"" + EscapeTomlString(kv.Value) + "\""));
		return key + " = { " + text + " }";
	}

	private static Dictionary<string, string>? ParseTomlInlineTable(string rawValue)
	{
		string text = rawValue.Trim();
		int num = text.LastIndexOf('}');
		if (!text.StartsWith("{") || num < 0)
		{
			return null;
		}
		string text2 = text.Substring(1, num - 1).Trim();
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		if (string.IsNullOrEmpty(text2))
		{
			return dictionary;
		}
		int i = 0;
		while (i < text2.Length)
		{
			for (; i < text2.Length && (char.IsWhiteSpace(text2[i]) || text2[i] == ','); i++)
			{
			}
			if (i >= text2.Length)
			{
				break;
			}
			string text3;
			if (text2[i] == '"')
			{
				text3 = ReadQuotedString(text2, ref i);
			}
			else
			{
				int num2 = i;
				for (; i < text2.Length && text2[i] != '=' && text2[i] != ','; i++)
				{
				}
				int num3 = num2;
				text3 = text2.Substring(num3, i - num3).Trim();
			}
			if (text3 == null)
			{
				return null;
			}
			for (; i < text2.Length && char.IsWhiteSpace(text2[i]); i++)
			{
			}
			if (i >= text2.Length || text2[i] != '=')
			{
				return null;
			}
			for (i++; i < text2.Length && char.IsWhiteSpace(text2[i]); i++)
			{
			}
			string text4;
			if (i < text2.Length && text2[i] == '"')
			{
				text4 = ReadQuotedString(text2, ref i);
			}
			else
			{
				int num4 = i;
				for (; i < text2.Length && text2[i] != ','; i++)
				{
				}
				int num3 = num4;
				text4 = text2.Substring(num3, i - num3).Trim();
			}
			if (text4 == null)
			{
				return null;
			}
			dictionary[text3] = text4;
		}
		return dictionary;
	}

	private static string? ReadQuotedString(string s, ref int pos)
	{
		if (pos >= s.Length || s[pos] != '"')
		{
			return null;
		}
		pos++;
		StringBuilder stringBuilder = new StringBuilder();
		while (pos < s.Length)
		{
			if (s[pos] == '\\' && pos + 1 < s.Length)
			{
				pos++;
				switch (s[pos])
				{
				case 'b':
					stringBuilder.Append('\b');
					pos++;
					continue;
				case 't':
					stringBuilder.Append('\t');
					pos++;
					continue;
				case 'n':
					stringBuilder.Append('\n');
					pos++;
					continue;
				case 'f':
					stringBuilder.Append('\f');
					pos++;
					continue;
				case 'r':
					stringBuilder.Append('\r');
					pos++;
					continue;
				case '"':
					stringBuilder.Append('"');
					pos++;
					continue;
				case '\\':
					stringBuilder.Append('\\');
					pos++;
					continue;
				case 'u':
					if (pos + 4 < s.Length)
					{
						if (int.TryParse(s.Substring(pos + 1, 4), NumberStyles.HexNumber, null, out var result2))
						{
							stringBuilder.Append((char)result2);
						}
						pos += 5;
						continue;
					}
					break;
				case 'U':
					if (pos + 8 < s.Length)
					{
						if (int.TryParse(s.Substring(pos + 1, 8), NumberStyles.HexNumber, null, out var result))
						{
							stringBuilder.Append(char.ConvertFromUtf32(result));
						}
						pos += 9;
						continue;
					}
					break;
				}
				stringBuilder.Append('\\');
				stringBuilder.Append(s[pos]);
				pos++;
			}
			else
			{
				if (s[pos] == '"')
				{
					pos++;
					return stringBuilder.ToString();
				}
				stringBuilder.Append(s[pos]);
				pos++;
			}
		}
		return null;
	}

	private static string EscapeTomlString(string value)
	{
		return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
	}

	private static string StripInlineComment(string value)
	{
		int num = value.IndexOf('#');
		if (num < 0)
		{
			return value;
		}
		return value.Substring(0, num).TrimEnd();
	}

	private static string StripArrayInlineComment(string value)
	{
		int num = 0;
		bool flag = false;
		bool flag2 = false;
		for (int i = 0; i < value.Length; i++)
		{
			char c = value[i];
			if (flag2)
			{
				flag2 = false;
			}
			else if ((c == '\\') & flag)
			{
				flag2 = true;
			}
			else if (c == '"')
			{
				flag = !flag;
			}
			else
			{
				if (flag)
				{
					continue;
				}
				switch (c)
				{
				case '[':
					num++;
					break;
				case ']':
					num--;
					if (num == 0)
					{
						return value.Substring(0, i + 1);
					}
					break;
				}
			}
		}
		return value;
	}

	private static string? ParseTomlStringValue(string line)
	{
		string[] array = line.Split('=', 2);
		if (array.Length != 2)
		{
			return null;
		}
		string text = array[1].Trim();
		if (!text.StartsWith("\""))
		{
			return text;
		}
		for (int i = 1; i < text.Length; i++)
		{
			if (text[i] == '\\')
			{
				i++;
			}
			else if (text[i] == '"')
			{
				return text.Substring(1, i - 1).Replace("\\\"", "\"").Replace("\\\\", "\\");
			}
		}
		return text;
	}

	private static object ParseTypedTomlArrayValue(string rawValue)
	{
		if (!rawValue.StartsWith("[") || !rawValue.EndsWith("]"))
		{
			return Array.Empty<string>();
		}
		string text = rawValue.Substring(1, rawValue.Length - 1 - 1).Trim();
		if (string.IsNullOrEmpty(text))
		{
			return Array.Empty<string>();
		}
		if (text.StartsWith("\""))
		{
			return ParseTomlStringArrayContent(text);
		}
		if (text.StartsWith("true", StringComparison.Ordinal) || text.StartsWith("false", StringComparison.Ordinal))
		{
			return ((object)ParseTomlBoolArrayContent(text)) ?? ((object)new RawTomlValue(rawValue));
		}
		if (char.IsDigit(text[0]) || text[0] == '-')
		{
			return ((object)ParseTomlIntArrayContent(text)) ?? ((object)new RawTomlValue(rawValue));
		}
		return ParseTomlStringArrayContent(text);
	}

	private static string[] ParseTomlStringArrayContent(string arrayContent)
	{
		List<string> list = new List<string>();
		bool flag = false;
		bool flag2 = false;
		StringBuilder stringBuilder = new StringBuilder();
		foreach (char c in arrayContent)
		{
			if (flag2)
			{
				stringBuilder.Append(c);
				flag2 = false;
				continue;
			}
			switch (c)
			{
			case '\\':
				flag2 = true;
				break;
			case '"':
				if (flag)
				{
					string text = stringBuilder.ToString();
					text = text.Replace("\\\"", "\"").Replace("\\\\", "\\");
					list.Add(text);
					stringBuilder.Clear();
				}
				flag = !flag;
				break;
			default:
				if (flag)
				{
					stringBuilder.Append(c);
				}
				break;
			}
		}
		return list.ToArray();
	}

	private static bool[]? ParseTomlBoolArrayContent(string arrayContent)
	{
		string[] array = arrayContent.Split(',');
		bool[] array2 = new bool[array.Length];
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim().ToLowerInvariant();
			if (text == "true")
			{
				array2[i] = true;
				continue;
			}
			if (text == "false")
			{
				array2[i] = false;
				continue;
			}
			return null;
		}
		return array2;
	}

	private static int[]? ParseTomlIntArrayContent(string arrayContent)
	{
		string[] array = arrayContent.Split(',');
		int[] array2 = new int[array.Length];
		for (int i = 0; i < array.Length; i++)
		{
			if (!int.TryParse(array[i].Trim(), out array2[i]))
			{
				return null;
			}
		}
		return array2;
	}

	private List<(int start, int end)> FindDuplicateServerSectionIndices(List<string> lines, string ownSectionName)
	{
		Dictionary<string, (string, ValueComparisonMode)> dictionary = _identityKeys.Where((string key) => _properties.TryGetValue(key, out (object, bool, ValueComparisonMode) value2) && value2.Item1 is string).ToDictionary((string key) => key, (string key) => ((string)_properties[key].value, comparison: _properties[key].comparison));
		if (dictionary.Count == 0)
		{
			return new List<(int, int)>();
		}
		string value = "[" + base.BodyPath + ".";
		List<(int, int)> list = new List<(int, int)>();
		for (int num = 0; num < lines.Count; num++)
		{
			string text = lines[num].Trim();
			if (!text.StartsWith(value) || !text.EndsWith("]"))
			{
				continue;
			}
			string text2 = text;
			if (!(text2.Substring(1, text2.Length - 1 - 1) == ownSectionName))
			{
				int num2 = FindSectionEnd(lines, num);
				Dictionary<string, object> props = ParseSectionProperties(lines, num + 1, num2);
				if (dictionary.Any<KeyValuePair<string, (string, ValueComparisonMode)>>((KeyValuePair<string, (string, ValueComparisonMode comparison)> identity) => props.TryGetValue(identity.Key, out object value2) && value2 is string actual && AreStringValuesEquivalent(identity.Value.comparison, identity.Value.Item1, actual)))
				{
					list.Add((num, num2));
				}
			}
		}
		return list;
	}

	private void RemoveDuplicateServerSections(List<string> lines, string ownSectionName)
	{
		List<(int, int)> list = FindDuplicateServerSectionIndices(lines, ownSectionName);
		for (int num = list.Count - 1; num >= 0; num--)
		{
			var (num2, num3) = list[num];
			lines.RemoveRange(num2, num3 - num2);
		}
	}

	private static int FindTomlSection(List<string> lines, string sectionName)
	{
		string text = "[" + sectionName + "]";
		for (int i = 0; i < lines.Count; i++)
		{
			if (lines[i].Trim() == text)
			{
				return i;
			}
		}
		return -1;
	}

	// the file's own line ending and final newline are kept, so the lines of other servers stay byte-for-byte the same
	private static List<string> ReadLines(string path, out string newline, out bool finalNewline)
	{
		string text = File.ReadAllText(path);
		newline = text.Contains("\r\n") ? "\r\n" : "\n";
		finalNewline = text.EndsWith("\n", StringComparison.Ordinal);
		List<string> lines = SplitLines(text).ToList();
		if (finalNewline || text.Length == 0)
		{
			lines.RemoveAt(lines.Count - 1);
		}
		return lines;
	}

	private static string[] SplitLines(string text)
	{
		return text.Replace("\r\n", "\n").Split('\n');
	}

	// blank and comment lines right before the next section belong to that section, not to ours
	private static int FindOwnSectionEnd(List<string> lines, int sectionStartIndex)
	{
		int end = FindSectionEnd(lines, sectionStartIndex);
		while (end > sectionStartIndex + 1 && (string.IsNullOrWhiteSpace(lines[end - 1]) || lines[end - 1].TrimStart().StartsWith("#")))
		{
			end--;
		}
		return end;
	}

	private static int FindSectionEnd(List<string> lines, int sectionStartIndex)
	{
		for (int i = sectionStartIndex + 1; i < lines.Count; i++)
		{
			string text = lines[i].Trim();
			if (text.StartsWith("[") && text.EndsWith("]"))
			{
				return i;
			}
		}
		return lines.Count;
	}
}
}
