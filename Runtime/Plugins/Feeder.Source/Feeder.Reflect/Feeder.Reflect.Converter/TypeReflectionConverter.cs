using System;
using System.Reflection;
using Feeder.Json;
using Feeder.Reflect.Model;
using Feeder.Reflect.Utils;
using Feeder.Logging;

namespace Feeder.Reflect.Converter
{
public class TypeReflectionConverter : IgnoreFieldsAndPropertiesReflectionConverter<Type>
{
	public TypeReflectionConverter()
		: base(true, true)
	{
	}

	protected override SerializedMember InternalSerialize(Reflector reflector, object? obj, Type type, string? name = null, bool recursive = true, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, int depth = 0, Logs? logs = null, ILogger? logger = null, SerializationContext? context = null)
	{
		if (obj is Type type2)
		{
			string typeId = type2.GetTypeId();
			return SerializedMember.FromValue(reflector, type, typeId, name);
		}
		return base.InternalSerialize(reflector, obj, type, name, recursive, flags, depth, logs, logger, context);
	}

	public override object? CreateInstance(Reflector reflector, Type type)
	{
		return typeof(object);
	}

	protected override bool TryDeserializeValueInternal(Reflector reflector, SerializedMember data, out object? result, Type type, int depth = 0, Logs? logs = null, ILogger? logger = null)
	{
		result = null;
		if (!data.valueJsonElement.HasValue)
		{
			return false;
		}
		try
		{
			string text = data.valueJsonElement.Value.GetString();
			if (!string.IsNullOrEmpty(text))
			{
				Type type2 = TypeUtils.GetType(text);
				if (type2 != null)
				{
					result = type2;
					return true;
				}
			}
		}
		catch (Exception ex)
		{
			logger?.LogError("Failed to deserialize Type from value '{Value}': {Message}", data.valueJsonElement, ex.Message);
			return false;
		}
		return false;
	}

	protected override bool SetValue(Reflector reflector, ref object? obj, Type type, JsonElement? value, int depth = 0, Logs? logs = null, ILogger? logger = null)
	{
		if (!value.HasValue)
		{
			return false;
		}
		try
		{
			string text = value.Value.GetString();
			if (!string.IsNullOrEmpty(text))
			{
				Type type2 = TypeUtils.GetType(text);
				if (type2 != null)
				{
					obj = type2;
					return true;
				}
			}
		}
		catch (Exception ex)
		{
			logger?.LogError("Failed to deserialize Type from value '{Value}': {Message}", value, ex.Message);
			return false;
		}
		return false;
	}
}
}
