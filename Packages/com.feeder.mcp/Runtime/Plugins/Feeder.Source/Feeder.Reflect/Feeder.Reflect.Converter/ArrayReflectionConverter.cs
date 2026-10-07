using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Feeder.Json;
using Feeder.Reflect.Model;
using Feeder.Reflect.Utils;
using Feeder.Logging;

namespace Feeder.Reflect.Converter
{
public class ArrayReflectionConverter : BaseReflectionConverter<Array>
{
	protected virtual bool IsGenericList(Type type, out Type? elementType)
	{
		Type type2 = type.GetInterfaces().FirstOrDefault((Type i) => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>));
		if (type2 == null)
		{
			elementType = null;
			return false;
		}
		elementType = type2.GetGenericArguments()[0];
		return true;
	}

	public override int SerializationPriority(Type type, ILogger? logger = null)
	{
		if (type.IsArray)
		{
			return 10001;
		}
		if (IsGenericList(type, out Type _))
		{
			return 10001;
		}
		if (type == typeof(string))
		{
			return 0;
		}
		if (!typeof(IEnumerable).IsAssignableFrom(type))
		{
			return 0;
		}
		return 2500;
	}

	protected override SerializedMember InternalSerialize(Reflector reflector, object? obj, Type type, string? name = null, bool recursive = true, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, int depth = 0, Logs? logs = null, ILogger? logger = null, SerializationContext? context = null)
	{
		if (logger != null && logger.IsEnabled(LogLevel.Trace))
		{
			logger.LogTrace("{padding}Serializing enumerable of type {Type} at depth {Depth}, recursive {recursive}.", StringUtils.GetPadding(depth), type.GetTypeId(), depth, recursive);
		}
		if (obj == null)
		{
			return SerializedMember.FromJson(type, null, name);
		}
		if (recursive)
		{
			depth++;
			int num = 0;
			IEnumerable obj2 = (IEnumerable)obj;
			SerializedMemberList serializedMemberList = new SerializedMemberList();
			Type enumerableItemType = TypeUtils.GetEnumerableItemType(type);
			foreach (object item in obj2)
			{
				Type type2 = item?.GetType();
				Type type3 = type2 ?? enumerableItemType;
				if (logger != null && logger.IsEnabled(LogLevel.Trace))
				{
					logger.LogTrace("{padding}Serializing item '{index}' of type '{type}' in '{objType}'.\nPath: {path}", StringUtils.GetPadding(depth), num, type3?.GetTypeId().ValueOrNull(), obj.GetType().GetTypeId().ValueOrNull(), context?.GetPath(obj));
				}
				if (type2 != null && reflector.Converters.IsTypeBlacklisted(type2))
				{
					serializedMemberList.Add(null);
					num++;
				}
				else
				{
					serializedMemberList.Add(reflector.Serialize(item, type3, $"[{num++}]", recursive, flags, depth, logs, logger, context));
				}
			}
			return SerializedMember.FromValue(reflector, type, serializedMemberList, name);
		}
		return SerializedMember.FromJson(type, obj.ToJson(reflector, null, depth, logger), name);
	}

	protected override bool SetValue(Reflector reflector, ref object? obj, Type type, JsonElement? value, int depth = 0, Logs? logs = null, ILogger? logger = null)
	{
		if (logger != null && logger.IsEnabled(LogLevel.Trace))
		{
			logger.LogTrace(StringUtils.GetPadding(depth) + "Set value type='" + type.GetTypeShortName() + "'. Converter='" + GetType().GetTypeShortName() + "'.");
		}
		if (!TryDeserializeValueListInternal(reflector, value, type, out IEnumerable result, null, depth + 1, logs, logger))
		{
			Print.FailedToSetNewValue(ref obj, type, depth, logs);
			return false;
		}
		Print.SetNewValueEnumerable(ref obj, ref result, type, depth, logs);
		obj = result;
		return true;
	}

	public override bool SetField(Reflector reflector, ref object? obj, Type fallbackType, FieldInfo fieldInfo, SerializedMember? value, int depth = 0, Logs? logs = null, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		if (!TryDeserializeValue(reflector, value, out object result, out Type type, fallbackType, depth, logs, logger))
		{
			logs?.Error("Failed to deserialize value for field '" + fieldInfo.Name + "'.", depth);
			return false;
		}
		if (!TypeUtils.IsCastable(type, fieldInfo.FieldType))
		{
			logs?.Error("Parsed value type '" + type?.GetTypeId().ValueOrNull() + "' is not assignable to field type '" + fieldInfo.FieldType.GetTypeId() + "' for field '" + fieldInfo.Name + "'.", depth);
			return false;
		}
		fieldInfo.SetValue(obj, result);
		logs?.Success($"Field '{fieldInfo.Name}' modified to '{result}'.", depth);
		return true;
	}

	public override bool SetProperty(Reflector reflector, ref object? obj, Type fallbackType, PropertyInfo propertyInfo, SerializedMember? value, int depth = 0, Logs? logs = null, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		if (!propertyInfo.CanWrite)
		{
			logs?.Error("Property '" + propertyInfo.Name + "' is read-only.", depth);
			return false;
		}
		if (!TryDeserializeValue(reflector, value, out object result, out Type type, fallbackType, depth, logs, logger))
		{
			logs?.Error("Failed to deserialize value for property '" + propertyInfo.Name + "'.", depth);
			return false;
		}
		if (!TypeUtils.IsCastable(type, propertyInfo.PropertyType))
		{
			logs?.Error("Parsed value type '" + type?.GetTypeId().ValueOrNull() + "' is not assignable to property type '" + propertyInfo.PropertyType.GetTypeId() + "' for property '" + propertyInfo.Name + "'.", depth);
			return false;
		}
		propertyInfo.SetValue(obj, result);
		logs?.Success($"Property '{propertyInfo.Name}' modified to '{result}'.", depth);
		return true;
	}

	public override object? Deserialize(Reflector reflector, SerializedMember data, Type? fallbackType = null, string? fallbackName = null, int depth = 0, Logs? logs = null, ILogger? logger = null, DeserializationContext? context = null)
	{
		string padding = StringUtils.GetPadding(depth);
		Type typeWithNamePriority = TypeUtils.GetTypeWithNamePriority(data, fallbackType, out string error);
		if (typeWithNamePriority == null)
		{
			if (logger != null && logger.IsEnabled(LogLevel.Warning))
			{
				logger.LogWarning(padding + error);
			}
			logs?.Warning(error ?? string.Empty, depth);
			return null;
		}
		if (logger != null && logger.IsEnabled(LogLevel.Trace))
		{
			logger.LogTrace("{padding}{icon} Deserialize 'value', type='{typeName}', collectionType='{collectionType}'", padding, "⚪", typeWithNamePriority.GetTypeShortName(), typeWithNamePriority.IsArray ? "Array" : (IsGenericList(typeWithNamePriority, out Type _) ? "IList<>" : "IEnumerable"));
		}
		if (!data.valueJsonElement.HasValue || data.valueJsonElement.Value.ValueKind != JsonValueKind.Array)
		{
			if (logger != null && logger.IsEnabled(LogLevel.Warning))
			{
				logger.LogWarning("{padding}{icon} Failed to deserialize 'value' json as Array. Value is null or not an array.", padding, "⚠\ufe0f");
			}
			logs?.Warning("Failed to deserialize 'value' json as Array.", depth);
			return null;
		}
		JsonElement value = data.valueJsonElement.Value;
		int arrayLength = value.GetArrayLength();
		if (typeWithNamePriority.IsArray)
		{
			Type elementType2 = typeWithNamePriority.GetElementType();
			if (elementType2 == null)
			{
				if (logger != null && logger.IsEnabled(LogLevel.Warning))
				{
					logger.LogWarning("{padding}{icon} Failed to get element type for array type '{typeName}'", padding, "⚠\ufe0f", typeWithNamePriority.GetTypeId());
				}
				logs?.Warning("Failed to get element type for array type '" + typeWithNamePriority.GetTypeId() + "'.", depth);
				return null;
			}
			Array array = Array.CreateInstance(elementType2, arrayLength);
			if (array == null)
			{
				if (logger != null && logger.IsEnabled(LogLevel.Warning))
				{
					logger.LogWarning("{padding}{icon} Failed to create array instance for type '{typeName}'", padding, "⚠\ufe0f", typeWithNamePriority.GetTypeId());
				}
				logs?.Warning("Failed to create array instance for type '" + typeWithNamePriority.GetTypeId() + "'.", depth);
				return null;
			}
			context?.Register(array);
			int num = 0;
			{
				foreach (JsonElement item in value.EnumerateArray())
				{
					SerializedMember serializedMember = ParseElementToMember(item);
					serializedMember.name = $"[{num}]";
					object obj = reflector.Deserialize(serializedMember, elementType2, null, depth + 1, logs, logger, context);
					if (obj != null)
					{
						array.SetValue(obj, num);
					}
					num++;
				}
				return array;
			}
		}
		if (IsGenericList(typeWithNamePriority, out Type elementType3))
		{
			object obj2 = reflector.CreateInstance(typeWithNamePriority);
			if (obj2 == null)
			{
				if (logger != null && logger.IsEnabled(LogLevel.Warning))
				{
					logger.LogWarning("{padding}{icon} Failed to create list instance for type '{typeName}'", padding, "⚠\ufe0f", typeWithNamePriority.GetTypeId());
				}
				return null;
			}
			MethodInfo method = typeWithNamePriority.GetMethod("Add");
			if (method == null)
			{
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError("{padding}{icon} Failed to find 'Add' method on list type='{typeName}'", padding, "❌", typeWithNamePriority.GetTypeId());
				}
				return null;
			}
			context?.Register(obj2);
			int num2 = 0;
			foreach (JsonElement item2 in value.EnumerateArray())
			{
				SerializedMember serializedMember2 = ParseElementToMember(item2);
				serializedMember2.name = $"[{num2}]";
				object obj3 = reflector.Deserialize(serializedMember2, elementType3, null, depth + 1, logs, logger, context);
				method.Invoke(obj2, new object[1] { obj3 });
				num2++;
			}
			if (logger != null && logger.IsEnabled(LogLevel.Information))
			{
				logger.LogInformation("{padding}Successfully created list of type='{typeName}'", padding, obj2.GetType().GetTypeId());
			}
			return obj2;
		}
		if (logger != null && logger.IsEnabled(LogLevel.Warning))
		{
			logger.LogWarning("{padding}Type '{typeName}' is neither array nor generic list", padding, typeWithNamePriority.GetTypeId());
		}
		return null;
	}

	protected override bool TryDeserializeValueInternal(Reflector reflector, SerializedMember serializedMember, out object? result, Type type, int depth = 0, Logs? logs = null, ILogger? logger = null)
	{
		string padding = StringUtils.GetPadding(depth);
		if (logger != null && logger.IsEnabled(LogLevel.Trace))
		{
			logger.LogTrace("{padding}TryDeserializeValueInternal type='{typeName}', name='{name}', AllowCascadeSerialize={AllowCascadeSerialize}, Converter='{ConverterName}'", padding, type.GetTypeId(), serializedMember.name.ValueOrNull(), AllowCascadeSerialization, GetType().Name);
		}
		if (AllowCascadeSerialization)
		{
			if (!serializedMember.valueJsonElement.HasValue || serializedMember.valueJsonElement.Value.ValueKind == JsonValueKind.Null)
			{
				result = reflector.GetDefaultValue(type);
				if (logger != null && logger.IsEnabled(LogLevel.Warning))
				{
					logger.LogWarning("{padding}{icon} 'value' is null for type='{typeName}', name='{name}'. Converter='{ConverterName}'", padding, "⚠\ufe0f", type.GetTypeId(), serializedMember.name.ValueOrNull(), GetType().Name);
				}
				return true;
			}
			if (serializedMember.valueJsonElement.Value.ValueKind != JsonValueKind.Array)
			{
				result = reflector.GetDefaultValue(type);
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + "❌ Only array deserialization is supported in this Converter (" + GetType().Name + ").");
				}
				logs?.Error("Only array deserialization is supported in this Converter (" + GetType().Name + ").", depth);
				return false;
			}
			if (TryDeserializeValueListInternal(reflector, serializedMember.valueJsonElement, type, out IEnumerable result2, serializedMember.name, depth + 1, logs, logger))
			{
				result = result2;
				if (logger != null && logger.IsEnabled(LogLevel.Trace))
				{
					logger.LogTrace(padding + "\ud83d\udfe2 Deserialized as an enumerable.");
				}
				return true;
			}
			result = reflector.CreateInstance(type);
			return false;
		}
		return base.TryDeserializeValueInternal(reflector, serializedMember, out result, type, depth, logs, logger);
	}

	protected virtual bool TryDeserializeValueListInternal(Reflector reflector, JsonElement? jsonElement, Type type, out IEnumerable? result, string? name = null, int depth = 0, Logs? logs = null, ILogger? logger = null, DeserializationContext? context = null)
	{
		string padding = StringUtils.GetPadding(depth);
		string padding2 = StringUtils.GetPadding(depth + 1);
		if (logger != null && logger.IsEnabled(LogLevel.Trace))
		{
			logger.LogTrace("{padding}TryDeserializeValueListInternal name='{name}', type='{typeName}'", padding, name.ValueOrNull(), type.GetTypeShortName());
		}
		try
		{
			name = name.ValueOrNull();
			if (!jsonElement.HasValue)
			{
				result = null;
				return true;
			}
			if (jsonElement.Value.ValueKind != JsonValueKind.Array)
			{
				result = null;
				return false;
			}
			JsonElement value = jsonElement.Value;
			int arrayLength = value.GetArrayLength();
			if (logger != null && logger.IsEnabled(LogLevel.Trace))
			{
				logger.LogTrace($"{padding}Deserializing '{name}' enumerable with {arrayLength} items.");
			}
			logs?.Info($"Deserializing '{name}' enumerable with {arrayLength} items.", depth);
			Type enumerableItemType = TypeUtils.GetEnumerableItemType(type);
			if (enumerableItemType == null)
			{
				result = null;
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + "Failed to determine element type for '" + name + "' of type '" + type.GetTypeShortName() + "'.");
				}
				logs?.Error("Failed to determine element type for '" + name + "'.", depth);
				return false;
			}
			IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(enumerableItemType));
			if (list == null)
			{
				result = null;
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + "Failed to create list instance for type '" + type.GetTypeShortName() + "'.");
				}
				logs?.Error("Failed to create list instance for type '" + type.GetTypeShortName() + "'.", depth);
				return false;
			}
			context?.Register(list);
			int num = 0;
			foreach (JsonElement item in value.EnumerateArray())
			{
				SerializedMember serializedMember = ParseElementToMember(item);
				serializedMember.name = $"[{num}]";
				object obj = reflector.Deserialize(serializedMember, enumerableItemType, null, depth + 1, logs, logger, context);
				if (logger != null && logger.IsEnabled(LogLevel.Trace))
				{
					logger.LogTrace($"{padding2}Enumerable[{num}] deserialized successfully: {obj?.GetType().GetTypeShortName()}");
				}
				logs?.Info($"Enumerable[{num}] deserialized successfully.", depth + 1);
				list.Add(obj);
				num++;
			}
			if (type.IsArray)
			{
				Array array = Array.CreateInstance(enumerableItemType, list.Count);
				for (int i = 0; i < list.Count; i++)
				{
					array.SetValue(list[i], i);
				}
				result = array;
				if (logger != null && logger.IsEnabled(LogLevel.Trace))
				{
					logger.LogTrace($"{padding}Deserialized '{name}' as an array with {array.Length} items.");
				}
				logs?.Success($"Deserialized '{name}' as an array with {array.Length} items.", depth);
			}
			else
			{
				result = list;
				if (logger != null && logger.IsEnabled(LogLevel.Trace))
				{
					logger.LogTrace($"{padding}Deserialized '{name}' as a list with {list.Count} items.");
				}
				logs?.Success($"Deserialized '{name}' as a list with {list.Count} items.", depth);
			}
			return true;
		}
		catch (Exception ex)
		{
			result = null;
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(padding + "Failed to deserialize '" + name + "': " + ex.Message + "\n" + ex.StackTrace);
			}
			logs?.Error("Failed to deserialize '" + name + "': " + ex.Message, depth);
			return false;
		}
	}

	protected virtual SerializedMember ParseElementToMember(JsonElement element)
	{
		SerializedMember serializedMember = null;
		if (element.ValueKind == JsonValueKind.Object && (element.TryGetProperty("typeName", out var value) || element.TryGetProperty("fields", out value) || element.TryGetProperty("props", out value)))
		{
			try
			{
				serializedMember = Feeder.Json.JsonSerializer.Deserialize<SerializedMember>(element.GetRawText());
				if (serializedMember != null && element.TryGetProperty("value", out var value2))
				{
					serializedMember.valueJsonElement = value2;
				}
			}
			catch
			{
			}
		}
		if (serializedMember == null)
		{
			serializedMember = new SerializedMember
			{
				valueJsonElement = element
			};
		}
		return serializedMember;
	}

	public override bool TryModify(Reflector reflector, ref object? obj, SerializedMember data, Type type, int depth = 0, Logs? logs = null, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		if (data.valueJsonElement.HasValue)
		{
			return base.TryModify(reflector, ref obj, data, type, depth, logs, flags, logger);
		}
		List<SerializedMember> list = data.fields?.Where((SerializedMember f) => IsArrayIndexName(f?.name)).ToList();
		if (list == null || list.Count == 0)
		{
			return base.TryModify(reflector, ref obj, data, type, depth, logs, flags, logger);
		}
		if (obj == null)
		{
			logs?.Error("Cannot modify array elements: array is null.", depth);
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(StringUtils.GetPadding(depth) + "Cannot modify array elements: array is null.");
			}
			return false;
		}
		Type enumerableItemType = TypeUtils.GetEnumerableItemType(type);
		bool flag = true;
		if (obj is Array array)
		{
			foreach (SerializedMember item in list)
			{
				int num = ParseArrayIndex(item.name);
				if (num < 0 || num >= array.Length)
				{
					string text = $"Bracket segment '[{num}]' index out of range on type '{type.GetTypeShortName()}'. Array length is {array.Length}.";
					logs?.Error(text, depth);
					if (logger != null && logger.IsEnabled(LogLevel.Error))
					{
						logger.LogError(StringUtils.GetPadding(depth) + text);
					}
					flag = false;
				}
				else
				{
					object obj2 = array.GetValue(num);
					bool flag2 = reflector.TryModify(ref obj2, item, enumerableItemType, depth + 1, logs, flags, logger);
					if (flag2)
					{
						array.SetValue(obj2, num);
					}
					flag &= flag2;
				}
			}
		}
		else
		{
			if (!(obj is IList list2))
			{
				string text2 = "Cannot modify array elements: type '" + type.GetTypeShortName() + "' is not an array or list.";
				logs?.Error(text2, depth);
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(StringUtils.GetPadding(depth) + text2);
				}
				return false;
			}
			foreach (SerializedMember item2 in list)
			{
				int num2 = ParseArrayIndex(item2.name);
				if (num2 < 0 || num2 >= list2.Count)
				{
					string text3 = $"Bracket segment '[{num2}]' index out of range on type '{type.GetTypeShortName()}'. List count is {list2.Count}.";
					logs?.Error(text3, depth);
					if (logger != null && logger.IsEnabled(LogLevel.Error))
					{
						logger.LogError(StringUtils.GetPadding(depth) + text3);
					}
					flag = false;
				}
				else
				{
					object obj3 = list2[num2];
					bool flag3 = reflector.TryModify(ref obj3, item2, enumerableItemType, depth + 1, logs, flags, logger);
					if (flag3)
					{
						list2[num2] = obj3;
					}
					flag &= flag3;
				}
			}
		}
		return flag;
	}

	private static bool IsArrayIndexName(string? name)
	{
		if (string.IsNullOrEmpty(name) || name.Length < 3)
		{
			return false;
		}
		if (name[0] != '[' || name[name.Length - 1] != ']')
		{
			return false;
		}
		int result;
		return int.TryParse(name.Substring(1, name.Length - 2), out result);
	}

	private static int ParseArrayIndex(string name)
	{
		return int.Parse(name.Substring(1, name.Length - 2));
	}
}
}
