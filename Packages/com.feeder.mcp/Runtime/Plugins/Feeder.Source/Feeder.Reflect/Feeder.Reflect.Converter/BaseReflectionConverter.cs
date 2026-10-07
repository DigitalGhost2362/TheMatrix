using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Feeder.Json;
using Feeder.Reflect.Model;
using Feeder.Reflect.Utils;
using Feeder.Logging;

namespace Feeder.Reflect.Converter
{
public abstract class BaseReflectionConverter<T> : IReflectionConverter
{
	protected const int MAX_DEPTH = 10000;

	protected const int CACHE_CAPACITY = 10000;

	private readonly LruCache<(Type, BindingFlags), FieldInfo[]> _serializableFieldsCache = new LruCache<(Type, BindingFlags), FieldInfo[]>(10000);

	private readonly LruCache<(Type, BindingFlags), PropertyInfo[]> _serializablePropertiesCache = new LruCache<(Type, BindingFlags), PropertyInfo[]>(10000);

	private readonly LruCache<(Type, BindingFlags), (List<string> fieldNames, List<string> propertyNames)> _serializableMemberNamesCache = new LruCache<(Type, BindingFlags), (List<string>, List<string>)>(10000);

	public virtual bool AllowSetValue => true;

	public virtual bool AllowCascadeSerialization => true;

	public virtual bool AllowCascadeFieldsConversion => true;

	public virtual bool AllowCascadePropertiesConversion => true;

	public virtual bool AllowPointerFieldsAccess => true;

	public virtual bool AllowPointerPropertiesAccess => true;

	public void ClearReflectionCache(ILogger? logger = null)
	{
		logger?.LogDebug("Clearing reflection caches: {_serializableFieldsCacheCount} field entries, {_serializablePropertiesCacheCount} property entries, {_serializableMemberNamesCacheCount} member name entries.", _serializableFieldsCache.Count, _serializablePropertiesCache.Count, _serializableMemberNamesCache.Count);
		_serializableFieldsCache.Clear();
		_serializablePropertiesCache.Clear();
		_serializableMemberNamesCache.Clear();
	}

	public virtual bool TreatJsonObjectAsAtomicValue(Type type)
	{
		return false;
	}

	public virtual int SerializationPriority(Type type, ILogger? logger = null)
	{
		if (type == typeof(T))
		{
			return 10001;
		}
		int inheritanceDistance = TypeUtils.GetInheritanceDistance(typeof(T), type);
		if (inheritanceDistance < 0)
		{
			return 0;
		}
		return 10000 - inheritanceDistance;
	}

	public IEnumerable<FieldInfo>? GetSerializableFields(Reflector reflector, Type objType, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		(Type, BindingFlags) key = (objType, flags);
		FieldInfo[] orAdd = _serializableFieldsCache.GetOrAdd(key, delegate((Type, BindingFlags) tuple)
		{
			IEnumerable<string> ignoredFields = GetIgnoredFields();
			return GetSerializableFieldsInternal(reflector, tuple.Item1, tuple.Item2, logger)?.Where((FieldInfo field) => !ignoredFields.Contains(field.Name)).ToArray() ?? Array.Empty<FieldInfo>();
		});
		if (orAdd.Length == 0)
		{
			return null;
		}
		return orAdd;
	}

	protected virtual IEnumerable<FieldInfo>? GetSerializableFieldsInternal(Reflector reflector, Type objType, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		IEnumerable<FieldInfo> enumerable = from field in objType.GetFields(flags)
			where field.IsPublic
			where field.GetCustomAttribute<ObsoleteAttribute>() == null
			where field.GetCustomAttribute<NonSerializedAttribute>() == null
			select field;
		if (!AllowPointerFieldsAccess)
		{
			enumerable = enumerable.Where((FieldInfo field) => !field.FieldType.IsPointer);
		}
		return enumerable;
	}

	public IEnumerable<PropertyInfo>? GetSerializableProperties(Reflector reflector, Type objType, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		(Type, BindingFlags) key = (objType, flags);
		PropertyInfo[] orAdd = _serializablePropertiesCache.GetOrAdd(key, delegate((Type, BindingFlags) tuple)
		{
			IEnumerable<string> ignoredProperties = GetIgnoredProperties();
			return GetSerializablePropertiesInternal(reflector, tuple.Item1, tuple.Item2, logger)?.Where((PropertyInfo prop) => !ignoredProperties.Contains(prop.Name)).ToArray() ?? Array.Empty<PropertyInfo>();
		});
		if (orAdd.Length == 0)
		{
			return null;
		}
		return orAdd;
	}

	protected virtual IEnumerable<PropertyInfo>? GetSerializablePropertiesInternal(Reflector reflector, Type objType, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		IEnumerable<PropertyInfo> enumerable = from prop in objType.GetProperties(flags)
			where prop.CanRead
			where prop.GetCustomAttribute<ObsoleteAttribute>() == null
			where prop.GetIndexParameters().Length == 0
			select prop;
		if (!AllowPointerPropertiesAccess)
		{
			enumerable = enumerable.Where((PropertyInfo prop) => !prop.PropertyType.IsPointer);
		}
		return enumerable;
	}

	public virtual IEnumerable<string> GetAdditionalSerializableFields(Reflector reflector, Type objType, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		return Enumerable.Empty<string>();
	}

	public virtual IEnumerable<string> GetAdditionalSerializableProperties(Reflector reflector, Type objType, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		return Enumerable.Empty<string>();
	}

	public virtual object? CreateInstance(Reflector reflector, Type type)
	{
		if (reflector == null)
		{
			throw new ArgumentNullException("reflector");
		}
		if (type == null)
		{
			throw new ArgumentNullException("type");
		}
		if (type.IsEnum)
		{
			Array values = Enum.GetValues(type);
			if (values == null)
			{
				return Activator.CreateInstance(type);
			}
			if (values.Length <= 0)
			{
				return Activator.CreateInstance(type);
			}
			return values.GetValue(0);
		}
		if (type == typeof(string))
		{
			return string.Empty;
		}
		if (type == typeof(DateTime))
		{
			return DateTime.MinValue;
		}
		if (type == typeof(DateTimeOffset))
		{
			return DateTimeOffset.MinValue;
		}
		if (type == typeof(TimeSpan))
		{
			return TimeSpan.Zero;
		}
		if (type == typeof(Guid))
		{
			return Guid.Empty;
		}
		if (type.IsValueType)
		{
			return Activator.CreateInstance(type);
		}
		if (type.IsPrimitive)
		{
			return Activator.CreateInstance(type);
		}
		if (type.IsArray)
		{
			Type? elementType = type.GetElementType();
			if (elementType == null)
			{
				throw new ArgumentException("Array type '" + type.GetTypeId() + "' has no element type.");
			}
			return Array.CreateInstance(elementType, 0);
		}
		if (type.IsInterface || type.IsAbstract)
		{
			throw new InvalidOperationException("Cannot create instance of type '" + type.GetTypeId() + "' because it is an interface or abstract class.");
		}
		if (type.GetConstructor(Type.EmptyTypes) != null)
		{
			return Activator.CreateInstance(type);
		}
		ConstructorInfo constructorInfo = type.GetConstructors().FirstOrDefault();
		if (constructorInfo != null)
		{
			ParameterInfo[] parameters = constructorInfo.GetParameters();
			object[] array = new object[parameters.Length];
			for (int i = 0; i < parameters.Length; i++)
			{
				array[i] = reflector.CreateInstance(parameters[i].ParameterType);
			}
			return constructorInfo.Invoke(array);
		}
		try
		{
			return Activator.CreateInstance(type);
		}
		catch
		{
			throw new ArgumentException("Type '" + type.GetTypeId() + "' does not have a constructor or is not a value type or primitive type.");
		}
	}

	public virtual object? GetDefaultValue(Reflector reflector, Type type)
	{
		if (reflector == null)
		{
			throw new ArgumentNullException("reflector");
		}
		if (type == null)
		{
			throw new ArgumentNullException("type");
		}
		if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
		{
			return null;
		}
		if (type.IsValueType)
		{
			return Activator.CreateInstance(type);
		}
		if (type.IsEnum)
		{
			Array values = Enum.GetValues(type);
			if (values == null)
			{
				return Activator.CreateInstance(type);
			}
			if (values.Length <= 0)
			{
				return Activator.CreateInstance(type);
			}
			return values.GetValue(0);
		}
		return null;
	}

	public virtual object? Deserialize(Reflector reflector, SerializedMember data, Type? fallbackType = null, string? fallbackName = null, int depth = 0, Logs? logs = null, ILogger? logger = null, DeserializationContext? context = null)
	{
		if (reflector == null)
		{
			throw new ArgumentNullException("reflector");
		}
		if (data == null)
		{
			throw new ArgumentNullException("data");
		}
		if (!TryDeserializeValue(reflector, data, out object result, out Type type, fallbackType, depth, logs, logger))
		{
			return result;
		}
		string padding = StringUtils.GetPadding(depth);
		if (result != null)
		{
			context?.Register(result);
		}
		if (data.fields != null)
		{
			if (data.fields.Count > 0 && result == null)
			{
				result = CreateInstance(reflector, type);
			}
			if (logger != null && logger.IsEnabled(LogLevel.Trace))
			{
				logger.LogTrace(padding + "\ud83d\udd39 Deserialize 'fields' type='" + type?.GetTypeId().ValueOrNull() + "' name='" + (StringUtils.IsNullOrEmpty(data.name) ? fallbackName : data.name).ValueOrNull() + "'.");
			}
			foreach (SerializedMember field2 in data.fields)
			{
				if (string.IsNullOrEmpty(field2.name))
				{
					if (logger != null && logger.IsEnabled(LogLevel.Warning))
					{
						logger.LogWarning(padding + "⚠\ufe0f Field name is null or empty in serialized data: '" + (StringUtils.IsNullOrEmpty(data.name) ? fallbackName : data.name).ValueOrNull() + "'. Skipping.");
					}
					logs?.Warning("Field name is null or empty in serialized data: '" + (StringUtils.IsNullOrEmpty(data.name) ? fallbackName : data.name).ValueOrNull() + "'. Skipping.", depth);
					continue;
				}
				FieldInfo field = type.GetField(field2.name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field == null)
				{
					if (logger != null && logger.IsEnabled(LogLevel.Warning))
					{
						logger.LogWarning(padding + "⚠\ufe0f Field '" + field2.name + "' not found on type '" + type.GetTypeId() + "'.");
					}
					logs?.Warning("Field '" + field2.name + "' not found on type '" + type.GetTypeId() + "'.", depth);
				}
				else
				{
					object value = reflector.Deserialize(field2, field.FieldType, null, depth + 1, logs, logger, context);
					field.SetValue(result, value);
				}
			}
		}
		if (data.props != null)
		{
			if (data.props.Count > 0 && result == null)
			{
				result = CreateInstance(reflector, type);
			}
			if (logger != null && logger.IsEnabled(LogLevel.Trace))
			{
				logger.LogTrace(padding + "\ud83d\udd38 Deserialize 'props' type='" + type?.GetTypeId().ValueOrNull() + "' name='" + (StringUtils.IsNullOrEmpty(data.name) ? fallbackName : data.name).ValueOrNull() + "'.");
			}
			foreach (SerializedMember prop in data.props)
			{
				if (string.IsNullOrEmpty(prop.name))
				{
					if (logger != null && logger.IsEnabled(LogLevel.Warning))
					{
						logger.LogWarning(padding + "⚠\ufe0f Property name is null or empty in serialized data: '" + (StringUtils.IsNullOrEmpty(data.name) ? fallbackName : data.name).ValueOrNull() + "'. Skipping.");
					}
					logs?.Warning("Property name is null or empty in serialized data: '" + (StringUtils.IsNullOrEmpty(data.name) ? fallbackName : data.name).ValueOrNull() + "'. Skipping.", depth);
					continue;
				}
				PropertyInfo property = type.GetProperty(prop.name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (property == null)
				{
					if (logger != null && logger.IsEnabled(LogLevel.Warning))
					{
						logger.LogWarning(padding + "⚠\ufe0f Property '" + prop.name + "' not found on type '" + type.GetTypeId() + "'.");
					}
					logs?.Warning("Property '" + prop.name + "' not found on type '" + type.GetTypeId() + "'.", depth);
				}
				else if (!property.CanWrite)
				{
					if (logger != null && logger.IsEnabled(LogLevel.Warning))
					{
						logger.LogWarning(padding + "⚠\ufe0f Property '" + prop.name + "' on type '" + type.GetTypeId() + "' is read-only and cannot be set.");
					}
					logs?.Warning("Property '" + prop.name + "' on type '" + type.GetTypeId() + "' is read-only and cannot be set.", depth);
				}
				else
				{
					object value2 = reflector.Deserialize(prop, property.PropertyType, null, depth + 1, logs, logger, context);
					property.SetValue(result, value2);
				}
			}
		}
		return result;
	}

	protected virtual bool TryDeserializeValue(Reflector reflector, SerializedMember? data, out object? result, out Type? type, Type? fallbackType = null, int depth = 0, Logs? logs = null, ILogger? logger = null)
	{
		if (reflector == null)
		{
			throw new ArgumentNullException("reflector");
		}
		if (data == null)
		{
			result = null;
			type = null;
			return false;
		}
		string padding = StringUtils.GetPadding(depth);
		type = TypeUtils.GetTypeWithNamePriority(data, fallbackType, out string error);
		if (type == null)
		{
			result = null;
			logs?.Error(error ?? "Unknown error", depth);
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(padding + error);
			}
			return false;
		}
		if (logger != null && logger.IsEnabled(LogLevel.Trace))
		{
			logger.LogTrace(padding + "⚪ Deserialize 'value', type='" + type.GetTypeId() + "' name='" + data.name.ValueOrNull() + "'.");
		}
		bool flag = TryDeserializeValueInternal(reflector, data, out result, type, depth, logs, logger);
		if (flag)
		{
			if (logger != null && logger.IsEnabled(LogLevel.Trace))
			{
				logger.LogTrace(padding + "\ud83d\udfe2 Deserialized '" + type.GetTypeId() + "'.");
			}
		}
		else if (logger != null && logger.IsEnabled(LogLevel.Error))
		{
			logger.LogError(padding + "❌ Deserialization '" + type.GetTypeId() + "' failed. Converter: " + GetType().GetTypeShortName());
		}
		return flag;
	}

	protected virtual bool TryDeserializeValueInternal(Reflector reflector, SerializedMember data, out object? result, Type type, int depth = 0, Logs? logs = null, ILogger? logger = null)
	{
		if (reflector == null)
		{
			throw new ArgumentNullException("reflector");
		}
		if (data == null)
		{
			throw new ArgumentNullException("data");
		}
		if (type == null)
		{
			throw new ArgumentNullException("type");
		}
		string padding = StringUtils.GetPadding(depth);
		if (AllowCascadeSerialization)
		{
			try
			{
				if (!data.valueJsonElement.HasValue)
				{
					if (logger != null && logger.IsEnabled(LogLevel.Trace))
					{
						logger.LogTrace(padding + "'value' is null. Converter: " + GetType().GetTypeShortName());
					}
					result = GetDefaultValue(reflector, type);
					return true;
				}
				if (data.valueJsonElement.Value.ValueKind != JsonValueKind.Object)
				{
					if (logger != null && logger.IsEnabled(LogLevel.Error))
					{
						logger.LogError($"{padding}'value' is not an object. It is '{data.valueJsonElement?.ValueKind}'. Converter: {GetType().GetTypeShortName()}");
					}
					logs?.Error("'value' is not an object. Attempting to deserialize as SerializedMember.", depth);
					result = reflector.GetDefaultValue(type);
					return false;
				}
				result = data.valueJsonElement.DeserializeValueSerializedMember(reflector, type, data.name, depth + 1, logs, logger);
				return true;
			}
			catch (JsonException ex)
			{
				if (logger != null && logger.IsEnabled(LogLevel.Warning))
				{
					logger.LogWarning(padding + "⚠\ufe0f Deserialize 'value', type='" + type.GetTypeId() + "' name='" + data.name.ValueOrNull() + "':\n" + padding + ex.Message + "\n" + ex.StackTrace);
				}
				logs?.Warning("Failed to deserialize member '" + data.name.ValueOrNull() + "' of type '" + type.GetTypeId() + "':\n" + ex.Message, depth);
			}
			catch (NotSupportedException ex2)
			{
				if (logger != null && logger.IsEnabled(LogLevel.Warning))
				{
					logger.LogWarning(padding + "⚠\ufe0f Deserialize 'value', type='" + type.GetTypeId() + "' name='" + data.name.ValueOrNull() + "':\n" + padding + ex2.Message + "\n" + ex2.StackTrace);
				}
				logs?.Warning("Unsupported type '" + type.GetTypeId() + "' for member '" + data.name.ValueOrNull() + "':\n" + ex2.Message, depth);
			}
			result = reflector.GetDefaultValue(type);
			return false;
		}
		try
		{
			if (logger != null && logger.IsEnabled(LogLevel.Trace))
			{
				logger.LogTrace(padding + "Deserialize as json. Converter: " + GetType().GetTypeShortName());
			}
			result = DeserializeValueAsJsonElement(reflector, data, type, depth, logs, logger);
			if (logger != null && logger.IsEnabled(LogLevel.Trace))
			{
				logger.LogTrace(string.Format("{0}{1} Deserialized as json: {2}", padding, "\ud83d\udfe2", data.valueJsonElement));
			}
			return true;
		}
		catch (Exception ex3)
		{
			logs?.Error("Failed to deserialize value'" + data.name.ValueOrNull() + "' of type '" + type.GetTypeId() + "':\n" + ex3.Message, depth);
			if (logger != null && logger.IsEnabled(LogLevel.Critical))
			{
				logger.LogCritical(padding + "❌ Deserialize 'value', type='" + type.GetTypeId() + "' name='" + data.name.ValueOrNull() + "':\n" + padding + ex3.Message + "\n" + ex3.StackTrace);
			}
			result = reflector.GetDefaultValue(type);
			return false;
		}
	}

	protected virtual object? DeserializeValueAsJsonElement(Reflector reflector, SerializedMember data, Type type, int depth = 0, Logs? logs = null, ILogger? logger = null)
	{
		if (reflector == null)
		{
			throw new ArgumentNullException("reflector");
		}
		if (data == null)
		{
			throw new ArgumentNullException("data");
		}
		if (type == null)
		{
			throw new ArgumentNullException("type");
		}
		return reflector.JsonSerializer.Deserialize(reflector, data.valueJsonElement, type);
	}

	private (List<string> fieldNames, List<string> propertyNames) GetCachedSerializableMemberNames(Reflector reflector, Type objType, BindingFlags flags, ILogger? logger)
	{
		(Type, BindingFlags) key = (objType, flags);
		return _serializableMemberNamesCache.GetOrAdd(key, delegate((Type, BindingFlags) tuple)
		{
			List<string> item = GetSerializableFields(reflector, tuple.Item1, tuple.Item2, logger)?.Select((FieldInfo f) => f.Name)?.Concat(GetAdditionalSerializableFields(reflector, tuple.Item1, tuple.Item2, logger))?.ToList() ?? new List<string>();
			List<string> item2 = GetSerializableProperties(reflector, tuple.Item1, tuple.Item2, logger)?.Select((PropertyInfo f) => f.Name)?.Concat(GetAdditionalSerializableProperties(reflector, tuple.Item1, tuple.Item2, logger))?.ToList() ?? new List<string>();
			return (fieldNames: item, propertyNames: item2);
		});
	}

	public virtual bool TryModify(Reflector reflector, ref object? obj, SerializedMember data, Type type, int depth = 0, Logs? logs = null, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		if (reflector == null)
		{
			throw new ArgumentNullException("reflector");
		}
		if (data == null)
		{
			throw new ArgumentNullException("data");
		}
		if (type == null)
		{
			throw new ArgumentNullException("type");
		}
		string padding = StringUtils.GetPadding(depth);
		if (obj == null)
		{
			obj = reflector.Deserialize(data, type, null, depth, logs, logger);
			if (obj == null)
			{
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + "Object '" + data.name.ValueOrNull() + "' modification failed: Object is null. Instance creation failed for type '" + type.GetTypeId() + "'.");
				}
				logs?.Error("Object '" + data.name.ValueOrNull() + "' modification failed: Object is null. Instance creation failed for type '" + type.GetTypeId() + "'.", depth);
				return false;
			}
			if (logger != null && logger.IsEnabled(LogLevel.Trace))
			{
				logger.LogTrace(padding + "Object '" + data.name.ValueOrNull() + "' modified with type '" + type.GetTypeId() + "'.");
			}
			logs?.Success("Object '" + data.name.ValueOrNull() + "' modified with type '" + type.GetTypeId() + "'.", depth);
			return true;
		}
		if (!TypeUtils.IsCastable(obj.GetType(), type))
		{
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(padding + "Type mismatch: '" + data.typeName + "' vs '" + obj.GetType().GetTypeId().ValueOrNull() + "'.");
			}
			logs?.Error("Type mismatch: '" + data.typeName + "' vs '" + obj.GetType().GetTypeId().ValueOrNull() + "'.", depth);
			return false;
		}
		bool flag = true;
		SerializedMemberList? fields = data.fields;
		int num;
		if (fields == null || fields.Count <= 0)
		{
			SerializedMemberList? props = data.props;
			num = ((props != null && props.Count > 0) ? 1 : 0);
		}
		else
		{
			num = 1;
		}
		bool flag2 = (byte)num != 0;
		if (AllowSetValue && (data.valueJsonElement.HasValue || !flag2))
		{
			try
			{
				bool flag3 = SetValue(reflector, ref obj, type, data.valueJsonElement, depth, logs, logger);
				flag &= flag3;
				if (flag3)
				{
					if (logger != null && logger.IsEnabled(LogLevel.Information))
					{
						logger.LogInformation($"{padding}[Success] Value '{obj}' modified to\n{padding}```json\n{data.valueJsonElement}\n{padding}```");
					}
					logs?.Success($"Value '{obj}' modified to\n```json\n{data.valueJsonElement}\n```", depth);
				}
				else
				{
					if (logger != null && logger.IsEnabled(LogLevel.Warning))
					{
						logger.LogWarning($"{padding}Value '{obj}' was not modified to value \n{padding}```json\n{data.valueJsonElement}\n{padding}```");
					}
					logs?.Warning($"Value '{obj}' was not modified to value \n```json\n{data.valueJsonElement}\n```", depth);
				}
			}
			catch (Exception ex)
			{
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(ex, $"{padding}Value '{obj}' modification failed: {ex.Message}");
				}
				logs?.Error($"Value '{obj}' modification failed: {ex.Message}", depth);
			}
		}
		int depth2 = depth + 1;
		string padding2 = StringUtils.GetPadding(depth2);
		if (data.fields != null)
		{
			foreach (SerializedMember field in data.fields)
			{
				bool flag4 = TryModifyField(reflector, ref obj, type, field, depth2, logs, flags, logger);
				flag &= flag4;
				if (flag4)
				{
					if (logger != null && logger.IsEnabled(LogLevel.Information))
					{
						logger.LogInformation(padding2 + "[Success] Field '" + field.name + "' modified.");
					}
					logs?.Success("Field '" + field.name + "' modified.", depth2);
				}
				else
				{
					if (logger != null && logger.IsEnabled(LogLevel.Warning))
					{
						logger.LogWarning(padding2 + "Field '" + field.name + "' was not modified.");
					}
					logs?.Warning("Field '" + field.name + "' was not modified.", depth2);
				}
			}
		}
		SerializedMemberList? fields2 = data.fields;
		if (fields2 == null || fields2.Count == 0)
		{
			if (logger != null && logger.IsEnabled(LogLevel.Information))
			{
				logger.LogInformation(padding2 + "No fields modified.");
			}
			logs?.Info("No fields modified.", depth2);
		}
		if (data.props != null)
		{
			foreach (SerializedMember prop in data.props)
			{
				bool flag5 = TryModifyProperty(reflector, ref obj, type, prop, depth2, logs, flags, logger);
				flag &= flag5;
				if (flag5)
				{
					if (logger != null && logger.IsEnabled(LogLevel.Information))
					{
						logger.LogInformation(padding2 + "[Success] Property '" + prop.name + "' modified.");
					}
					logs?.Success("Property '" + prop.name + "' modified.", depth2);
				}
				else
				{
					if (logger != null && logger.IsEnabled(LogLevel.Warning))
					{
						logger.LogWarning(padding2 + "Property '" + prop.name + "' was not modified.");
					}
					logs?.Warning("Property '" + prop.name + "' was not modified.", depth2);
				}
			}
		}
		SerializedMemberList? props2 = data.props;
		if (props2 == null || props2.Count == 0)
		{
			if (logger != null && logger.IsEnabled(LogLevel.Information))
			{
				logger.LogInformation(padding2 + "No properties modified.");
			}
			logs?.Info("No properties modified.", depth2);
		}
		return flag;
	}

	protected abstract bool SetValue(Reflector reflector, ref object? obj, Type type, JsonElement? value, int depth = 0, Logs? logs = null, ILogger? logger = null);

	protected virtual bool TryModifyField(Reflector reflector, ref object obj, Type objType, SerializedMember fieldValue, int depth = 0, Logs? logs = null, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		if (reflector == null)
		{
			throw new ArgumentNullException("reflector");
		}
		if (obj == null)
		{
			throw new ArgumentNullException("obj");
		}
		if (objType == null)
		{
			throw new ArgumentNullException("objType");
		}
		if (fieldValue == null)
		{
			throw new ArgumentNullException("fieldValue");
		}
		string padding = StringUtils.GetPadding(depth);
		if (string.IsNullOrEmpty(fieldValue.name))
		{
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(padding + "Field name is null or empty in serialized data: '" + fieldValue.name.ValueOrNull() + "'. Skipping.");
			}
			logs?.Error(Reflector.Error.FieldNameIsEmpty(), depth);
			return false;
		}
		FieldInfo field = TypeMemberUtils.GetField(objType, flags, fieldValue.name);
		if (field == null)
		{
			(List<string> fieldNames, List<string> propertyNames) cachedSerializableMemberNames = GetCachedSerializableMemberNames(reflector, objType, flags, logger);
			List<string> item = cachedSerializableMemberNames.fieldNames;
			List<string> item2 = cachedSerializableMemberNames.propertyNames;
			int count = item.Count;
			int count2 = item2.Count;
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(padding + "Field '" + fieldValue.name.ValueOrNull() + "' not found. Make sure the name is right, it is case sensitive. Make sure this is a field, maybe is it a property?\n" + padding + ((count > 0) ? ("Available fields: " + string.Join(", ", item)) : "No available fields.") + "\n" + padding + ((count2 > 0) ? ("Available properties: " + string.Join(", ", item2)) : "No available properties."));
			}
			logs?.Error("Field '" + fieldValue.name.ValueOrNull() + "'. Make sure the name is right, it is case sensitive. Make sure this is a field, maybe is it a property?\n" + ((count > 0) ? ("Available fields: " + string.Join(", ", item)) : "No available fields.") + "\n" + ((count2 > 0) ? ("Available properties: " + string.Join(", ", item2)) : "No available properties."), depth);
			return false;
		}
		Type typeWithNamePriority = TypeUtils.GetTypeWithNamePriority(fieldValue, field.FieldType, out string error);
		if (typeWithNamePriority == null)
		{
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(padding + "Field '" + fieldValue.name.ValueOrNull() + "'. " + error);
			}
			logs?.Error("Field '" + fieldValue.name.ValueOrNull() + "'. " + error, depth);
			return false;
		}
		try
		{
			if (logger != null && logger.IsEnabled(LogLevel.Trace))
			{
				logger.LogTrace(padding + "Modify field type='" + field.FieldType.GetTypeShortName() + "', name='" + field.Name.ValueOrNull() + "'. Converter='" + GetType().GetTypeShortName() + "'.");
			}
			logs?.Info("Modify field type='" + field.FieldType.GetTypeId().ValueOrNull() + "', name='" + field.Name.ValueOrNull() + "'. Converter='" + GetType().GetTypeShortName() + "'.", depth);
			object obj2 = field.GetValue(obj);
			if (!reflector.TryModify(ref obj2, fieldValue, typeWithNamePriority, depth + 1, logs, flags, logger))
			{
				if (logger != null && logger.IsEnabled(LogLevel.Warning))
				{
					logger.LogWarning(padding + "Field '" + fieldValue.name.ValueOrNull() + "' was not modified.");
				}
				logs?.Warning("Field '" + fieldValue.name.ValueOrNull() + "' was not modified.", depth);
				return false;
			}
			field.SetValue(obj, obj2);
			if (logger != null && logger.IsEnabled(LogLevel.Information))
			{
				logger.LogInformation(padding + "[Success] Field '" + fieldValue.name.ValueOrNull() + "' modified.");
			}
			logs?.Success("Field '" + fieldValue.name.ValueOrNull() + "' modified.", depth);
			return true;
		}
		catch (Exception ex)
		{
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(ex, padding + "Field '" + fieldValue.name.ValueOrNull() + "' modification failed: " + ex.Message);
			}
			logs?.Error("Field '" + fieldValue.name.ValueOrNull() + "' modification failed: " + ex.Message, depth);
			return false;
		}
	}

	protected virtual bool TryModifyProperty(Reflector reflector, ref object obj, Type objType, SerializedMember propertyValue, int depth = 0, Logs? logs = null, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		if (reflector == null)
		{
			throw new ArgumentNullException("reflector");
		}
		if (obj == null)
		{
			throw new ArgumentNullException("obj");
		}
		if (objType == null)
		{
			throw new ArgumentNullException("objType");
		}
		if (propertyValue == null)
		{
			throw new ArgumentNullException("propertyValue");
		}
		string padding = StringUtils.GetPadding(depth);
		if (string.IsNullOrEmpty(propertyValue.name))
		{
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(padding + "Property name is null or empty in serialized data: '" + propertyValue.name.ValueOrNull() + "'. Skipping.");
			}
			logs?.Error("Property name is null or empty in serialized data: '" + propertyValue.name.ValueOrNull() + "'. Skipping.", depth);
			return false;
		}
		PropertyInfo property = TypeMemberUtils.GetProperty(objType, flags, propertyValue.name);
		if (property == null)
		{
			(List<string> fieldNames, List<string> propertyNames) cachedSerializableMemberNames = GetCachedSerializableMemberNames(reflector, objType, flags, logger);
			List<string> item = cachedSerializableMemberNames.fieldNames;
			List<string> item2 = cachedSerializableMemberNames.propertyNames;
			int count = item.Count;
			int count2 = item2.Count;
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(padding + "Property '" + propertyValue.name.ValueOrNull() + "' not found. Make sure the name is right, it is case sensitive. Make sure this is a property, maybe is it a field?\n" + padding + ((count2 > 0) ? ("Available properties: " + string.Join(", ", item2)) : "No available properties.") + "\n" + padding + ((count > 0) ? ("Available fields: " + string.Join(", ", item)) : "No available fields."));
			}
			logs?.Error("Property '" + propertyValue.name.ValueOrNull() + "'. Make sure the name is right, it is case sensitive. Make sure this is a property, maybe is it a field?\n" + ((count2 > 0) ? ("Available properties: " + string.Join(", ", item2)) : "No available properties.") + "\n" + ((count > 0) ? ("Available fields: " + string.Join(", ", item)) : "No available fields."), depth);
			return false;
		}
		Type typeWithNamePriority = TypeUtils.GetTypeWithNamePriority(propertyValue, property.PropertyType, out string error);
		if (typeWithNamePriority == null)
		{
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(padding + "Property '" + propertyValue.name.ValueOrNull() + "'. " + error);
			}
			logs?.Error("Property '" + propertyValue.name.ValueOrNull() + "'. " + error, depth);
			return false;
		}
		try
		{
			if (logger != null && logger.IsEnabled(LogLevel.Trace))
			{
				logger.LogTrace(padding + "Modify property type='" + property.PropertyType.GetTypeId().ValueOrNull() + "', name='" + property.Name.ValueOrNull() + "'. Converter='" + GetType().GetTypeShortName() + "'.");
			}
			logs?.Info("Modify property type='" + property.PropertyType.GetTypeId().ValueOrNull() + "', name='" + property.Name.ValueOrNull() + "'. Converter='" + GetType().GetTypeShortName() + "'.", depth);
			object obj2 = property.GetValue(obj);
			bool flag = reflector.TryModify(ref obj2, propertyValue, typeWithNamePriority, depth + 1, logs, flags, logger);
			if (!flag)
			{
				if (logger != null && logger.IsEnabled(LogLevel.Warning))
				{
					logger.LogWarning(padding + "Property '" + propertyValue.name.ValueOrNull() + "' was not modified.");
				}
				logs?.Warning("Property '" + propertyValue.name.ValueOrNull() + "' was not modified.", depth);
				return false;
			}
			property.SetValue(obj, obj2);
			if (logger != null && logger.IsEnabled(LogLevel.Information))
			{
				logger.LogInformation(padding + "[Success] Property '" + propertyValue.name.ValueOrNull() + "' modified.");
			}
			logs?.Success("Property '" + propertyValue.name.ValueOrNull() + "' modified.", depth);
			return flag;
		}
		catch (Exception ex)
		{
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(ex, padding + "Property '" + propertyValue.name.ValueOrNull() + "' modification failed: " + ex.Message);
			}
			logs?.Error("Property '" + propertyValue.name.ValueOrNull() + "' modification failed: " + ex.Message, depth);
			return false;
		}
	}

	public abstract bool SetField(Reflector reflector, ref object? obj, Type fallbackType, FieldInfo fieldInfo, SerializedMember? value, int depth = 0, Logs? logs = null, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null);

	public abstract bool SetProperty(Reflector reflector, ref object? obj, Type fallbackType, PropertyInfo propertyInfo, SerializedMember? value, int depth = 0, Logs? logs = null, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null);

	protected virtual IEnumerable<string> GetIgnoredFields()
	{
		return Enumerable.Empty<string>();
	}

	protected virtual IEnumerable<string> GetIgnoredProperties()
	{
		return Enumerable.Empty<string>();
	}

	public virtual SerializedMember Serialize(Reflector reflector, object? obj, Type? fallbackType = null, string? name = null, bool recursive = true, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, int depth = 0, Logs? logs = null, ILogger? logger = null, SerializationContext? context = null)
	{
		if (reflector == null)
		{
			throw new ArgumentNullException("reflector");
		}
		Type type = fallbackType ?? obj?.GetType() ?? typeof(T);
		return InternalSerialize(reflector, obj, type, name, recursive, flags, depth, logs, logger, context);
	}

	protected virtual SerializedMemberList? SerializeFields(Reflector reflector, object obj, BindingFlags flags, int depth = 0, Logs? logs = null, ILogger? logger = null, SerializationContext? context = null)
	{
		if (reflector == null)
		{
			throw new ArgumentNullException("reflector");
		}
		if (obj == null)
		{
			throw new ArgumentNullException("obj");
		}
		SerializedMemberList serializedMemberList = null;
		Type type = obj.GetType();
		IEnumerable<FieldInfo> serializableFields = GetSerializableFields(reflector, type, flags, logger);
		if (serializableFields == null)
		{
			return null;
		}
		foreach (FieldInfo item2 in serializableFields)
		{
			Type fieldType = item2.FieldType;
			if (reflector.Converters.IsTypeBlacklisted(fieldType))
			{
				if (logger != null && logger.IsEnabled(LogLevel.Trace))
				{
					logger.LogTrace("{padding}Skipping serialization of field '{fieldName}' of type '{type}' in '{objType}' because its type is blacklisted.\nPath: {path}", StringUtils.GetPadding(depth), item2.Name, fieldType.GetTypeId(), type.GetTypeId(), context?.GetPath(obj));
				}
				continue;
			}
			try
			{
				if (logger != null && logger.IsEnabled(LogLevel.Trace))
				{
					logger.LogTrace("{padding}Serializing field '{fieldName}' of type '{type}' in '{objType}'.\nPath: {path}", StringUtils.GetPadding(depth), item2.Name, fieldType.GetTypeId(), type.GetTypeId(), context?.GetPath(obj));
				}
				object value = item2.GetValue(obj);
				SerializedMember item = SerializeField(reflector, obj, item2, value, flags, depth, logs, logger, context);
				if (serializedMemberList == null)
				{
					serializedMemberList = new SerializedMemberList();
				}
				serializedMemberList.Add(item);
			}
			catch (Exception ex)
			{
				if (logger != null && logger.IsEnabled(LogLevel.Warning))
				{
					logger.LogWarning(ex.GetBaseException(), "{padding}Failed to serialize field '{fieldName}' of type '{type}' in '{objType}'. Converter: {converter}. Path: {path}", StringUtils.GetPadding(depth), item2.Name, fieldType.GetTypeId(), type.GetTypeId(), GetType().GetTypeShortName(), context?.GetPath(obj));
				}
			}
		}
		return serializedMemberList;
	}

	protected virtual SerializedMemberList? SerializeProperties(Reflector reflector, object obj, BindingFlags flags, int depth = 0, Logs? logs = null, ILogger? logger = null, SerializationContext? context = null)
	{
		if (reflector == null)
		{
			throw new ArgumentNullException("reflector");
		}
		if (obj == null)
		{
			throw new ArgumentNullException("obj");
		}
		SerializedMemberList serializedMemberList = null;
		Type type = obj.GetType();
		IEnumerable<PropertyInfo> serializableProperties = GetSerializableProperties(reflector, type, flags, logger);
		if (serializableProperties == null)
		{
			return null;
		}
		foreach (PropertyInfo item2 in serializableProperties)
		{
			Type propertyType = item2.PropertyType;
			if (reflector.Converters.IsTypeBlacklisted(propertyType))
			{
				if (logger != null && logger.IsEnabled(LogLevel.Trace))
				{
					logger.LogTrace("{padding}Skipping serialization of property '{propertyName}' of type '{type}' in '{objType}' because its type is blacklisted.\nPath: {path}", StringUtils.GetPadding(depth), item2.Name, propertyType.GetTypeId(), type.GetTypeId(), context?.GetPath(obj));
				}
				continue;
			}
			try
			{
				if (logger != null && logger.IsEnabled(LogLevel.Trace))
				{
					logger.LogTrace("{padding}Serializing property '{propertyName}' of type '{type}' in '{objType}'.\nPath: {path}", StringUtils.GetPadding(depth), item2.Name, propertyType.GetTypeId(), type.GetTypeId(), context?.GetPath(obj));
				}
				object value = item2.GetValue(obj);
				SerializedMember item = SerializeProperty(reflector, obj, item2, value, flags, depth, logs, logger, context);
				if (serializedMemberList == null)
				{
					serializedMemberList = new SerializedMemberList();
				}
				serializedMemberList.Add(item);
			}
			catch (Exception ex)
			{
				if (logger != null && logger.IsEnabled(LogLevel.Warning))
				{
					logger.LogWarning(ex.GetBaseException(), "{padding}Failed to serialize property '{propertyName}' of type '{type}' in '{objType}'. Converter: {converter}. Path: {path}", StringUtils.GetPadding(depth), item2.Name, propertyType.GetTypeId(), type.GetTypeId(), GetType().GetTypeShortName(), context?.GetPath(obj));
				}
			}
		}
		return serializedMemberList;
	}

	protected abstract SerializedMember InternalSerialize(Reflector reflector, object? obj, Type type, string? name = null, bool recursive = true, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, int depth = 0, Logs? logs = null, ILogger? logger = null, SerializationContext? context = null);

	protected virtual SerializedMember SerializeField(Reflector reflector, object obj, FieldInfo field, object? value, BindingFlags flags, int depth, Logs? logs, ILogger? logger, SerializationContext? context)
	{
		return reflector.Serialize(value, field.FieldType, field.Name, AllowCascadeFieldsConversion, flags, depth, logs, logger, context);
	}

	protected virtual SerializedMember SerializeProperty(Reflector reflector, object obj, PropertyInfo property, object? value, BindingFlags flags, int depth, Logs? logs, ILogger? logger, SerializationContext? context)
	{
		return reflector.Serialize(value, property.PropertyType, property.Name, AllowCascadePropertiesConversion, flags, depth, logs, logger, context);
	}
}
}
