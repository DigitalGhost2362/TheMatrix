using System;
using System.Reflection;

namespace Feeder.ToolHost
{
public class ResourceMethodData
{
	public Type ClassType { get; set; }

	public MethodInfo GetContentMethod { get; set; }

	public MethodInfo ListResourcesMethod { get; set; }

	public AiResourceAttribute Attribute { get; set; }

	public ResourceMethodData(Type classType, MethodInfo getContentMethod, MethodInfo listResourcesMethod, AiResourceAttribute attribute)
	{
		ClassType = classType;
		GetContentMethod = getContentMethod;
		ListResourcesMethod = listResourcesMethod;
		Attribute = attribute;
	}
}
}
