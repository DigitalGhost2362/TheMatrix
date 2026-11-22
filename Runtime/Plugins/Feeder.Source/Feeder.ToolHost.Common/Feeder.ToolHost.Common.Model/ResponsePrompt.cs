using System.Collections.Generic;

namespace Feeder.ToolHost.Common.Model
{
public class ResponsePrompt
{
	public string Name { get; set; } = string.Empty;

	public bool Enabled { get; set; } = true;

	public string? Title { get; set; }

	public string? Description { get; set; }

	public List<ResponsePromptArgument>? Arguments { get; set; }

	public ResponsePrompt()
	{
	}

	public ResponsePrompt(string name, bool enabled, string? title, string? description, List<ResponsePromptArgument>? arguments)
	{
		Name = name;
		Enabled = enabled;
		Title = title;
		Description = description;
		Arguments = arguments;
	}
}
}
