using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Feeder.Reflect.Model;
using Feeder.Reflect.Utils;

namespace Feeder.Reflect
{
public static class ExtensionsMethodInfo
{
	public static MethodInfo? FilterByParameters(this IEnumerable<MethodInfo> methods, SerializedMemberList? parameters = null)
	{
		if (parameters == null || parameters.Count == 0)
		{
			return methods.FirstOrDefault((MethodInfo m) => m.GetParameters().Length == 0);
		}
		return methods.FirstOrDefault(delegate(MethodInfo method)
		{
			ParameterInfo[] parameters2 = method.GetParameters();
			for (int i = 0; i < parameters2.Length; i++)
			{
				ParameterInfo parameterInfo = parameters2[i];
				if (i >= parameters.Count)
				{
					if (parameterInfo.IsOptional)
					{
						break;
					}
					return false;
				}
				SerializedMember serializedMember = parameters[i];
				if (parameterInfo.Name != serializedMember.name || parameterInfo.ParameterType != TypeUtils.GetType(serializedMember.typeName))
				{
					return false;
				}
			}
			return true;
		});
	}
}
}
