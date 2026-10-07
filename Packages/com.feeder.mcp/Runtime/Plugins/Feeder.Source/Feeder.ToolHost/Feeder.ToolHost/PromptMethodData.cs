using System;
using System.Reflection;

namespace Feeder.ToolHost
{
public class PromptMethodData
{
	public string Name => Attribute.Name;

	public Type ClassType { get; set; }

	public MethodInfo MethodInfo { get; set; }

	public AiPromptAttribute Attribute { get; set; }

	public PromptMethodData(Type classType, MethodInfo methodInfo, AiPromptAttribute attribute)
	{
		ClassType = classType;
		MethodInfo = methodInfo;
		Attribute = attribute;
	}
}
}
