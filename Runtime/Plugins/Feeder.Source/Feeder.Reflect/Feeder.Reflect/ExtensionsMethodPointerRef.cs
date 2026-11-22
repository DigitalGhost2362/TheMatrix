using System.Collections.Generic;
using System.Linq;
using Feeder.Reflect.Model;

namespace Feeder.Reflect
{
public static class ExtensionsMethodPointerRef
{
	public static void EnhanceInputParameters(this MethodRef? methodPointer, SerializedMemberList? parameters = null)
	{
		if (methodPointer == null)
		{
			return;
		}
		if (methodPointer.InputParameters == null)
		{
			List<MethodRef.Parameter> list = (methodPointer.InputParameters = new List<MethodRef.Parameter>());
		}
		if (parameters == null || parameters.Count == 0)
		{
			return;
		}
		foreach (SerializedMember parameter in parameters)
		{
			MethodRef.Parameter parameter2 = methodPointer.InputParameters.FirstOrDefault((MethodRef.Parameter p) => p.Name == parameter.name);
			if (parameter2 == null)
			{
				methodPointer.InputParameters.Add(new MethodRef.Parameter(parameter.typeName, parameter.name));
			}
			else
			{
				parameter2.TypeName = parameter.typeName;
			}
		}
	}
}
}
