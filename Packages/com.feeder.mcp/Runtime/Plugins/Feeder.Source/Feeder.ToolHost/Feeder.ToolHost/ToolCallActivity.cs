namespace Feeder.ToolHost
{
public sealed class ToolCallActivity
{
	public string Name { get; }

	public bool? ReadOnlyHint { get; }

	public bool? DestructiveHint { get; }

	public bool Finished { get; }

	public ToolCallActivity(string name, bool? readOnlyHint, bool? destructiveHint, bool finished)
	{
		Name = name;
		ReadOnlyHint = readOnlyHint;
		DestructiveHint = destructiveHint;
		Finished = finished;
	}
}
}
