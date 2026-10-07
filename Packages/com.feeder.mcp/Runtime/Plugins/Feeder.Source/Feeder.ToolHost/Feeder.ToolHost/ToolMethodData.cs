using System;
using System.Reflection;

namespace Feeder.ToolHost
{
public class ToolMethodData
{
	public string Name => Attribute.Name;

	public Type ClassType { get; set; }

	public MethodInfo MethodInfo { get; set; }

	public AiToolAttribute Attribute { get; set; }

	public ToolMethodData(Type classType, MethodInfo methodInfo, AiToolAttribute attribute)
	{
		ClassType = classType;
		MethodInfo = methodInfo;
		Attribute = attribute;
	}
}
}
