using System;
using System.Collections.Generic;
using System.Reflection;
using Feeder.Reflect.Model;
using Feeder.Logging;

namespace Feeder.Reflect.Converter
{
public interface IReflectionConverter
{
	bool AllowSetValue { get; }

	bool AllowCascadeSerialization { get; }

	bool AllowCascadeFieldsConversion { get; }

	bool AllowCascadePropertiesConversion { get; }

	bool AllowPointerFieldsAccess { get; }

	bool AllowPointerPropertiesAccess { get; }

	bool TreatJsonObjectAsAtomicValue(Type type);

	int SerializationPriority(Type type, ILogger? logger = null);

	object? Deserialize(Reflector reflector, SerializedMember data, Type? fallbackType = null, string? fallbackName = null, int depth = 0, Logs? logs = null, ILogger? logger = null, DeserializationContext? context = null);

	SerializedMember Serialize(Reflector reflector, object? obj, Type? fallbackType = null, string? name = null, bool recursive = true, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, int depth = 0, Logs? logs = null, ILogger? logger = null, SerializationContext? context = null);

	bool TryModify(Reflector reflector, ref object? obj, SerializedMember data, Type type, int depth = 0, Logs? logs = null, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null);

	bool SetField(Reflector reflector, ref object? obj, Type type, FieldInfo fieldInfo, SerializedMember? value, int depth = 0, Logs? logs = null, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null);

	bool SetProperty(Reflector reflector, ref object? obj, Type type, PropertyInfo propertyInfo, SerializedMember? value, int depth = 0, Logs? logs = null, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null);

	IEnumerable<FieldInfo>? GetSerializableFields(Reflector reflector, Type objType, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null);

	IEnumerable<PropertyInfo>? GetSerializableProperties(Reflector reflector, Type objType, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null);

	IEnumerable<string> GetAdditionalSerializableFields(Reflector reflector, Type objType, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null);

	IEnumerable<string> GetAdditionalSerializableProperties(Reflector reflector, Type objType, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null);

	object? CreateInstance(Reflector reflector, Type type);

	object? GetDefaultValue(Reflector reflector, Type type);
}
}
