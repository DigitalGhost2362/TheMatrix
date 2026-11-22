using System;
using System.Collections.Generic;
using System.Reflection;
using Feeder.Reflect.Utils;
using Feeder.Logging;

namespace Feeder.Reflect.Converter
{
public class LazyGenericReflectionConverter : LazyGenericReflectionConverter<object>
{
	public LazyGenericReflectionConverter(string targetTypeName, IEnumerable<string>? ignoredProperties = null, IEnumerable<string>? ignoredFields = null, IReflectionConverter? backingConverter = null)
		: base(targetTypeName, ignoredProperties, ignoredFields, backingConverter)
	{
	}
}
public class LazyGenericReflectionConverter<T> : GenericReflectionConverter<T>
{
	private readonly string _targetTypeName;

	private readonly HashSet<string> _ignoredProperties;

	private readonly HashSet<string> _ignoredFields;

	private readonly IReflectionConverter? _backingConverter;

	private readonly Lazy<Type?> _targetType;

	public override bool AllowSetValue => _backingConverter?.AllowSetValue ?? base.AllowSetValue;

	public override bool AllowCascadeSerialization => _backingConverter?.AllowCascadeSerialization ?? base.AllowCascadeSerialization;

	public override bool AllowCascadeFieldsConversion => _backingConverter?.AllowCascadeFieldsConversion ?? base.AllowCascadeFieldsConversion;

	public override bool AllowCascadePropertiesConversion => _backingConverter?.AllowCascadePropertiesConversion ?? base.AllowCascadePropertiesConversion;

	public LazyGenericReflectionConverter(string targetTypeName, IEnumerable<string>? ignoredProperties = null, IEnumerable<string>? ignoredFields = null, IReflectionConverter? backingConverter = null)
	{
		if (string.IsNullOrWhiteSpace(targetTypeName))
		{
			throw new ArgumentException("Target type name cannot be null or empty.", "targetTypeName");
		}
		_targetTypeName = targetTypeName;
		_ignoredProperties = ((ignoredProperties != null) ? new HashSet<string>(ignoredProperties) : new HashSet<string>());
		_ignoredFields = ((ignoredFields != null) ? new HashSet<string>(ignoredFields) : new HashSet<string>());
		_backingConverter = backingConverter;
		_targetType = new Lazy<Type>(() => TypeUtils.GetType(_targetTypeName));
	}

	public override int SerializationPriority(Type type, ILogger? logger = null)
	{
		Type value = _targetType.Value;
		if (value == null)
		{
			return 0;
		}
		if (type == value)
		{
			return 10001;
		}
		int inheritanceDistance = TypeUtils.GetInheritanceDistance(value, type);
		if (inheritanceDistance < 0)
		{
			return 0;
		}
		return 10000 - inheritanceDistance;
	}

	protected override IEnumerable<PropertyInfo>? GetSerializablePropertiesInternal(Reflector reflector, Type objType, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		if (_backingConverter != null)
		{
			return _backingConverter.GetSerializableProperties(reflector, objType, flags, logger);
		}
		return base.GetSerializablePropertiesInternal(reflector, objType, flags, logger);
	}

	protected override IEnumerable<FieldInfo>? GetSerializableFieldsInternal(Reflector reflector, Type objType, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		if (_backingConverter != null)
		{
			return _backingConverter.GetSerializableFields(reflector, objType, flags, logger);
		}
		return base.GetSerializableFieldsInternal(reflector, objType, flags, logger);
	}

	protected override IEnumerable<string> GetIgnoredProperties()
	{
		foreach (string ignoredProperty in base.GetIgnoredProperties())
		{
			yield return ignoredProperty;
		}
		foreach (string ignoredProperty2 in _ignoredProperties)
		{
			yield return ignoredProperty2;
		}
	}

	protected override IEnumerable<string> GetIgnoredFields()
	{
		foreach (string ignoredField in base.GetIgnoredFields())
		{
			yield return ignoredField;
		}
		foreach (string ignoredField2 in _ignoredFields)
		{
			yield return ignoredField2;
		}
	}
}
}
