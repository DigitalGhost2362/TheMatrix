using System.Collections.Generic;

namespace Feeder.ToolHost.Skills
{
public interface ISkillFileGenerator
{
	bool Generate(IEnumerable<IRunTool> tools, string skillsPath, string host);

	bool Delete(IEnumerable<IRunTool> tools, string skillsPath);

	bool Generate(IEnumerable<ISkillContent> skills, string skillsPath);

	bool Delete(IEnumerable<ISkillContent> skills, string skillsPath);
}
}
