using System;
using System.Reflection;

namespace Feeder.ToolHost
{
public class SkillMemberData
{
	public string Name => Attribute.Name;

	public Type ClassType { get; set; }

	public MemberInfo MemberInfo { get; set; }

	public AiSkillAttribute Attribute { get; set; }

	public string Content { get; set; }

	public SkillMemberData(Type classType, MemberInfo memberInfo, AiSkillAttribute attribute, string content)
	{
		ClassType = classType;
		MemberInfo = memberInfo;
		Attribute = attribute;
		Content = content;
	}
}
}
