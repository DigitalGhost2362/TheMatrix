using System.Collections.Generic;
using System.Linq;
using Feeder.ToolHost.Skills;
using Feeder.Logging;

namespace Feeder.ToolHost
{
public class SkillContentCollection : Dictionary<string, ISkillContent>
{
	private readonly ILogger? _logger;

	public SkillContentCollection(ILogger? logger = null)
	{
		_logger = logger;
		_logger?.LogTrace("Ctor.");
	}

	public SkillContentCollection Add(IEnumerable<SkillMemberData> fields)
	{
		foreach (SkillMemberData item in fields.Where((SkillMemberData f) => !string.IsNullOrEmpty(f.Attribute?.Name)))
		{
			AiSkillAttribute attribute = item.Attribute;
			bool flag = attribute.EnabledValue ?? true;
			if (!flag)
			{
				_logger?.LogDebug("Skill '{name}' is disabled, skipping.", attribute.Name);
			}
			else
			{
				base[attribute.Name] = new SkillContent(attribute.Name, attribute.Description, item.Content, flag, attribute.SkillDescription);
			}
		}
		return this;
	}
}
}
