using System;
using System.Reflection;
using Feeder.Json.Serialization.Metadata;

namespace Feeder.Reflect.Utils
{
public static class JsonTypeInfoModifiers
{
	public static void ExcludeObsoleteMembers(JsonTypeInfo typeInfo)
	{
		if ((int)typeInfo.Kind != 1)
		{
			return;
		}
		for (int num = typeInfo.Properties.Count - 1; num >= 0; num--)
		{
			if (IsObsolete(typeInfo.Properties[num]))
			{
				typeInfo.Properties.RemoveAt(num);
			}
		}
	}

	private static bool IsObsolete(JsonPropertyInfo propertyInfo)
	{
		ICustomAttributeProvider attributeProvider = propertyInfo.AttributeProvider;
		if (attributeProvider == null)
		{
			return false;
		}
		if (attributeProvider.IsDefined(typeof(ObsoleteAttribute), inherit: true))
		{
			return true;
		}
		if (attributeProvider is PropertyInfo propertyInfo2)
		{
			MethodInfo? getMethod = propertyInfo2.GetMethod;
			if ((object)getMethod == null || !getMethod.IsDefined(typeof(ObsoleteAttribute), inherit: true))
			{
				return propertyInfo2.SetMethod?.IsDefined(typeof(ObsoleteAttribute), inherit: true) ?? false;
			}
			return true;
		}
		return false;
	}
}
}
