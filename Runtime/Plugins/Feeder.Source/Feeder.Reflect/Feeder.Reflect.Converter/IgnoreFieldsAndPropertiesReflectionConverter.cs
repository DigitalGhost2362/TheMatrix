using System;
using System.Collections.Generic;
using System.Reflection;
using Feeder.Logging;

namespace Feeder.Reflect.Converter
{
public class IgnoreFieldsAndPropertiesReflectionConverter<T> : GenericReflectionConverter<T>
{
	private readonly bool _ignoreFields;

	private readonly bool _ignoreProperties;

	public override bool AllowCascadeSerialization => false;

	public IgnoreFieldsAndPropertiesReflectionConverter(bool ignoreFields, bool ignoreProperties)
	{
		_ignoreFields = ignoreFields;
		_ignoreProperties = ignoreProperties;
	}

	protected override IEnumerable<FieldInfo>? GetSerializableFieldsInternal(Reflector reflector, Type objType, BindingFlags flags, ILogger? logger = null)
	{
		if (!_ignoreFields)
		{
			return base.GetSerializableFieldsInternal(reflector, objType, flags, logger);
		}
		return null;
	}

	protected override IEnumerable<PropertyInfo>? GetSerializablePropertiesInternal(Reflector reflector, Type objType, BindingFlags flags, ILogger? logger = null)
	{
		if (!_ignoreProperties)
		{
			return base.GetSerializablePropertiesInternal(reflector, objType, flags, logger);
		}
		return null;
	}
}
}
