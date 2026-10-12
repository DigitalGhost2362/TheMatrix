using System.Reflection;
using System.Text;
using Feeder.Reflect.Model;
using Feeder.Reflect.Utils;

namespace Feeder.Reflect
{
public static class ExtensionsSerializedMemberList
{
	public static bool IsValidTypeNames(this SerializedMemberList? parameters, string fieldName, out string? error)
	{
		if (parameters == null || parameters.Count == 0)
		{
			error = null;
			return true;
		}
		bool result = true;
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < parameters.Count; i++)
		{
			SerializedMember serializedMember = parameters[i];
			if (string.IsNullOrEmpty(serializedMember.typeName))
			{
				stringBuilder.AppendLine(string.Format("[Error] {0}[{1}].{2} is empty. Please specify the '{3}' properly.", fieldName, i, "typeName", "name"));
				result = false;
			}
			else if (TypeUtils.GetType(serializedMember.typeName) == null)
			{
				stringBuilder.AppendLine(string.Format("[Error] {0}[{1}].{2} type '{3}' not found. Please specify the '{4}' properly.", fieldName, i, "typeName", serializedMember.typeName, "name"));
				result = false;
			}
		}
		error = stringBuilder.ToString();
		if (string.IsNullOrEmpty(error))
		{
			error = null;
		}
		return result;
	}

	public static void EnhanceNames(this SerializedMemberList? parameters, MethodInfo method)
	{
		if (parameters == null || parameters.Count == 0)
		{
			return;
		}
		ParameterInfo[] parameters2 = method.GetParameters();
		for (int i = 0; i < parameters.Count && i < parameters2.Length; i++)
		{
			SerializedMember serializedMember = parameters[i];
			if (string.IsNullOrEmpty(serializedMember.name))
			{
				ParameterInfo parameterInfo = parameters2[i];
				serializedMember.name = parameterInfo.Name;
			}
		}
	}

	public static void EnhanceTypes(this SerializedMemberList? parameters, MethodInfo method)
	{
		if (parameters == null || parameters.Count == 0)
		{
			return;
		}
		ParameterInfo[] parameters2 = method.GetParameters();
		for (int i = 0; i < parameters.Count && i < parameters2.Length; i++)
		{
			SerializedMember serializedMember = parameters[i];
			if (string.IsNullOrEmpty(serializedMember.typeName))
			{
				string text = parameters2[i]?.ParameterType?.GetTypeId();
				if (text != null)
				{
					serializedMember.typeName = text;
				}
			}
		}
	}
}
}
