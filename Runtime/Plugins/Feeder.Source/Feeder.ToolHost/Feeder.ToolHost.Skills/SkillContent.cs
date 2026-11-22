namespace Feeder.ToolHost.Skills
{
public class SkillContent : ISkillContent
{
	public string Name { get; }

	public string? Description { get; }

	public string? SkillDescription { get; }

	public string Content { get; }

	public bool Enabled { get; }

	public SkillContent(string name, string? description, string content, bool enabled = true, string? skillDescription = null)
	{
		Name = name;
		Description = description;
		SkillDescription = skillDescription;
		Content = content;
		Enabled = enabled;
	}
}
}
