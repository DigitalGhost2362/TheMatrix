using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Feeder.Json;
using Feeder.Json.Nodes;
using Feeder.Json.Serialization.Metadata;
using Feeder.Logging;

namespace Feeder.ToolHost.Skills
{
public class SkillFileGenerator : ISkillFileGenerator
{
	// tool skills live in "matrix-<tool>", so they never share a folder with a skill another tool wrote
	public const string ToolSkillPrefix = "matrix-";

	// every tool skill this package ever wrote points at its own setup skill;
	// only a SKILL.md carrying this text is overwritten or deleted
	public const string OwnSkillMark = "/feeder-mcp-initial-setup skill";

	private readonly ILogger? _logger;

	private static readonly UTF8Encoding _utf8NoBom;

	private static readonly JsonSerializerOptions _prettyJsonOptions;

	public virtual bool IncludeAuthorizationExample { get; } = true;

	public virtual bool IncludeParameterTable { get; } = true;

	public virtual bool IncludeDescriptionBody { get; } = true;

	public virtual bool IncludeInputJsonSchema { get; } = true;

	public virtual bool IncludeInputSchemaPropertyDescriptions { get; } = true;

	public virtual bool IncludeOutputSection { get; } = true;

	public virtual SkillAdditionalContentPosition AdditionalContentPosition { get; } = SkillAdditionalContentPosition.End;

	public virtual int MaxSkillDescriptionLength { get; } = 1024;

	public SkillFileGenerator(ILogger? logger = null)
	{
		_logger = logger;
	}

	public virtual string? GetAdditionalContent(IRunTool tool)
	{
		return null;
	}

	public virtual bool Generate(IEnumerable<IRunTool> tools, string skillsPath, string host)
	{
		if (tools == null)
		{
			_logger?.LogWarning("{class}.{method}: tools collection is null, skipping.", "SkillFileGenerator", "Generate");
			return false;
		}
		try
		{
			Directory.CreateDirectory(skillsPath);
		}
		catch (Exception exception)
		{
			_logger?.LogError(exception, "{class}.{method}: Failed to create skills directory '{dir}'.", "SkillFileGenerator", "Generate", skillsPath);
			return false;
		}
		List<IRunTool> list = new List<IRunTool>();
		foreach (IRunTool tool in tools)
		{
			if (tool != null)
			{
				list.Add(tool);
			}
		}
		Dictionary<string, string> dictionary = BuildNameMap(list, "Generate");
		bool result = true;
		foreach (IRunTool item in list)
		{
			RemoveLegacyToolSkill(skillsPath, dictionary[item.Name]);
			if (!GenerateFor(item, skillsPath, host, ToolSkillPrefix + dictionary[item.Name]))
			{
				result = false;
			}
		}
		return result;
	}

	public virtual bool Delete(IEnumerable<IRunTool> tools, string skillsPath)
	{
		if (tools == null)
		{
			_logger?.LogWarning("{class}.{method}: tools collection is null, skipping.", "SkillFileGenerator", "Delete");
			return false;
		}
		if (!Directory.Exists(skillsPath))
		{
			return true;
		}
		List<IRunTool> list = new List<IRunTool>();
		foreach (IRunTool tool in tools)
		{
			if (tool != null)
			{
				list.Add(tool);
			}
		}
		Dictionary<string, string> dictionary = BuildNameMap(list, "Delete");
		bool result = true;
		foreach (IRunTool item in list)
		{
			RemoveLegacyToolSkill(skillsPath, dictionary[item.Name]);
			string text = Path.Combine(skillsPath, ToolSkillPrefix + dictionary[item.Name]);
			if (Directory.Exists(text))
			{
				try
				{
					if (DeleteOwnSkill(text))
						_logger?.LogDebug("{class}.{method}: Deleted skill directory for tool '{tool}' → '{path}'.", "SkillFileGenerator", "Delete", item.Name, text);
				}
				catch (Exception exception)
				{
					_logger?.LogError(exception, "{class}.{method}: Failed to delete skill directory for tool '{tool}' at '{path}'.", "SkillFileGenerator", "Delete", item.Name, text);
					result = false;
				}
			}
		}
		return result;
	}

	// a tool removed from the package leaves its matrix-<tool> folder behind; delete the ones this package wrote
	public virtual void RemoveOrphanedToolSkills(IEnumerable<IRunTool> registeredTools, string skillsPath)
	{
		if (registeredTools == null || !Directory.Exists(skillsPath))
		{
			return;
		}
		List<IRunTool> list = new List<IRunTool>();
		foreach (IRunTool tool in registeredTools)
		{
			if (tool != null)
			{
				list.Add(tool);
			}
		}
		HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (IRunTool tool in list)
		{
			known.Add(ToolSkillPrefix + SanitizeSkillName(tool.Name));
		}
		foreach (string folderName in BuildNameMap(list, "RemoveOrphanedToolSkills").Values)
		{
			known.Add(ToolSkillPrefix + folderName);
		}
		foreach (string folder in Directory.GetDirectories(skillsPath, ToolSkillPrefix + "*"))
		{
			if (known.Contains(Path.GetFileName(folder)))
			{
				continue;
			}
			try
			{
				if (DeleteOwnSkill(folder))
				{
					_logger?.LogInformation("{class}: removed '{path}': no registered tool writes it any more.", "SkillFileGenerator", folder);
				}
			}
			catch (Exception exception)
			{
				_logger?.LogWarning(exception, "{class}: could not remove the orphaned skill '{path}'.", "SkillFileGenerator", folder);
			}
		}
	}

	public virtual bool Generate(IEnumerable<ISkillContent> skills, string skillsPath)
	{
		if (skills == null)
		{
			_logger?.LogWarning("{class}.{method}: skills collection is null, skipping.", "SkillFileGenerator", "Generate");
			return false;
		}
		try
		{
			Directory.CreateDirectory(skillsPath);
		}
		catch (Exception exception)
		{
			_logger?.LogError(exception, "{class}.{method}: Failed to create skills directory '{dir}'.", "SkillFileGenerator", "Generate", skillsPath);
			return false;
		}
		List<ISkillContent> list = new List<ISkillContent>();
		foreach (ISkillContent skill in skills)
		{
			if (skill != null)
			{
				list.Add(skill);
			}
		}
		Dictionary<string, string> dictionary = BuildSkillNameMap(list, "Generate");
		bool result = true;
		foreach (ISkillContent item in list)
		{
			if (!GenerateForSkillContent(item, skillsPath, dictionary[item.Name]))
			{
				result = false;
			}
		}
		return result;
	}

	public virtual bool Delete(IEnumerable<ISkillContent> skills, string skillsPath)
	{
		if (skills == null)
		{
			_logger?.LogWarning("{class}.{method}: skills collection is null, skipping.", "SkillFileGenerator", "Delete");
			return false;
		}
		if (!Directory.Exists(skillsPath))
		{
			return true;
		}
		List<ISkillContent> list = new List<ISkillContent>();
		foreach (ISkillContent skill in skills)
		{
			if (skill != null)
			{
				list.Add(skill);
			}
		}
		Dictionary<string, string> dictionary = BuildSkillNameMap(list, "Delete");
		bool result = true;
		foreach (ISkillContent item in list)
		{
			string text = Path.Combine(skillsPath, dictionary[item.Name]);
			if (Directory.Exists(text))
			{
				try
				{
					Directory.Delete(text, recursive: true);
					_logger?.LogDebug("{class}.{method}: Deleted skill directory for skill '{skill}' → '{path}'.", "SkillFileGenerator", "Delete", item.Name, text);
				}
				catch (Exception exception)
				{
					_logger?.LogError(exception, "{class}.{method}: Failed to delete skill directory for skill '{skill}' at '{path}'.", "SkillFileGenerator", "Delete", item.Name, text);
					result = false;
				}
			}
		}
		return result;
	}

	protected virtual bool GenerateForSkillContent(ISkillContent skill, string skillsDir, string skillName)
	{
		skillName = SanitizeSkillName(skillName);
		string text = Path.Combine(skillsDir, skillName);
		string text2 = Path.Combine(text, "SKILL.md");
		try
		{
			Directory.CreateDirectory(text);
			string contents = BuildSkillContentMarkdown(skill, skillName);
			File.WriteAllText(text2, contents, _utf8NoBom);
			_logger?.LogDebug("{class}.{method}: Skill file written for skill '{skill}' → '{path}'.", "SkillFileGenerator", "GenerateForSkillContent", skill.Name, text2);
			return true;
		}
		catch (Exception exception)
		{
			_logger?.LogError(exception, "{class}.{method}: Failed to write skill file for skill '{skill}' at '{path}'.", "SkillFileGenerator", "GenerateForSkillContent", skill.Name, text2);
			return false;
		}
	}

	protected virtual string BuildSkillContentMarkdown(ISkillContent skill, string skillName)
	{
		StringBuilder stringBuilder = new StringBuilder();
		string value = ResolveYamlDescription(fullDescription: skill.Description ?? string.Empty, skillDescription: skill.SkillDescription, toolOrSkillName: skill.Name);
		stringBuilder.AppendLine("---");
		stringBuilder.AppendLine("name: " + EscapeYaml(skillName));
		stringBuilder.AppendLine("description: " + EscapeYaml(value));
		stringBuilder.AppendLine("---");
		stringBuilder.Append(skill.Content);
		return stringBuilder.ToString();
	}

	protected virtual Dictionary<string, string> BuildSkillNameMap(List<ISkillContent> skills, string callerName)
	{
		Dictionary<string, List<ISkillContent>> dictionary = new Dictionary<string, List<ISkillContent>>(StringComparer.Ordinal);
		foreach (ISkillContent skill in skills)
		{
			string key = SanitizeSkillName(skill.Name);
			if (!dictionary.TryGetValue(key, out var value))
			{
				value = (dictionary[key] = new List<ISkillContent>());
			}
			value.Add(skill);
		}
		Dictionary<string, string> dictionary2 = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (KeyValuePair<string, List<ISkillContent>> item in dictionary)
		{
			if (item.Value.Count == 1)
			{
				dictionary2[item.Value[0].Name] = item.Key;
				continue;
			}
			_logger?.LogWarning("{class}.{method}: Skills [{skills}] all sanitize to '{sanitized}'. Appending hash suffixes to avoid directory collisions.", "SkillFileGenerator", callerName, string.Join(", ", item.Value.ConvertAll((ISkillContent s) => "'" + s.Name + "'")), item.Key);
			foreach (ISkillContent item2 in item.Value)
			{
				dictionary2[item2.Name] = item.Key + "-" + StableShortHash(item2.Name);
			}
		}
		return dictionary2;
	}

	protected virtual Dictionary<string, string> BuildNameMap(List<IRunTool> tools, string callerName)
	{
		Dictionary<string, List<IRunTool>> dictionary = new Dictionary<string, List<IRunTool>>(StringComparer.Ordinal);
		foreach (IRunTool tool in tools)
		{
			string key = SanitizeSkillName(tool.Name);
			if (!dictionary.TryGetValue(key, out var value))
			{
				value = (dictionary[key] = new List<IRunTool>());
			}
			value.Add(tool);
		}
		Dictionary<string, string> dictionary2 = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (KeyValuePair<string, List<IRunTool>> item in dictionary)
		{
			if (item.Value.Count == 1)
			{
				dictionary2[item.Value[0].Name] = item.Key;
				continue;
			}
			_logger?.LogWarning("{class}.{method}: Tools [{tools}] all sanitize to '{sanitized}'. Appending hash suffixes to avoid directory collisions.", "SkillFileGenerator", callerName, string.Join(", ", item.Value.ConvertAll((IRunTool t) => "'" + t.Name + "'")), item.Key);
			foreach (IRunTool item2 in item.Value)
			{
				dictionary2[item2.Name] = item.Key + "-" + StableShortHash(item2.Name);
			}
		}
		return dictionary2;
	}

	protected virtual bool GenerateFor(IRunTool tool, string skillsDir, string host, string skillName)
	{
		skillName = SanitizeSkillName(skillName);
		string text = Path.Combine(skillsDir, skillName);
		string text2 = Path.Combine(text, "SKILL.md");
		if (File.Exists(text2) && !IsOwnSkillFile(text2))
		{
			_logger?.LogDebug("{class}.{method}: '{path}' was not written by this package, left as is.", "SkillFileGenerator", "GenerateFor", text2);
			return true;
		}
		try
		{
			Directory.CreateDirectory(text);
			string contents = BuildMarkdown(tool, skillName, host);
			File.WriteAllText(text2, contents, _utf8NoBom);
			_logger?.LogDebug("{class}.{method}: Skill file written for tool '{tool}' → '{path}'.", "SkillFileGenerator", "GenerateFor", tool.Name, text2);
			return true;
		}
		catch (Exception exception)
		{
			_logger?.LogError(exception, "{class}.{method}: Failed to write skill file for tool '{tool}' at '{path}'.", "SkillFileGenerator", "GenerateFor", tool.Name, text2);
			return false;
		}
	}

	public static bool IsOwnSkillFile(string skillFile)
	{
		try
		{
			return File.ReadAllText(skillFile).Contains(OwnSkillMark);
		}
		catch (Exception)
		{
			return false;
		}
	}

	// deletes folder/SKILL.md only when this package wrote it, then the folder once it is empty; true when deleted
	public static bool DeleteOwnSkill(string folder)
	{
		string file = Path.Combine(folder, "SKILL.md");
		if (!File.Exists(file) || !IsOwnSkillFile(file))
		{
			return false;
		}
		File.Delete(file);
		if (Directory.GetFileSystemEntries(folder).Length == 0)
		{
			Directory.Delete(folder);
		}
		return true;
	}

	// older versions wrote tool skills without the prefix; such a folder is removed only when it is ours
	private void RemoveLegacyToolSkill(string skillsDir, string folderName)
	{
		string folder = Path.Combine(skillsDir, folderName);
		try
		{
			if (DeleteOwnSkill(folder))
			{
				_logger?.LogInformation("{class}: removed '{path}', written without the '{prefix}' prefix by an older version.", "SkillFileGenerator", folder, ToolSkillPrefix);
			}
		}
		catch (Exception exception)
		{
			_logger?.LogWarning(exception, "{class}: could not remove the old skill '{path}'.", "SkillFileGenerator", folder);
		}
	}

	protected virtual string BuildMarkdown(IRunTool tool, string skillName, string host)
	{
		host = host.TrimEnd('/');
		StringBuilder stringBuilder = new StringBuilder();
		string text = tool.Title ?? tool.Name;
		string text2 = tool.Description ?? string.Empty;
		string value = ResolveYamlDescription(tool.SkillDescription, text2, tool.Name);
		string additionalContent = GetAdditionalContent(tool);
		stringBuilder.AppendLine("---");
		stringBuilder.AppendLine("name: " + EscapeYaml(skillName));
		stringBuilder.AppendLine("description: " + EscapeYaml(value));
		stringBuilder.AppendLine("---");
		stringBuilder.AppendLine();
		BuildFrontMatterNotes(stringBuilder);
		stringBuilder.AppendLine("# " + text);
		stringBuilder.AppendLine();
		if (IncludeDescriptionBody && !string.IsNullOrWhiteSpace(text2))
		{
			stringBuilder.AppendLine(text2);
			stringBuilder.AppendLine();
		}
		if (!string.IsNullOrWhiteSpace(tool.SkillBody))
		{
			stringBuilder.AppendLine(tool.SkillBody);
			stringBuilder.AppendLine();
		}
		BuildDescriptionNotes(stringBuilder);
		AppendAdditionalContent(stringBuilder, additionalContent, SkillAdditionalContentPosition.AfterTitle);
		stringBuilder.AppendLine("## How to Call");
		stringBuilder.AppendLine();
		BuildHowToCallHeading(stringBuilder);
		BuildHowToCallIntroNotes(stringBuilder);
		string inputExample = BuildInputExample(tool.InputSchema);
		BuildToolCommand(stringBuilder, tool, host, inputExample);
		BuildInputExampleNotes(stringBuilder);
		if (IncludeAuthorizationExample)
		{
			BuildToolCommandWithAuth(stringBuilder, tool, host, inputExample);
			BuildInputAuthorizationNotes(stringBuilder);
		}
		AppendAdditionalContent(stringBuilder, additionalContent, SkillAdditionalContentPosition.AfterHowToCall);
		stringBuilder.AppendLine("## Input");
		stringBuilder.AppendLine();
		BuildInputSectionNotes(stringBuilder);
		if (IncludeParameterTable)
		{
			AppendParameterTable(stringBuilder, tool.InputSchema);
			stringBuilder.AppendLine();
			BuildParameterTableNotes(stringBuilder);
		}
		if (IncludeInputJsonSchema)
		{
			BuildInputJsonSchemaBlock(stringBuilder, tool);
			BuildInputJsonSchemaNotes(stringBuilder);
		}
		AppendAdditionalContent(stringBuilder, additionalContent, SkillAdditionalContentPosition.AfterInput);
		if (IncludeOutputSection)
		{
			stringBuilder.AppendLine("## Output");
			stringBuilder.AppendLine();
			BuildOutputSectionNotes(stringBuilder);
			BuildOutputSchemaBlock(stringBuilder, tool);
			BuildOutputSchemaNotes(stringBuilder);
			stringBuilder.AppendLine();
		}
		AppendAdditionalContent(stringBuilder, additionalContent, SkillAdditionalContentPosition.End);
		return stringBuilder.ToString();
	}

	protected virtual void BuildFrontMatterNotes(StringBuilder sb)
	{
	}

	protected virtual void BuildDescriptionNotes(StringBuilder sb)
	{
	}

	protected virtual void BuildHowToCallHeading(StringBuilder sb)
	{
		sb.AppendLine("### HTTP API (Direct Tool Execution)");
		sb.AppendLine();
		sb.AppendLine("Execute this tool directly via the MCP Plugin HTTP API:");
		sb.AppendLine();
	}

	protected virtual void BuildHowToCallIntroNotes(StringBuilder sb)
	{
	}

	protected virtual void BuildToolCommand(StringBuilder sb, IRunTool tool, string host, string inputExample)
	{
		sb.AppendLine("```bash");
		sb.AppendLine("curl -X POST " + host + GetApiRoutePrefix(tool) + "/" + tool.Name + " \\");
		sb.AppendLine("  -H \"Content-Type: application/json\" \\");
		sb.AppendLine("  -d '" + inputExample + "'");
		sb.AppendLine("```");
		sb.AppendLine();
		AppendInputFileHint(sb, tool, host, inputExample);
	}

	protected virtual void AppendInputFileHint(StringBuilder sb, IRunTool tool, string host, string inputExample)
	{
		if (!(inputExample == "{}"))
		{
			sb.AppendLine("> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.");
			sb.AppendLine(">");
			sb.AppendLine("> Or pipe via stdin:");
			sb.AppendLine("> ```bash");
			sb.AppendLine("> curl -X POST " + host + GetApiRoutePrefix(tool) + "/" + tool.Name + " -H \"Content-Type: application/json\" -d @- <<'EOF'");
			sb.AppendLine("> {\"param\": \"value\"}");
			sb.AppendLine("> EOF");
			sb.AppendLine("> ```");
			sb.AppendLine();
		}
	}

	protected virtual void BuildInputExampleNotes(StringBuilder sb)
	{
	}

	protected virtual void BuildToolCommandWithAuth(StringBuilder sb, IRunTool tool, string host, string inputExample)
	{
		sb.AppendLine("#### With Authorization (if required)");
		sb.AppendLine();
		sb.AppendLine("```bash");
		sb.AppendLine("curl -X POST " + host + GetApiRoutePrefix(tool) + "/" + tool.Name + " \\");
		sb.AppendLine("  -H \"Content-Type: application/json\" \\");
		sb.AppendLine("  -H \"Authorization: Bearer YOUR_TOKEN\" \\");
		sb.AppendLine("  -d '" + inputExample + "'");
		sb.AppendLine("```");
		sb.AppendLine();
	}

	protected virtual void BuildInputAuthorizationNotes(StringBuilder sb)
	{
	}

	protected virtual void BuildInputSectionNotes(StringBuilder sb)
	{
	}

	protected virtual void BuildParameterTableNotes(StringBuilder sb)
	{
	}

	protected virtual void BuildInputJsonSchemaBlock(StringBuilder sb, IRunTool tool)
	{
		JsonNode jsonNode = tool.InputSchema;
		if (!IncludeInputSchemaPropertyDescriptions && jsonNode != null)
		{
			jsonNode = StripPropertyDescriptions(jsonNode);
		}
		sb.AppendLine("### Input JSON Schema");
		sb.AppendLine();
		sb.AppendLine("```json");
		sb.AppendLine(PrettyPrintJson(jsonNode));
		sb.AppendLine("```");
		sb.AppendLine();
	}

	protected virtual void BuildInputJsonSchemaNotes(StringBuilder sb)
	{
	}

	protected virtual void BuildOutputSectionNotes(StringBuilder sb)
	{
	}

	protected virtual void BuildOutputSchemaBlock(StringBuilder sb, IRunTool tool)
	{
		if (tool.OutputSchema != null)
		{
			sb.AppendLine("### Output JSON Schema");
			sb.AppendLine();
			sb.AppendLine("```json");
			sb.AppendLine(PrettyPrintJson(tool.OutputSchema));
			sb.AppendLine("```");
		}
		else
		{
			sb.AppendLine("This tool does not return structured output.");
		}
	}

	protected virtual void BuildOutputSchemaNotes(StringBuilder sb)
	{
	}

	protected virtual void AppendParameterTable(StringBuilder sb, JsonNode? inputSchema)
	{
		if (inputSchema == null)
		{
			sb.AppendLine("This tool takes no input parameters.");
			return;
		}
		if (!(inputSchema["properties"] is JsonObject { Count: not 0 } jsonObject))
		{
			sb.AppendLine("This tool takes no input parameters.");
			return;
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (inputSchema["required"] is JsonArray jsonArray)
		{
			foreach (JsonNode item in jsonArray)
			{
				string text = item?.GetValue<string>();
				if (text != null)
				{
					hashSet.Add(text);
				}
			}
		}
		sb.AppendLine("| Name | Type | Required | Description |");
		sb.AppendLine("|------|------|----------|-------------|");
		foreach (KeyValuePair<string, JsonNode> item2 in jsonObject)
		{
			string key = item2.Key;
			JsonObject obj = item2.Value as JsonObject;
			string text2 = obj?["type"]?.GetValue<string>() ?? "any";
			string text3 = obj?["description"]?.GetValue<string>() ?? string.Empty;
			string text4 = (hashSet.Contains(key) ? "Yes" : "No");
			sb.AppendLine("| `" + key + "` | `" + text2 + "` | " + text4 + " | " + text3 + " |");
		}
	}

	protected virtual string BuildInputExample(JsonNode? inputSchema)
	{
		if (inputSchema == null)
		{
			return "{}";
		}
		if (!(inputSchema["properties"] is JsonObject { Count: not 0 } jsonObject))
		{
			return "{}";
		}
		JsonObject jsonObject2 = new JsonObject();
		foreach (KeyValuePair<string, JsonNode> item in jsonObject)
		{
			JsonObject jsonObject3 = item.Value as JsonObject;
			string type = jsonObject3?["type"]?.GetValue<string>() ?? "string";
			jsonObject2[item.Key] = CreateExampleValue(type, jsonObject3);
		}
		return jsonObject2.ToJsonString(_prettyJsonOptions);
	}

	protected virtual string PrettyPrintJson(JsonNode? node)
	{
		if (node == null)
		{
			return "null";
		}
		try
		{
			return node.ToJsonString(_prettyJsonOptions);
		}
		catch (Exception exception)
		{
			_logger?.LogWarning(exception, "{class}.{method}: Failed to pretty-print JSON schema.", "SkillFileGenerator", "PrettyPrintJson");
			return node.ToString();
		}
	}

	protected static string GetApiRoutePrefix(IRunTool tool)
	{
		if (tool.ToolType != McpToolType.System)
		{
			return "/api/tools";
		}
		return "/api/system-tools";
	}

	protected static JsonNode CreateExampleValue(string type, JsonObject? schema)
	{
		// Prefer a concrete, meaningful example over a generic placeholder:
		// - const / enum values from the schema
		// - well-known formats (uri, date-time, email, guid, path)
		// - only fall back to neutral values for plain types.
		if (schema != null)
		{
			if (schema.TryGetPropertyValue("const", out JsonNode? constValue) && constValue != null)
			{
				return constValue.DeepClone();
			}
			if (schema.TryGetPropertyValue("enum", out JsonNode? enumNode) && enumNode is JsonArray enumArray && enumArray.Count > 0)
			{
				return enumArray[0]!.DeepClone();
			}
			// $ref parameters (GameObjectRef, ComponentRef, ObjectRef, AssetObjectRef, ...) resolve to
			// the $defs blocks; emit a minimal realistic object instead of a string placeholder so the
			// model sees the expected payload shape.
			if (schema.TryGetPropertyValue("$ref", out JsonNode? refNode) && refNode != null)
			{
				string refName = refNode.GetValue<string>();
				if (refName.Contains("GameObjectRef") || refName.Contains("ComponentRef") || refName.Contains("ObjectRef") || refName.Contains("AssetObjectRef") || refName.Contains("SceneRef"))
				{
					return new JsonObject { ["instanceID"] = 0 };
				}
				if (refName.Contains("System.Collections.Generic.List") || refName.Contains("System.Collections.Generic.IList") || refName.Contains("System.Collections.Generic.IReadOnlyList"))
				{
					return new JsonArray();
				}
				return new JsonObject();
			}
			if (schema.TryGetPropertyValue("format", out JsonNode? formatNode) && formatNode != null)
			{
				string format = formatNode.GetValue<string>();
				return format switch
				{
					"uri" or "url" or "uri-reference" => JsonValue.Create("https://example.com"),
					"date-time" => JsonValue.Create("2024-01-01T00:00:00Z"),
					"date" => JsonValue.Create("2024-01-01"),
					"time" => JsonValue.Create("00:00:00"),
					"email" => JsonValue.Create("user@example.com"),
					"uuid" or "guid" => JsonValue.Create("00000000-0000-0000-0000-000000000000"),
					"ipv4" => JsonValue.Create("127.0.0.1"),
					"ipv6" => JsonValue.Create("::1"),
					"hostname" => JsonValue.Create("localhost"),
					"regex" => JsonValue.Create(".*"),
					_ => JsonValue.Create("example"),
				};
			}
			if (type == "string" && schema.TryGetPropertyValue("description", out JsonNode? descriptionNode) && descriptionNode != null)
			{
				// A description that quotes a concrete sample (e.g. `"Assets/..."`) is more useful than a placeholder.
				string description = descriptionNode.GetValue<string>();
				if (description.Contains("Sample:", StringComparison.OrdinalIgnoreCase))
				{
					int idx = description.IndexOf("Sample:", StringComparison.OrdinalIgnoreCase);
					string sample = description.Substring(idx + "Sample:".Length).Trim().Trim('"', '\'', '.');
					if (!string.IsNullOrEmpty(sample))
					{
						return JsonValue.Create(sample);
					}
				}
			}
		}
		return type switch
		{
			"integer" => JsonValue.Create(0), 
			"number" => JsonValue.Create(0.0), 
			"boolean" => JsonValue.Create(value: false), 
			"array" => new JsonArray(), 
			"object" => new JsonObject(), 
			"null" => JsonValue.Create((string?)null, (JsonNodeOptions?)null), 
			_ => JsonValue.Create("value"), 
		};
	}

	protected static string SanitizeSkillName(string name)
	{
		StringBuilder stringBuilder = new StringBuilder();
		bool flag = false;
		foreach (char c in name)
		{
			if (char.IsLetterOrDigit(c))
			{
				stringBuilder.Append(char.ToLowerInvariant(c));
				flag = false;
			}
			else if (stringBuilder.Length > 0 && !flag)
			{
				stringBuilder.Append('-');
				flag = true;
			}
		}
		while (stringBuilder.Length > 0 && stringBuilder[stringBuilder.Length - 1] == '-')
		{
			stringBuilder.Length--;
		}
		if (stringBuilder.Length <= 0)
		{
			return "tool-" + StableShortHash(name);
		}
		return stringBuilder.ToString();
	}

	protected static string StableShortHash(string value)
	{
		uint num = 2166136261u;
		byte[] bytes = Encoding.UTF8.GetBytes(value);
		foreach (byte b in bytes)
		{
			num ^= b;
			num *= 16777619;
		}
		return num.ToString("x8");
	}

	protected static string EscapeYaml(string value)
	{
		if (value.Contains('\n'))
		{
			string text = "  ";
			string text2 = value.Replace("\r\n", "\n").Replace("\r", "\n");
			string text3 = string.Join("\n" + text, text2.TrimEnd('\n').Split('\n'));
			return "|-\n" + text + text3;
		}
		if (value.Contains(':') || value.Contains('"'))
		{
			return "\"" + value.Replace("\"", "\\\"") + "\"";
		}
		return value;
	}

	protected virtual string ResolveYamlDescription(string? skillDescription, string fullDescription, string toolOrSkillName)
	{
		int maxSkillDescriptionLength = MaxSkillDescriptionLength;
		if (!string.IsNullOrEmpty(skillDescription))
		{
			if (maxSkillDescriptionLength > 0 && skillDescription.Length > maxSkillDescriptionLength)
			{
				_logger?.LogWarning("{class}: SkillDescription for '{name}' is {len} chars, exceeding the YAML cap of {cap}. Truncating.", "SkillFileGenerator", toolOrSkillName, skillDescription.Length, maxSkillDescriptionLength);
				return TruncateForYaml(skillDescription, maxSkillDescriptionLength);
			}
			return skillDescription;
		}
		if (maxSkillDescriptionLength > 0 && fullDescription.Length > maxSkillDescriptionLength)
		{
			_logger?.LogWarning("{class}: Description for '{name}' is {len} chars, exceeding the YAML cap of {cap}. Truncating for SKILL.md; consider adding [AiSkillDescription] for a concise summary and [AiSkillBody] for long-form content.", "SkillFileGenerator", toolOrSkillName, fullDescription.Length, maxSkillDescriptionLength);
			return TruncateForYaml(fullDescription, maxSkillDescriptionLength);
		}
		return fullDescription;
	}

	protected static string TruncateForYaml(string value, int maxLength)
	{
		if (maxLength <= 0 || value.Length <= maxLength)
		{
			return value;
		}
		int num = maxLength - "…".Length;
		if (num <= 0)
		{
			return value.Substring(0, maxLength);
		}
		int num2 = num;
		int num3 = Math.Max(0, num - 32);
		for (int num4 = num; num4 >= num3; num4--)
		{
			if (char.IsWhiteSpace(value[num4]))
			{
				num2 = num4;
				break;
			}
		}
		int num5 = 0;
		int num6 = -1;
		for (int i = 0; i + 2 < num2; i++)
		{
			if (value[i] == '`' && value[i + 1] == '`' && value[i + 2] == '`')
			{
				num5++;
				num6 = i;
				i += 2;
			}
		}
		if (num5 % 2 == 1 && num6 >= 0)
		{
			num2 = num6;
		}
		if (num2 <= 0)
		{
			num2 = num;
		}
		return value.Substring(0, num2).TrimEnd() + "…";
	}

	private static JsonNode StripPropertyDescriptions(JsonNode schema)
	{
		JsonNode jsonNode = JsonNode.Parse(schema.ToJsonString());
		if (jsonNode["properties"] is JsonObject jsonObject)
		{
			foreach (KeyValuePair<string, JsonNode> item in jsonObject)
			{
				if (item.Value is JsonObject jsonObject2)
				{
					jsonObject2.Remove("description");
				}
			}
		}
		return jsonNode;
	}

	private void AppendAdditionalContent(StringBuilder sb, string? content, SkillAdditionalContentPosition targetPosition)
	{
		if (!string.IsNullOrEmpty(content) && AdditionalContentPosition != SkillAdditionalContentPosition.None && AdditionalContentPosition == targetPosition)
		{
			sb.AppendLine(content);
			sb.AppendLine();
		}
	}

	static SkillFileGenerator()
	{
		_utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
		_prettyJsonOptions = new JsonSerializerOptions
		{
			WriteIndented = true,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
			TypeInfoResolver = (IJsonTypeInfoResolver)new DefaultJsonTypeInfoResolver()
		};
	}
}
}
