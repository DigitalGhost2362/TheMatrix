namespace Feeder.ToolHost.Skills
{
public interface ISkillContent
{
	string Name { get; }

	string? Description { get; }

	string? SkillDescription { get; }

	string Content { get; }

	bool Enabled { get; }
}
}
