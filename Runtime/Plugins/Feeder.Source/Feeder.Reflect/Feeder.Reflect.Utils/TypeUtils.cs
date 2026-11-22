using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using Feeder.Reflect.Model;
using Feeder.Logging;

namespace Feeder.Reflect.Utils
{
public static class TypeUtils
{
	public const int TypeCacheCapacity = 1000;

	public const int EnumerableItemTypeCacheCapacity = 500;

	private static readonly char[] NestedTypeSeparators = new char[2] { '+', '.' };

	private static readonly LruCache<string, Type?> _typeCache = new LruCache<string, Type>(1000);

	private static readonly LruCache<string, Type?> _assemblyTypeCache = new LruCache<string, Type>(1000);

	private static readonly LruCache<string, Type?> _exactAssemblyTypeCache = new LruCache<string, Type>(1000);

	private static readonly LruCache<Type, Type?> _enumerableItemTypeCache = new LruCache<Type, Type>(500);

	public const string ArraySuffix = "[]";

	public static IEnumerable<Type> AllTypes => AssemblyUtils.AllTypes;

	public static void ClearTypeCache(ILogger? logger = null)
	{
		logger?.LogDebug("Clearing type resolution cache with {count} entries (capacity: {capacity}).", _typeCache.Count, _typeCache.Capacity);
		_typeCache.Clear();
	}

	public static void ClearEnumerableItemTypeCache(ILogger? logger = null)
	{
		logger?.LogDebug("Clearing enumerable item type cache with {count} entries (capacity: {capacity}).", _enumerableItemTypeCache.Count, _enumerableItemTypeCache.Capacity);
		_enumerableItemTypeCache.Clear();
	}

	public static void ClearAssemblyTypeCache(ILogger? logger = null)
	{
		logger?.LogDebug("Clearing assembly-prefixed type resolution cache with {count} entries (capacity: {capacity}).", _assemblyTypeCache.Count, _assemblyTypeCache.Capacity);
		_assemblyTypeCache.Clear();
	}

	public static void ClearExactAssemblyTypeCache(ILogger? logger = null)
	{
		logger?.LogDebug("Clearing exact assembly type resolution cache with {count} entries (capacity: {capacity}).", _exactAssemblyTypeCache.Count, _exactAssemblyTypeCache.Capacity);
		_exactAssemblyTypeCache.Clear();
	}

	public static bool IsDictionary(Type type)
	{
		if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Dictionary<, >) || type.GetGenericTypeDefinition() == typeof(IDictionary<, >)))
		{
			return true;
		}
		return type.GetInterfaces().Any((Type i) => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<, >));
	}

	public static Type[]? GetDictionaryGenericArguments(Type type)
	{
		if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Dictionary<, >) || type.GetGenericTypeDefinition() == typeof(IDictionary<, >)))
		{
			return type.GetGenericArguments();
		}
		return type.GetInterfaces().FirstOrDefault((Type i) => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<, >))?.GetGenericArguments();
	}

	public static bool IsIEnumerable(Type type)
	{
		if (type.IsArray)
		{
			return true;
		}
		if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
		{
			return true;
		}
		return type.GetInterfaces().Any((Type i) => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
	}

	public static Type? GetEnumerableItemType(Type type)
	{
		return _enumerableItemTypeCache.GetOrAdd(type, GetEnumerableItemTypeInternal);
	}

	private static Type? GetEnumerableItemTypeInternal(Type type)
	{
		if (type.IsArray)
		{
			return type.GetElementType();
		}
		if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
		{
			return type.GetGenericArguments().FirstOrDefault();
		}
		Type type2 = type.GetInterfaces().FirstOrDefault((Type i) => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
		if (type2 != null)
		{
			return type2.GetGenericArguments().FirstOrDefault();
		}
		Type baseType = type.BaseType;
		while (baseType != null && baseType != typeof(object))
		{
			if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
			{
				return baseType.GetGenericArguments().FirstOrDefault();
			}
			type2 = baseType.GetInterfaces().FirstOrDefault((Type i) => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
			if (type2 != null)
			{
				return type2.GetGenericArguments().FirstOrDefault();
			}
			baseType = baseType.BaseType;
		}
		return null;
	}

	public static string? GetDescription(Type type)
	{
		object obj = type.GetCustomAttribute<DescriptionAttribute>(inherit: true)?.Description;
		if (obj == null)
		{
			if (!(type.BaseType != null))
			{
				return null;
			}
			obj = GetDescription(type.BaseType);
		}
		return (string?)obj;
	}

	public static string? GetDescription(ParameterInfo? parameterInfo)
	{
		object obj = parameterInfo?.GetCustomAttribute<DescriptionAttribute>(inherit: true)?.Description;
		if (obj == null)
		{
			if (parameterInfo == null)
			{
				return null;
			}
			obj = GetDescription(parameterInfo.ParameterType);
		}
		return (string?)obj;
	}

	public static string? GetDescription(MemberInfo? memberInfo)
	{
		if (memberInfo == null)
		{
			return null;
		}
		string text = memberInfo.GetCustomAttribute<DescriptionAttribute>(inherit: true)?.Description;
		if (text != null)
		{
			return text;
		}
		return memberInfo.MemberType switch
		{
			MemberTypes.Field => GetFieldDescription((FieldInfo)memberInfo), 
			MemberTypes.Property => GetPropertyDescription((PropertyInfo)memberInfo), 
			_ => null, 
		};
	}

	public static string? GetFieldDescription(FieldInfo? fieldInfo)
	{
		if (fieldInfo == null)
		{
			return null;
		}
		object obj = fieldInfo.GetCustomAttribute<DescriptionAttribute>(inherit: true)?.Description;
		if (obj == null)
		{
			if (!(fieldInfo.FieldType != null))
			{
				return null;
			}
			obj = GetDescription(fieldInfo.FieldType);
		}
		return (string?)obj;
	}

	public static string? GetPropertyDescription(PropertyInfo? propertyInfo)
	{
		if (propertyInfo == null)
		{
			return null;
		}
		object obj = propertyInfo.GetCustomAttribute<DescriptionAttribute>(inherit: true)?.Description;
		if (obj == null)
		{
			if (!(propertyInfo.PropertyType != null))
			{
				return null;
			}
			obj = GetDescription(propertyInfo.PropertyType);
		}
		return (string?)obj;
	}

	public static string? GetPropertyDescription(Type type, string propertyName)
	{
		PropertyInfo property = type.GetProperty(propertyName);
		if (!(property != null))
		{
			return null;
		}
		return GetPropertyDescription(property);
	}

	private static string ToPascalCase(string camelCase)
	{
		if (string.IsNullOrEmpty(camelCase))
		{
			return camelCase;
		}
		return char.ToUpperInvariant(camelCase[0]) + camelCase.Substring(1);
	}

	public static string? GetFieldDescription(Type type, string fieldName)
	{
		FieldInfo field = type.GetField(fieldName);
		if (!(field != null))
		{
			return null;
		}
		return GetFieldDescription(field);
	}

	private static string DecodeSchemaRefChars(string typeName)
	{
		if (string.IsNullOrEmpty(typeName))
		{
			return typeName;
		}
		if (typeName.IndexOf('(') < 0 && typeName.IndexOf(')') < 0 && typeName.IndexOf('-') < 0)
		{
			return typeName;
		}
		StringBuilder stringBuilder = new StringBuilder(typeName.Length + 4);
		int num = 0;
		bool flag = false;
		for (int i = 0; i < typeName.Length; i++)
		{
			char c = typeName[i];
			if (flag)
			{
				stringBuilder.Append(c);
				continue;
			}
			switch (c)
			{
			case '(':
				num++;
				stringBuilder.Append('<');
				break;
			case ')':
				num--;
				stringBuilder.Append('>');
				break;
			case '[':
				num++;
				stringBuilder.Append('[');
				break;
			case ']':
				num--;
				stringBuilder.Append(']');
				break;
			case ',':
				if (num == 0)
				{
					flag = true;
				}
				stringBuilder.Append(',');
				break;
			case '-':
			{
				char c2 = ((i + 1 < typeName.Length) ? typeName[i + 1] : '\0');
				if (c2 >= '0' && c2 <= '9')
				{
					int j;
					for (j = i + 1; j < typeName.Length && typeName[j] >= '0' && typeName[j] <= '9'; j++)
					{
					}
					if (int.TryParse(typeName.Substring(i + 1, j - (i + 1)), out var result) && result >= 1)
					{
						stringBuilder.Append('[');
						if (result > 1)
						{
							stringBuilder.Append(new string(',', result - 1));
						}
						stringBuilder.Append(']');
						i = j - 1;
					}
					else
					{
						stringBuilder.Append('-');
					}
				}
				else if (c2 == '_' || char.IsLetter(c2))
				{
					stringBuilder.Append('+');
				}
				else
				{
					stringBuilder.Append('-');
				}
				break;
			}
			default:
				stringBuilder.Append(c);
				break;
			}
		}
		return stringBuilder.ToString();
	}

	public static Type? GetType(string? typeName)
	{
		if (string.IsNullOrWhiteSpace(typeName))
		{
			return null;
		}
		typeName = DecodeSchemaRefChars(typeName);
		if (_typeCache.TryGetValue(typeName, out Type value))
		{
			return value;
		}
		Type type = null;
		try
		{
			type = Type.GetType(typeName, throwOnError: false);
		}
		catch
		{
		}
		if (type != null)
		{
			_typeCache[typeName] = type;
			return type;
		}
		type = TryResolveArrayType(typeName);
		if (type != null)
		{
			_typeCache[typeName] = type;
			return type;
		}
		type = TryResolveCSharpGenericType(typeName);
		if (type != null)
		{
			_typeCache[typeName] = type;
			return type;
		}
		type = TryResolveClassicGenericType((string?)null, typeName);
		if (type != null)
		{
			_typeCache[typeName] = type;
			return type;
		}
		type = AssemblyUtils.AllTypes.FirstOrDefault((Type t) => typeName == t.FullName || typeName == t.AssemblyQualifiedName || typeName == DecodeSchemaRefChars(t.GetTypeId()));
		_typeCache[typeName] = type;
		return type;
	}

	public static Type? GetType(string? assemblyName, string? typeName)
	{
		if (string.IsNullOrWhiteSpace(typeName))
		{
			return null;
		}
		typeName = DecodeSchemaRefChars(typeName);
		if (string.IsNullOrEmpty(assemblyName))
		{
			return GetType(typeName);
		}
		string key = assemblyName + "|" + typeName;
		if (_assemblyTypeCache.TryGetValue(key, out Type value))
		{
			return value;
		}
		Type type = null;
		try
		{
			type = Type.GetType(typeName, throwOnError: false);
			if (type != null && !IsTypeInMatchingAssembly(type, assemblyName))
			{
				type = null;
			}
		}
		catch
		{
		}
		if (type != null)
		{
			_assemblyTypeCache[key] = type;
			return type;
		}
		type = TryResolveArrayType(assemblyName, typeName);
		if (type != null)
		{
			_assemblyTypeCache[key] = type;
			return type;
		}
		type = TryResolveCSharpGenericType(assemblyName, typeName);
		if (type != null)
		{
			_assemblyTypeCache[key] = type;
			return type;
		}
		type = TryResolveClassicGenericType(assemblyName, typeName);
		if (type != null)
		{
			_assemblyTypeCache[key] = type;
			return type;
		}
		type = AssemblyUtils.GetTypesStartingWith(assemblyName).FirstOrDefault((Type t) => typeName == t.FullName || typeName == t.AssemblyQualifiedName || typeName == DecodeSchemaRefChars(t.GetTypeId()));
		_assemblyTypeCache[key] = type;
		return type;
	}

	public static Type? GetType(Assembly assembly, string? typeName)
	{
		if (string.IsNullOrWhiteSpace(typeName) || assembly == null)
		{
			return null;
		}
		typeName = DecodeSchemaRefChars(typeName);
		string key = assembly.GetName().Name + "|" + typeName;
		if (_exactAssemblyTypeCache.TryGetValue(key, out Type value))
		{
			return value;
		}
		Type type = null;
		try
		{
			type = assembly.GetType(typeName, throwOnError: false);
		}
		catch
		{
		}
		if (type != null)
		{
			_exactAssemblyTypeCache[key] = type;
			return type;
		}
		type = TryResolveArrayType(assembly, typeName);
		if (type != null)
		{
			_exactAssemblyTypeCache[key] = type;
			return type;
		}
		type = TryResolveCSharpGenericType(assembly, typeName);
		if (type != null)
		{
			_exactAssemblyTypeCache[key] = type;
			return type;
		}
		type = TryResolveClassicGenericType(assembly, typeName);
		if (type != null)
		{
			_exactAssemblyTypeCache[key] = type;
			return type;
		}
		type = AssemblyUtils.GetAssemblyTypes(assembly).FirstOrDefault((Type t) => typeName == t.FullName || typeName == t.AssemblyQualifiedName || typeName == DecodeSchemaRefChars(t.GetTypeId()));
		_exactAssemblyTypeCache[key] = type;
		return type;
	}

	private static bool IsTypeInMatchingAssembly(Type type, string assemblyPrefix)
	{
		return type.Assembly.GetName().Name?.StartsWith(assemblyPrefix, StringComparison.Ordinal) ?? false;
	}

	private static Type? ResolveSimpleType(string name)
	{
		Type type = Type.GetType(name, throwOnError: false);
		if (type != null)
		{
			return type;
		}
		return AssemblyUtils.AllTypes.FirstOrDefault((Type t) => name == t.AssemblyQualifiedName || name == t.FullName || name == t.Name);
	}

	private static Type? ResolveSimpleType(string assemblyPrefix, string name)
	{
		Type type = Type.GetType(name, throwOnError: false);
		if (type != null && IsTypeInMatchingAssembly(type, assemblyPrefix))
		{
			return type;
		}
		return AssemblyUtils.GetTypesStartingWith(assemblyPrefix).FirstOrDefault((Type t) => name == t.AssemblyQualifiedName || name == t.FullName || name == t.Name);
	}

	private static Type? TryResolveArrayType(string typeName)
	{
		return TryResolveArrayType((string?)null, typeName);
	}

	private static Type? TryResolveArrayType(string? assemblyPrefix, string typeName)
	{
		if (!typeName.EndsWith("]"))
		{
			return null;
		}
		int num = typeName.LastIndexOf('[');
		if (num < 0)
		{
			return null;
		}
		string text = typeName.Substring(num);
		string text2 = text.Substring(1, text.Length - 2);
		if (text2.Length > 0 && text2.Any((char c) => c != ','))
		{
			return null;
		}
		int length = text2.Length;
		string typeName2 = typeName.Substring(0, num);
		Type type = GetType(assemblyPrefix, typeName2);
		if (type == null)
		{
			return null;
		}
		if (length != 0)
		{
			return type.MakeArrayType(length + 1);
		}
		return type.MakeArrayType();
	}

	private static Type? TryResolveCSharpGenericType(string typeName)
	{
		return TryResolveCSharpGenericType((string?)null, typeName);
	}

	private static Type? TryResolveCSharpGenericType(string? assemblyPrefix, string typeName)
	{
		int num = typeName.IndexOf('<');
		if (num < 0)
		{
			return null;
		}
		int num2 = FindMatchingCloseBracket(typeName, num);
		if (num2 < 0)
		{
			return null;
		}
		string text = typeName.Substring(0, num);
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		string[] array = ParseCSharpGenericArguments(typeName.Substring(num + 1, num2 - num - 1));
		if (array == null || array.Length == 0)
		{
			return null;
		}
		string name = $"{text}`{array.Length}";
		Type type = ((assemblyPrefix == null) ? ResolveSimpleType(name) : ResolveSimpleType(assemblyPrefix, name));
		if (type == null || !type.IsGenericTypeDefinition)
		{
			return null;
		}
		Type[] array2 = new Type[array.Length];
		for (int i = 0; i < array.Length; i++)
		{
			Type type2 = GetType(array[i].Trim());
			if (type2 == null)
			{
				return null;
			}
			array2[i] = type2;
		}
		Type type3;
		try
		{
			type3 = type.MakeGenericType(array2);
		}
		catch
		{
			return null;
		}
		string text2 = typeName.Substring(num2 + 1);
		while (!string.IsNullOrEmpty(text2))
		{
			if (!text2.StartsWith("+") && !text2.StartsWith("."))
			{
				return null;
			}
			text2 = text2.Substring(1);
			int num3 = text2.IndexOf('<');
			Type[] array3 = null;
			string text3;
			int startIndex;
			if (num3 > 0)
			{
				int num4 = FindMatchingCloseBracket(text2, num3);
				if (num4 < 0)
				{
					return null;
				}
				text3 = text2.Substring(0, num3);
				string[] array4 = ParseCSharpGenericArguments(text2.Substring(num3 + 1, num4 - num3 - 1));
				if (array4 == null)
				{
					return null;
				}
				array3 = new Type[array4.Length];
				for (int j = 0; j < array4.Length; j++)
				{
					Type type4 = GetType(array4[j]?.Trim());
					if (type4 == null)
					{
						return null;
					}
					array3[j] = type4;
				}
				startIndex = num4 + 1;
			}
			else
			{
				int num5 = text2.IndexOfAny(NestedTypeSeparators);
				if (num5 > 0)
				{
					text3 = text2.Substring(0, num5);
					startIndex = num5;
				}
				else
				{
					text3 = text2;
					startIndex = text2.Length;
				}
			}
			Type nestedType;
			Type[] array5;
			if (array3 != null)
			{
				nestedType = type3.GetNestedType($"{text3}`{array3.Length}");
				if (nestedType == null)
				{
					return null;
				}
				if (type3.IsGenericType && !type3.IsGenericTypeDefinition)
				{
					Type[] genericArguments = type3.GetGenericArguments();
					array5 = new Type[genericArguments.Length + array3.Length];
					Array.Copy(genericArguments, array5, genericArguments.Length);
					Array.Copy(array3, 0, array5, genericArguments.Length, array3.Length);
				}
				else
				{
					array5 = array3;
				}
			}
			else
			{
				nestedType = type3.GetNestedType(text3);
				if (nestedType == null)
				{
					return null;
				}
				array5 = ((type3.IsGenericType && !type3.IsGenericTypeDefinition) ? type3.GetGenericArguments() : Type.EmptyTypes);
			}
			if (nestedType.IsGenericTypeDefinition)
			{
				try
				{
					type3 = ((nestedType.GetGenericArguments().Length == array5.Length) ? nestedType.MakeGenericType(array5) : nestedType);
				}
				catch
				{
					return null;
				}
			}
			else
			{
				type3 = nestedType;
			}
			text2 = text2.Substring(startIndex);
		}
		return type3;
	}

	private static int FindMatchingCloseBracket(string typeName, int openIndex)
	{
		int num = 0;
		for (int i = openIndex; i < typeName.Length; i++)
		{
			if (typeName[i] == '<')
			{
				num++;
			}
			else if (typeName[i] == '>')
			{
				num--;
				if (num == 0)
				{
					return i;
				}
			}
		}
		return -1;
	}

	private static string[]? ParseCSharpGenericArguments(string argsString)
	{
		if (string.IsNullOrWhiteSpace(argsString))
		{
			return null;
		}
		List<string> list = new List<string>();
		int num = 0;
		StringBuilder stringBuilder = new StringBuilder();
		foreach (char c in argsString)
		{
			switch (c)
			{
			case '<':
				num++;
				stringBuilder.Append(c);
				continue;
			case '>':
				num--;
				stringBuilder.Append(c);
				continue;
			case ',':
				if (num == 0)
				{
					string text = stringBuilder.ToString().Trim();
					if (!string.IsNullOrEmpty(text))
					{
						list.Add(text);
					}
					stringBuilder.Clear();
					continue;
				}
				break;
			}
			stringBuilder.Append(c);
		}
		string text2 = stringBuilder.ToString().Trim();
		if (!string.IsNullOrEmpty(text2))
		{
			list.Add(text2);
		}
		if (list.Count <= 0)
		{
			return null;
		}
		return list.ToArray();
	}

	private static Type? TryResolveClassicGenericType(string? assemblyPrefix, string typeName)
	{
		int num = typeName.IndexOf('`');
		if (num < 0)
		{
			return null;
		}
		int num2 = typeName.IndexOf("[[", num);
		if (num2 < 0)
		{
			return null;
		}
		string name = typeName.Substring(0, num2);
		Type type = ((assemblyPrefix == null) ? ResolveSimpleType(name) : ResolveSimpleType(assemblyPrefix, name));
		if (type == null || !type.IsGenericTypeDefinition)
		{
			return null;
		}
		Type[] array = ParseGenericArguments(typeName, num2);
		if (array == null || array.Length != type.GetGenericArguments().Length)
		{
			return null;
		}
		try
		{
			return type.MakeGenericType(array);
		}
		catch
		{
			return null;
		}
	}

	private static Type[]? ParseGenericArguments(string typeName, int startIndex)
	{
		List<Type> list = new List<Type>();
		int num = 0;
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = startIndex; i < typeName.Length; i++)
		{
			char c = typeName[i];
			if (c == '[')
			{
				num++;
				if (num > 2)
				{
					stringBuilder.Append(c);
				}
			}
			else if (c == ']')
			{
				num--;
				if (num == 1)
				{
					string text = stringBuilder.ToString().Trim();
					if (!string.IsNullOrEmpty(text))
					{
						Type type = GetType(text);
						if (type == null)
						{
							return null;
						}
						list.Add(type);
					}
					stringBuilder.Clear();
				}
				else if (num > 1)
				{
					stringBuilder.Append(c);
				}
				else if (num == 0)
				{
					break;
				}
			}
			else if ((c != ',' || num != 1) && num > 1)
			{
				stringBuilder.Append(c);
			}
		}
		if (list.Count <= 0)
		{
			return null;
		}
		return list.ToArray();
	}

	private static Type? ResolveSimpleType(Assembly assembly, string name)
	{
		Type type = assembly.GetType(name, throwOnError: false);
		if (type != null)
		{
			return type;
		}
		try
		{
			return AssemblyUtils.GetAssemblyTypes(assembly).FirstOrDefault((Type t) => name == t.AssemblyQualifiedName || name == t.FullName || name == t.Name);
		}
		catch
		{
			return null;
		}
	}

	private static Type? TryResolveArrayType(Assembly assembly, string typeName)
	{
		if (!typeName.EndsWith("]"))
		{
			return null;
		}
		int num = typeName.LastIndexOf('[');
		if (num < 0)
		{
			return null;
		}
		string text = typeName.Substring(num);
		string text2 = text.Substring(1, text.Length - 2);
		if (text2.Length > 0 && text2.Any((char c) => c != ','))
		{
			return null;
		}
		int length = text2.Length;
		string typeName2 = typeName.Substring(0, num);
		Type type = GetType(assembly, typeName2);
		if (type == null)
		{
			return null;
		}
		if (length != 0)
		{
			return type.MakeArrayType(length + 1);
		}
		return type.MakeArrayType();
	}

	private static Type? TryResolveCSharpGenericType(Assembly assembly, string typeName)
	{
		int num = typeName.IndexOf('<');
		if (num < 0)
		{
			return null;
		}
		int num2 = FindMatchingCloseBracket(typeName, num);
		if (num2 < 0)
		{
			return null;
		}
		string text = typeName.Substring(0, num);
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		string[] array = ParseCSharpGenericArguments(typeName.Substring(num + 1, num2 - num - 1));
		if (array == null || array.Length == 0)
		{
			return null;
		}
		string name = $"{text}`{array.Length}";
		Type type = ResolveSimpleType(assembly, name);
		if (type == null || !type.IsGenericTypeDefinition)
		{
			return null;
		}
		Type[] array2 = new Type[array.Length];
		for (int i = 0; i < array.Length; i++)
		{
			Type type2 = GetType(array[i].Trim());
			if (type2 == null)
			{
				return null;
			}
			array2[i] = type2;
		}
		Type type3;
		try
		{
			type3 = type.MakeGenericType(array2);
		}
		catch
		{
			return null;
		}
		string text2 = typeName.Substring(num2 + 1);
		while (!string.IsNullOrEmpty(text2))
		{
			if (!text2.StartsWith("+") && !text2.StartsWith("."))
			{
				return null;
			}
			text2 = text2.Substring(1);
			int num3 = text2.IndexOf('<');
			Type[] array3 = null;
			string text3;
			int startIndex;
			if (num3 > 0)
			{
				int num4 = FindMatchingCloseBracket(text2, num3);
				if (num4 < 0)
				{
					return null;
				}
				text3 = text2.Substring(0, num3);
				string[] array4 = ParseCSharpGenericArguments(text2.Substring(num3 + 1, num4 - num3 - 1));
				if (array4 == null)
				{
					return null;
				}
				array3 = new Type[array4.Length];
				for (int j = 0; j < array4.Length; j++)
				{
					Type type4 = GetType(array4[j]?.Trim());
					if (type4 == null)
					{
						return null;
					}
					array3[j] = type4;
				}
				startIndex = num4 + 1;
			}
			else
			{
				int num5 = text2.IndexOfAny(NestedTypeSeparators);
				if (num5 > 0)
				{
					text3 = text2.Substring(0, num5);
					startIndex = num5;
				}
				else
				{
					text3 = text2;
					startIndex = text2.Length;
				}
			}
			Type nestedType;
			Type[] array5;
			if (array3 != null)
			{
				nestedType = type3.GetNestedType($"{text3}`{array3.Length}");
				if (nestedType == null)
				{
					return null;
				}
				if (type3.IsGenericType && !type3.IsGenericTypeDefinition)
				{
					Type[] genericArguments = type3.GetGenericArguments();
					array5 = new Type[genericArguments.Length + array3.Length];
					Array.Copy(genericArguments, array5, genericArguments.Length);
					Array.Copy(array3, 0, array5, genericArguments.Length, array3.Length);
				}
				else
				{
					array5 = array3;
				}
			}
			else
			{
				nestedType = type3.GetNestedType(text3);
				if (nestedType == null)
				{
					return null;
				}
				array5 = ((type3.IsGenericType && !type3.IsGenericTypeDefinition) ? type3.GetGenericArguments() : Type.EmptyTypes);
			}
			if (nestedType.IsGenericTypeDefinition)
			{
				try
				{
					type3 = ((nestedType.GetGenericArguments().Length == array5.Length) ? nestedType.MakeGenericType(array5) : nestedType);
				}
				catch
				{
					return null;
				}
			}
			else
			{
				type3 = nestedType;
			}
			text2 = text2.Substring(startIndex);
		}
		return type3;
	}

	private static Type? TryResolveClassicGenericType(Assembly assembly, string typeName)
	{
		int num = typeName.IndexOf('`');
		if (num < 0)
		{
			return null;
		}
		int num2 = typeName.IndexOf("[[", num);
		if (num2 < 0)
		{
			return null;
		}
		string name = typeName.Substring(0, num2);
		Type type = ResolveSimpleType(assembly, name);
		if (type == null || !type.IsGenericTypeDefinition)
		{
			return null;
		}
		Type[] array = ParseGenericArguments(typeName, num2);
		if (array == null || array.Length != type.GetGenericArguments().Length)
		{
			return null;
		}
		try
		{
			return type.MakeGenericType(array);
		}
		catch
		{
			return null;
		}
	}

	public static bool IsAssignableTo(object? obj, Type targetType)
	{
		if (targetType == null)
		{
			return false;
		}
		if (obj == null)
		{
			if (targetType.IsValueType)
			{
				return Nullable.GetUnderlyingType(targetType) != null;
			}
			return true;
		}
		return targetType.IsAssignableFrom(obj.GetType());
	}

	public static bool IsCastable(Type? type, Type to)
	{
		if (type == null || to == null)
		{
			return false;
		}
		type = Nullable.GetUnderlyingType(type) ?? type;
		to = Nullable.GetUnderlyingType(to) ?? to;
		if (to.IsAssignableFrom(type))
		{
			return true;
		}
		if (type.IsPrimitive && to.IsPrimitive)
		{
			return true;
		}
		if (type == typeof(string) && to == typeof(object))
		{
			return true;
		}
		return false;
	}

	public static int GetInheritanceDistance(Type baseType, Type targetType)
	{
		if (!baseType.IsAssignableFrom(targetType))
		{
			return -1;
		}
		int num = 0;
		Type type = targetType;
		while (type != null && type != baseType)
		{
			type = type.BaseType;
			num++;
		}
		if (!(type == baseType))
		{
			return -1;
		}
		return num;
	}

	public static bool IsPrimitive(Type type)
	{
		if (!type.IsPrimitive && !type.IsEnum && !(type == typeof(string)) && !(type == typeof(decimal)) && !(type == typeof(DateTime)) && !(type == typeof(DateTimeOffset)) && !(type == typeof(TimeSpan)))
		{
			return type == typeof(Guid);
		}
		return true;
	}

	public static IEnumerable<Type> GetGenericTypes(Type type, HashSet<int>? visited = null)
	{
		if (visited == null)
		{
			visited = new HashSet<int>();
		}
		if (visited.Contains(type.GetHashCode()))
		{
			yield break;
		}
		if (type.IsGenericType)
		{
			Type[] genericArguments = type.GetGenericArguments();
			if (genericArguments != null)
			{
				Type[] array = genericArguments;
				foreach (Type genericArgument in array)
				{
					int item = type.GetHashCode() ^ (genericArgument.GetHashCode() * 397);
					if (visited.Contains(item))
					{
						continue;
					}
					visited.Add(item);
					yield return genericArgument;
					foreach (Type genericType in GetGenericTypes(genericArgument, visited))
					{
						yield return genericType;
					}
				}
			}
		}
		if (type.BaseType == null || visited.Contains(type.BaseType.GetHashCode()))
		{
			yield break;
		}
		foreach (Type genericType2 in GetGenericTypes(type.BaseType, visited))
		{
			yield return genericType2;
		}
	}

	public static Type? GetTypeWithObjectPriority(object? obj, Type? fallbackType, out string? error)
	{
		Type type = obj?.GetType() ?? fallbackType;
		if (type == null)
		{
			error = "Object is null and type is unknown. Provide proper typeName.";
			return null;
		}
		error = null;
		return type;
	}

	public static Type? GetTypeWithNamePriority(SerializedMember? member, Type? fallbackType, out string? error)
	{
		if (StringUtils.IsNullOrEmpty(member?.typeName) && fallbackType == null)
		{
			error = "SerializedMember.typeName is null or empty. Provide proper typeName.";
			return null;
		}
		Type type = GetType(member?.typeName);
		if (type == null)
		{
			if (fallbackType == null)
			{
				error = "Type '" + member.typeName + "' not found.";
				return null;
			}
			error = null;
			return fallbackType;
		}
		error = null;
		return type;
	}

	public static Type? GetTypeWithValuePriority(Type? type, SerializedMember? fallbackMember, out string? error)
	{
		if (type == null)
		{
			if (fallbackMember == null)
			{
				error = "Type is unknown and SerializedMember.typeName is null or empty.";
				return null;
			}
			type = GetType(fallbackMember.typeName);
			if (type == null)
			{
				error = "Type '" + fallbackMember.typeName + "' not found.";
				return null;
			}
			error = null;
		}
		error = null;
		return type;
	}

	public static string Sanitize<T>()
	{
		return Sanitize(typeof(T));
	}

	public static string Sanitize(Type? type)
	{
		if (type == null)
		{
			return "null";
		}
		type = Nullable.GetUnderlyingType(type) ?? type;
		return type.FullName ?? "null";
	}

	public static string GetTypeId<T>()
	{
		return GetTypeId(typeof(T));
	}

	public static string GetTypeId(Type type)
	{
		if (type == null)
		{
			throw new ArgumentNullException("type");
		}
		if (type.IsGenericParameter)
		{
			return type.Name;
		}
		type = Nullable.GetUnderlyingType(type) ?? type;
		if (type == typeof(string))
		{
			return Sanitize(type);
		}
		if (type.IsNested)
		{
			Type type2 = type.DeclaringType;
			if (type2 != null)
			{
				if (type2.IsGenericTypeDefinition && type.IsGenericType && !type.IsGenericTypeDefinition)
				{
					Type[] genericArguments = type.GetGenericArguments();
					Type[] genericArguments2 = type2.GetGenericArguments();
					if (genericArguments.Length >= genericArguments2.Length)
					{
						Type[] typeArguments = genericArguments.Take(genericArguments2.Length).ToArray();
						type2 = type2.MakeGenericType(typeArguments);
					}
				}
				string typeId = GetTypeId(type2);
				string text = type.Name;
				int num = text.IndexOf('`');
				if (num > 0)
				{
					text = text.Substring(0, num);
				}
				if (type.IsGenericType)
				{
					Type[] genericArguments3 = type.GetGenericArguments();
					int num2 = (type2.IsGenericType ? type2.GetGenericArguments().Length : 0);
					if (genericArguments3.Length > num2)
					{
						IEnumerable<string> values = genericArguments3.Skip(num2).Select(GetTypeId);
						return typeId + "-" + text + "(" + string.Join(",", values) + ")";
					}
				}
				return typeId + "-" + text;
			}
		}
		if (type.IsGenericType)
		{
			string text2 = type.GetGenericTypeDefinition().Sanitize();
			if (StringUtils.IsNullOrEmpty(text2))
			{
				throw new InvalidOperationException($"Generic type '{type}' does not have a full name.");
			}
			int num3 = text2.IndexOf('`');
			if (num3 > 0)
			{
				text2 = text2.Substring(0, num3);
			}
			IEnumerable<string> values2 = type.GetGenericArguments().Select(GetTypeId);
			return text2 + "(" + string.Join(",", values2) + ")";
		}
		if (type.IsArray)
		{
			Type elementType = type.GetElementType();
			if (elementType == null)
			{
				throw new InvalidOperationException($"Array type '{type}' has no element type.");
			}
			int arrayRank = type.GetArrayRank();
			return $"{GetTypeId(elementType)}-{arrayRank}";
		}
		return Sanitize(type);
	}

	public static string GetSchemaTypeId<T>()
	{
		return GetSchemaTypeId(typeof(T));
	}

	public static string GetSchemaTypeId(Type type)
	{
		return GetTypeId(type);
	}

	public static bool IsNameMatch(Type? type, string? typeName)
	{
		if (type == null || string.IsNullOrEmpty(typeName))
		{
			return false;
		}
		return type.GetTypeId() == typeName;
	}

	public static string GetTypeShortName<T>()
	{
		return GetTypeShortName(typeof(T));
	}

	public static string GetTypeShortName(Type? type)
	{
		if (type == null)
		{
			return "null";
		}
		if (type.IsGenericParameter)
		{
			return type.Name;
		}
		Type underlyingType = Nullable.GetUnderlyingType(type);
		if (underlyingType != null)
		{
			return GetTypeShortName(underlyingType) + "?";
		}
		if (type.IsNested)
		{
			Type type2 = type.DeclaringType;
			if (type2 != null)
			{
				if (type2.IsGenericTypeDefinition && type.IsGenericType && !type.IsGenericTypeDefinition)
				{
					Type[] genericArguments = type.GetGenericArguments();
					Type[] genericArguments2 = type2.GetGenericArguments();
					if (genericArguments.Length >= genericArguments2.Length)
					{
						Type[] typeArguments = genericArguments.Take(genericArguments2.Length).ToArray();
						type2 = type2.MakeGenericType(typeArguments);
					}
				}
				string typeShortName = GetTypeShortName(type2);
				string text = type.Name;
				int num = text.IndexOf('`');
				if (num > 0)
				{
					text = text.Substring(0, num);
				}
				if (type.IsGenericType)
				{
					Type[] genericArguments3 = type.GetGenericArguments();
					int num2 = (type2.IsGenericType ? type2.GetGenericArguments().Length : 0);
					if (genericArguments3.Length > num2)
					{
						IEnumerable<string> values = genericArguments3.Skip(num2).Select(GetTypeShortName);
						return typeShortName + "+" + text + "<" + string.Join(", ", values) + ">";
					}
				}
				return typeShortName + "+" + text;
			}
		}
		if (type.IsGenericType)
		{
			string text2 = type.Name;
			int num3 = text2.IndexOf('`');
			if (num3 > 0)
			{
				text2 = text2.Substring(0, num3);
			}
			IEnumerable<string> values2 = type.GetGenericArguments().Select(GetTypeShortName);
			return text2 + "<" + string.Join(", ", values2) + ">";
		}
		if (type.IsArray)
		{
			Type elementType = type.GetElementType();
			int arrayRank = type.GetArrayRank();
			if (arrayRank == 1)
			{
				return GetTypeShortName(elementType) + "[]";
			}
			return GetTypeShortName(elementType) + "[" + new string(',', arrayRank - 1).Replace(",", ", ") + "]";
		}
		if (!string.IsNullOrEmpty(type.Name))
		{
			return type.Name;
		}
		return "null";
	}
}
}
