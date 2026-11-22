namespace Feeder.ToolHost.AgentConfig
{
public sealed class ConfigurationItem
{
	public ConfigurationItemKind Kind { get; }

	public string Text { get; }

	public string? Url { get; }

	public ConfigurationItem(ConfigurationItemKind kind, string text, string? url = null)
	{
		Kind = kind;
		Text = text;
		Url = url;
	}

	public static ConfigurationItem Description(string text)
	{
		return new ConfigurationItem(ConfigurationItemKind.Description, text);
	}

	public static ConfigurationItem Warning(string text)
	{
		return new ConfigurationItem(ConfigurationItemKind.Warning, text);
	}

	public static ConfigurationItem Alert(string text)
	{
		return new ConfigurationItem(ConfigurationItemKind.Alert, text);
	}

	public static ConfigurationItem ReadOnlyField(string value)
	{
		return new ConfigurationItem(ConfigurationItemKind.ReadOnlyField, value);
	}

	public static ConfigurationItem EditableField(string value)
	{
		return new ConfigurationItem(ConfigurationItemKind.EditableField, value);
	}

	public static ConfigurationItem Link(string label, string url)
	{
		return new ConfigurationItem(ConfigurationItemKind.Link, label, url);
	}
}
}
