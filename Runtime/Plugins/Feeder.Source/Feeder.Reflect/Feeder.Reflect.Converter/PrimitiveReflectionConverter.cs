using System;
using System.Collections.Generic;
using System.Reflection;
using Feeder.Json;
using Feeder.Reflect.Model;
using Feeder.Reflect.Utils;
using Feeder.Logging;

namespace Feeder.Reflect.Converter
{
public class PrimitiveReflectionConverter : NotArrayReflectionConverter<object>
{
	public override bool AllowCascadeSerialization => false;

	public override int SerializationPriority(Type type, ILogger? logger = null)
	{
		if (!TypeUtils.IsPrimitive(type))
		{
			return 0;
		}
		return 10001;
	}

	protected override SerializedMember InternalSerialize(Reflector reflector, object? obj, Type type, string? name = null, bool recursive = true, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, int depth = 0, Logs? logs = null, ILogger? logger = null, SerializationContext? context = null)
	{
		if (obj == null)
		{
			return SerializedMember.Null(type, name);
		}
		return SerializedMember.FromValue(reflector, type, obj, name);
	}

	protected override IEnumerable<FieldInfo>? GetSerializableFieldsInternal(Reflector reflector, Type objType, BindingFlags flags, ILogger? logger = null)
	{
		return null;
	}

	protected override IEnumerable<PropertyInfo>? GetSerializablePropertiesInternal(Reflector reflector, Type objType, BindingFlags flags, ILogger? logger = null)
	{
		return null;
	}

	protected override bool SetValue(Reflector reflector, ref object? obj, Type type, JsonElement? value, int depth = 0, Logs? logs = null, ILogger? logger = null)
	{
		object newValue = value.Deserialize(type, reflector);
		Print.SetNewValue(ref obj, ref newValue, type, depth, logs, logger);
		obj = newValue;
		return true;
	}

	public override bool SetField(Reflector reflector, ref object? obj, Type fallbackType, FieldInfo fieldInfo, SerializedMember? value, int depth = 0, Logs? logs = null, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		if (!TryDeserializeValue(reflector, value, out object result, out Type type, fallbackType, depth, logs, logger))
		{
			logs?.Error("Failed to deserialize value for field '" + value?.name.ValueOrNull() + "'.", depth);
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
			logs?.Error("Failed to deserialize value for property '" + value?.name.ValueOrNull() + "'.", depth);
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
