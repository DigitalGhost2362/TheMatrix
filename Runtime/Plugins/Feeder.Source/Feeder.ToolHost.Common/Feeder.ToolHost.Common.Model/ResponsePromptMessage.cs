namespace Feeder.ToolHost.Common.Model
{
public class ResponsePromptMessage
{
	public ContentBlock Content { get; set; } = new ContentBlock
	{
		Type = string.Empty
	};

	public Role Role { get; set; }

	public ResponsePromptMessage()
	{
	}

	public ResponsePromptMessage(string message, Role role)
	{
		Content = new ContentBlock
		{
			Type = "text",
			Text = message,
			MimeType = "text/plain"
		};
		Role = role;
	}
}
}
