using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Feeder.Json;
using Feeder.Json.Nodes;
using Feeder.ToolHost.Common;
using Feeder.Logging;

namespace Feeder.ToolHost.AgentConfig
{
public class JsonAiAgentConfig : AiAgentConfig
{
	private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	private readonly Dictionary<string, (JsonNode value, bool required, ValueComparisonMode comparison)> _properties = new Dictionary<string, (JsonNode, bool, ValueComparisonMode)>();

	private readonly HashSet<string> _propertiesToRemove = new HashSet<string>();

	public override string ExpectedFileContent
	{
		get
		{
			JsonObject value = BuildServerEntry();
			string[] array = Consts.MCP.Server.BodyPathSegments(base.BodyPath);
			JsonObject jsonObject = new JsonObject { ["Feeder-MCP"] = value };
			for (int num = array.Length - 1; num >= 0; num--)
			{
				jsonObject = new JsonObject { [array[num]] = jsonObject };
			}
			return jsonObject.ToString();
		}
	}

	public JsonAiAgentConfig(string name, string configPath, string bodyPath = "mcpServers", ILogger? logger = null)
		: base(name, configPath, bodyPath, logger)
	{
	}

	public JsonAiAgentConfig SetProperty(string key, JsonNode value, bool requiredForConfiguration = false, ValueComparisonMode comparison = ValueComparisonMode.Exact)
	{
		_properties[key] = (value, requiredForConfiguration, comparison);
		return this;
	}

	public JsonAiAgentConfig SetPropertyToRemove(string key)
	{
		_propertiesToRemove.Add(key);
		return this;
	}

	public new JsonAiAgentConfig AddIdentityKey(string key)
	{
		base.AddIdentityKey(key);
		return this;
	}

	public override void ApplyHttpAuthorization(bool isRequired, string? token)
	{
		if (isRequired && !string.IsNullOrEmpty(token))
		{
			SetProperty("headers", new JsonObject { ["Authorization"] = JsonValue.Create("Bearer " + token) }, requiredForConfiguration: true);
		}
		else
		{
			SetPropertyToRemove("headers");
		}
	}

	public override void ApplyStdioAuthorization(bool isRequired, string? token)
	{
		SetPropertyToRemove("headers");
		if (!_properties.TryGetValue("args", out (JsonNode, bool, ValueComparisonMode) value) || !(value.Item1 is JsonArray jsonArray))
		{
			return;
		}
		string text = "token=";
		JsonArray jsonArray2 = new JsonArray();
		foreach (JsonNode item in jsonArray)
		{
			if (!(item is JsonValue jsonValue) || !jsonValue.TryGetValue<string>(out string value2) || !value2.StartsWith(text, StringComparison.Ordinal))
			{
				jsonArray2.Add((item != null) ? JsonNode.Parse(item.ToJsonString()) : null);
			}
		}
		if (isRequired && !string.IsNullOrEmpty(token))
		{
			jsonArray2.Add(JsonValue.Create(text + token));
		}
		SetProperty("args", jsonArray2, value.Item2, value.Item3);
	}

	public override bool Configure()
	{
		if (string.IsNullOrEmpty(base.ConfigPath))
		{
			return false;
		}
		_logger?.LogInformation("Configuring MCP client with path: {ConfigPath}, bodyPath: {BodyPath}", base.ConfigPath, base.BodyPath);
		try
		{
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
			string json = File.ReadAllText(base.ConfigPath);
			JsonObject jsonObject = null;
			try
			{
				jsonObject = JsonNode.Parse(json)?.AsObject();
				if (jsonObject == null)
				{
					throw new Exception("Config file is not a valid JSON object.");
				}
			}
			catch
			{
				// the file is shared with other tools: never replace one we cannot read
				_logger?.LogWarning("Config file {ConfigPath} is not a valid JSON object and was left untouched.", base.ConfigPath);
				return false;
			}
			string[] pathSegments = Consts.MCP.Server.BodyPathSegments(base.BodyPath);
			JsonObject jsonObject2 = EnsureJsonPathExists(jsonObject, pathSegments);
			string[] deprecatedMcpServerNames = AiAgentConfig.DeprecatedMcpServerNames;
			foreach (string propertyName in deprecatedMcpServerNames)
			{
				jsonObject2.Remove(propertyName);
			}
			RemoveDuplicateServerEntries(jsonObject2);
			JsonObject jsonObject3 = jsonObject2["Feeder-MCP"]?.AsObject();
			JsonObject jsonObject4 = (JsonObject)((jsonObject3 != null) ? jsonObject3 : (jsonObject2["Feeder-MCP"] = new JsonObject()));
			foreach (string item in _propertiesToRemove)
			{
				jsonObject4.Remove(item);
			}
			foreach (string item2 in _properties.Keys.OrderBy<string, string>((string k) => k, StringComparer.Ordinal))
			{
				string text = _properties[item2].value.ToJsonString();
				JsonNode value = ((text != null) ? JsonNode.Parse(text) : null);
				jsonObject4[item2] = value;
			}
			WriteAllTextAtomic(base.ConfigPath, jsonObject.ToJsonString(WriteOptions));
			return IsConfigured();
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error reading config file: {Message}", ex.Message);
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
			JsonObject jsonObject = JsonNode.Parse(File.ReadAllText(base.ConfigPath))?.AsObject();
			if (jsonObject == null)
			{
				return false;
			}
			string[] pathSegments = Consts.MCP.Server.BodyPathSegments(base.BodyPath);
			JsonObject jsonObject2 = NavigateToJsonPath(jsonObject, pathSegments);
			if (jsonObject2 == null)
			{
				return false;
			}
			bool flag = false;
			if (jsonObject2["Feeder-MCP"] != null)
			{
				jsonObject2.Remove("Feeder-MCP");
				flag = true;
			}
			string[] deprecatedMcpServerNames = AiAgentConfig.DeprecatedMcpServerNames;
			foreach (string propertyName in deprecatedMcpServerNames)
			{
				if (jsonObject2[propertyName] != null)
				{
					jsonObject2.Remove(propertyName);
					flag = true;
				}
			}
			List<string> list = FindDuplicateServerEntryKeys(jsonObject2);
			if (list.Count > 0)
			{
				foreach (string item in list)
				{
					jsonObject2.Remove(item);
				}
				flag = true;
			}
			if (!flag)
			{
				return false;
			}
			WriteAllTextAtomic(base.ConfigPath, jsonObject.ToJsonString(WriteOptions));
			return true;
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error unconfiguring MCP client: {Message}", ex.Message);
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
			string text = File.ReadAllText(base.ConfigPath);
			if (string.IsNullOrWhiteSpace(text))
			{
				return false;
			}
			JsonObject jsonObject = JsonNode.Parse(text)?.AsObject();
			if (jsonObject == null)
			{
				return false;
			}
			string[] pathSegments = Consts.MCP.Server.BodyPathSegments(base.BodyPath);
			JsonObject jsonObject2 = NavigateToJsonPath(jsonObject, pathSegments);
			if (jsonObject2 == null)
			{
				return false;
			}
			if (jsonObject2["Feeder-MCP"] != null)
			{
				return true;
			}
			string[] deprecatedMcpServerNames = AiAgentConfig.DeprecatedMcpServerNames;
			foreach (string propertyName in deprecatedMcpServerNames)
			{
				if (jsonObject2[propertyName] != null)
				{
					return true;
				}
			}
			return FindDuplicateServerEntryKeys(jsonObject2).Count > 0;
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error reading config file: {Message}", ex.Message);
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
			string text = File.ReadAllText(base.ConfigPath);
			if (string.IsNullOrWhiteSpace(text))
			{
				return false;
			}
			JsonObject jsonObject = JsonNode.Parse(text)?.AsObject();
			if (jsonObject == null)
			{
				return false;
			}
			string[] pathSegments = Consts.MCP.Server.BodyPathSegments(base.BodyPath);
			JsonObject jsonObject2 = NavigateToJsonPath(jsonObject, pathSegments);
			if (jsonObject2 == null)
			{
				return false;
			}
			JsonNode jsonNode = jsonObject2["Feeder-MCP"];
			if (jsonNode == null)
			{
				return false;
			}
			return AreRequiredPropertiesMatching(jsonNode) && !HasPropertiesToRemove(jsonNode);
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error reading config file: {Message}", ex.Message);
			return false;
		}
	}

	private JsonObject BuildServerEntry()
	{
		JsonObject jsonObject = new JsonObject();
		foreach (string item in _properties.Keys.OrderBy<string, string>((string k) => k, StringComparer.Ordinal))
		{
			string text = _properties[item].value.ToJsonString();
			JsonNode value = ((text != null) ? JsonNode.Parse(text) : null);
			jsonObject[item] = value;
		}
		return jsonObject;
	}

	private bool AreRequiredPropertiesMatching(JsonNode? serverEntry)
	{
		if (serverEntry == null)
		{
			return false;
		}
		foreach (KeyValuePair<string, (JsonNode, bool, ValueComparisonMode)> property in _properties)
		{
			if (property.Value.Item2)
			{
				JsonNode jsonNode = serverEntry[property.Key];
				if (jsonNode == null)
				{
					return false;
				}
				if (!AreJsonValuesEquivalent(property.Value.Item3, property.Value.Item1, jsonNode))
				{
					return false;
				}
			}
		}
		return true;
	}

	private static bool AreJsonValuesEquivalent(ValueComparisonMode comparison, JsonNode expected, JsonNode actual)
	{
		if (comparison == ValueComparisonMode.Path && TryGetStringValue(expected, out string value) && TryGetStringValue(actual, out string value2))
		{
			return NormalizePath(value) == NormalizePath(value2);
		}
		if (comparison == ValueComparisonMode.Url && TryGetStringValue(expected, out string value3) && TryGetStringValue(actual, out string value4))
		{
			return string.Equals(NormalizeUrl(value3), NormalizeUrl(value4), StringComparison.OrdinalIgnoreCase);
		}
		return expected.ToJsonString() == actual.ToJsonString();
	}

	private static bool TryGetStringValue(JsonNode node, out string value)
	{
		if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out string value2) && value2 != null)
		{
			value = value2;
			return true;
		}
		value = null;
		return false;
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

	private bool HasPropertiesToRemove(JsonNode? serverEntry)
	{
		if (serverEntry == null || _propertiesToRemove.Count == 0)
		{
			return false;
		}
		return _propertiesToRemove.Any((string key) => serverEntry[key] != null);
	}

	private static JsonObject? NavigateToJsonPath(JsonObject rootObj, string[] pathSegments)
	{
		JsonObject jsonObject = rootObj;
		foreach (string propertyName in pathSegments)
		{
			if (jsonObject == null)
			{
				return null;
			}
			jsonObject = jsonObject[propertyName]?.AsObject();
		}
		return jsonObject;
	}

	private List<string> FindDuplicateServerEntryKeys(JsonObject targetObj)
	{
		Dictionary<string, (JsonNode, ValueComparisonMode)> dictionary = new Dictionary<string, (JsonNode, ValueComparisonMode)>();
		foreach (string identityKey in _identityKeys)
		{
			if (_properties.TryGetValue(identityKey, out (JsonNode, bool, ValueComparisonMode) value))
			{
				dictionary[identityKey] = (value.Item1, value.Item3);
			}
		}
		if (dictionary.Count == 0)
		{
			return new List<string>();
		}
		List<string> list = new List<string>();
		foreach (KeyValuePair<string, JsonNode> item in targetObj)
		{
			if (item.Key == "Feeder-MCP")
			{
				continue;
			}
			JsonObject jsonObject = item.Value?.AsObject();
			if (jsonObject == null)
			{
				continue;
			}
			foreach (KeyValuePair<string, (JsonNode, ValueComparisonMode)> item2 in dictionary)
			{
				JsonNode jsonNode = jsonObject[item2.Key];
				if (jsonNode != null && AreJsonValuesEquivalent(item2.Value.Item2, item2.Value.Item1, jsonNode))
				{
					list.Add(item.Key);
					break;
				}
			}
		}
		return list;
	}

	private void RemoveDuplicateServerEntries(JsonObject targetObj)
	{
		foreach (string item in FindDuplicateServerEntryKeys(targetObj))
		{
			targetObj.Remove(item);
		}
	}

	private static JsonObject EnsureJsonPathExists(JsonObject rootObj, string[] pathSegments)
	{
		JsonObject jsonObject = rootObj;
		foreach (string propertyName in pathSegments)
		{
			JsonObject jsonObject2 = jsonObject[propertyName]?.AsObject();
			if (jsonObject2 != null)
			{
				jsonObject = jsonObject2;
				continue;
			}
			JsonObject jsonObject3 = (JsonObject)(jsonObject[propertyName] = new JsonObject());
			jsonObject = jsonObject3;
		}
		return jsonObject;
	}
}
}
