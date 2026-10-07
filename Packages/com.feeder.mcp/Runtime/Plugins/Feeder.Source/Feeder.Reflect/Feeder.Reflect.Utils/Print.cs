using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Feeder.Reflect.Model;
using Feeder.Logging;

namespace Feeder.Reflect.Utils
{
public static class Print
{
	public static void FailedToSetNewValue(ref object? obj, Type type, int depth = 0, Logs? logs = null, ILogger? logger = null)
	{
		logs?.Error("Failed to set new value for '" + type.GetTypeId() + "'.", depth);
	}

	public static void SetNewValue<T>(ref object? obj, ref T? newValue, Type type, int depth = 0, Logs? logs = null, ILogger? logger = null)
	{
		Type type2 = obj?.GetType() ?? type;
		Type type3 = newValue?.GetType() ?? type;
		logs?.Success($"Set value\n  was: type='{type2.GetTypeId().ValueOrNull()}', value='{obj}'\n  new: type='{type3.GetTypeId().ValueOrNull()}', value='{newValue}'.", depth);
	}

	public static void SetNewValueEnumerable(ref object? obj, ref IEnumerable? newValue, Type type, int depth = 0, Logs? logs = null, ILogger? logger = null)
	{
		Type type2 = obj?.GetType() ?? type;
		Type type3 = newValue?.GetType() ?? type;
		logs?.Success($"Set array value\n  was: type='{type2.GetTypeId().ValueOrNull()}', value='{obj}'\n  new: type='{type3.GetTypeId().ValueOrNull()}', value='{newValue}'.", depth);
	}

	public static void SetNewValueEnumerable<T>(ref object? obj, ref IEnumerable<T>? newValue, Type type, int depth = 0, Logs? logs = null, ILogger? logger = null)
	{
		Type type2 = obj?.GetType() ?? type;
		Type type3 = newValue?.GetType() ?? type;
		logs?.Success($"Set array value\n  was: type='{type2.GetTypeId().ValueOrNull()}', value='{obj}'\n  new: type='{type3.GetTypeId().ValueOrNull()}', value='{newValue}'.", depth);
	}

	public static void FailedToSetField(ref object? obj, Type type, FieldInfo fieldInfo, int depth = 0, Logs? logs = null, ILogger? logger = null)
	{
		logs?.Error("Failed to set field '" + fieldInfo.Name + "'", depth);
		logs?.Error("Failed to set new value for '" + type.GetTypeId() + "'.", depth);
	}

	public static void FailedToSetProperty(ref object? obj, Type type, PropertyInfo propertyInfo, int depth = 0, Logs? logs = null, ILogger? logger = null)
	{
		logs?.Error("Failed to set property '" + propertyInfo.Name + "'", depth);
		logs?.Error("Failed to set new value for '" + type.GetTypeId() + "'.", depth);
	}
}
}
