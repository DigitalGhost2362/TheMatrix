using System;
using System.Reflection;
using Feeder.Json;
using Feeder.Json.Nodes;
using Feeder.Reflect.Model;
using Feeder.Reflect.Utils;
using Feeder.Logging;

namespace Feeder.Reflect.Converter
{
public class GenericReflectionConverter<T> : NotArrayReflectionConverter<T>
{
	protected override SerializedMember InternalSerialize(Reflector reflector, object? obj, Type type, string? name = null, bool recursive = true, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, int depth = 0, Logs? logs = null, ILogger? logger = null, SerializationContext? context = null)
	{
		if (obj == null)
		{
			return SerializedMember.Null(type, name);
		}
		bool flag = type.IsValueType && !type.IsPrimitive && !type.IsEnum;
		if (type.IsClass | flag)
		{
			if (recursive)
			{
				return new SerializedMember
				{
					name = name,
					typeName = (type.GetTypeId() ?? string.Empty),
					fields = base.SerializeFields(reflector, obj, flags, depth + 1, logs, logger, context),
					props = base.SerializeProperties(reflector, obj, flags, depth + 1, logs, logger, context),
					valueJsonElement = new JsonObject().ToJsonElement()
				};
			}
			return SerializedMember.FromJson(type, obj.ToJson(reflector, null, depth, logger), name);
		}
		throw new ArgumentException("Unsupported type: '" + type.GetTypeId() + "' for converter '" + GetType().GetTypeShortName() + "'.");
	}

	protected override bool SetValue(Reflector reflector, ref object? obj, Type type, JsonElement? value, int depth = 0, Logs? logs = null, ILogger? logger = null)
	{
		object newValue = value.Deserialize(type, reflector);
		Print.SetNewValue(ref obj, ref newValue, type, depth, logs);
		obj = newValue;
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
		StringUtils.GetPadding(depth);
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
}
}
