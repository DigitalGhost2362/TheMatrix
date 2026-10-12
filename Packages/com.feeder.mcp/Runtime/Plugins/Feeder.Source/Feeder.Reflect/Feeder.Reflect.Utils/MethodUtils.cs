using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Feeder.Reflect.Utils
{
public static class MethodUtils
{
	private static readonly Type? NullableContextAttributeType = Type.GetType("System.Runtime.CompilerServices.NullableContextAttribute, System.Private.CoreLib");

	private const byte NullableContextOblivious = 0;

	private const byte NullableContextNotNull = 1;

	private const byte NullableContextNullable = 2;

	public static bool IsReturnTypeNullable(MethodInfo methodInfo)
	{
		if (methodInfo == null)
		{
			throw new ArgumentNullException("methodInfo");
		}
		Type returnType = methodInfo.ReturnType;
		bool flag = returnType.IsGenericType && (returnType.GetGenericTypeDefinition() == typeof(Task<>) || returnType.GetGenericTypeDefinition() == typeof(ValueTask<>));
		bool flag2 = false;
		bool flag3 = false;
		Type type = (flag ? returnType.GetGenericArguments()[0] : returnType);
		MethodInfo methodInfo2 = null;
		Type type2 = null;
		if (methodInfo.DeclaringType != null && methodInfo.DeclaringType.IsGenericType && !methodInfo.DeclaringType.IsGenericTypeDefinition)
		{
			MethodInfo method = methodInfo.DeclaringType.GetGenericTypeDefinition().GetMethod(methodInfo.Name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (method != null)
			{
				methodInfo2 = method;
				type2 = method.ReturnType;
			}
		}
		if (methodInfo2 != null && type2 != null)
		{
			Type type3 = type2;
			if (flag && type3.IsGenericType)
			{
				type3 = type3.GetGenericArguments()[0];
			}
			if (type3.IsGenericParameter)
			{
				try
				{
					bool flag4 = false;
					byte b = 1;
					foreach (CustomAttributeData customAttributesDatum in methodInfo2.GetCustomAttributesData())
					{
						if (NullableContextAttributeType != null && customAttributesDatum.AttributeType == NullableContextAttributeType)
						{
							flag4 = true;
							if (customAttributesDatum.ConstructorArguments.Count > 0 && customAttributesDatum.ConstructorArguments[0].Value is byte b2)
							{
								b = b2;
							}
							break;
						}
					}
					if (!flag4 && methodInfo2.DeclaringType != null)
					{
						foreach (CustomAttributeData customAttributesDatum2 in methodInfo2.DeclaringType.GetCustomAttributesData())
						{
							if (NullableContextAttributeType != null && customAttributesDatum2.AttributeType == NullableContextAttributeType)
							{
								if (customAttributesDatum2.ConstructorArguments.Count > 0 && customAttributesDatum2.ConstructorArguments[0].Value is byte b3)
								{
									b = b3;
								}
								break;
							}
						}
					}
					flag2 = b == 2;
					flag3 = true;
				}
				catch (Exception)
				{
					flag2 = false;
				}
			}
		}
		if (!flag2 && Nullable.GetUnderlyingType(type) != null)
		{
			flag2 = true;
		}
		if (!flag3 && !type.IsValueType)
		{
			flag2 = false;
		}
		return flag2;
	}
}
}
