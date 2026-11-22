using System;
using System.Reflection;
using Feeder.Logging;

namespace Feeder.Reflect.Utils
{
public static class TypeMemberUtils
{
	public const int FieldCacheCapacity = 10000;

	public const int PropertyCacheCapacity = 10000;

	private static readonly LruCache<(Type, BindingFlags, string), FieldInfo?> _fieldCache = new LruCache<(Type, BindingFlags, string), FieldInfo>(10000);

	private static readonly LruCache<(Type, BindingFlags, string), PropertyInfo?> _propertyCache = new LruCache<(Type, BindingFlags, string), PropertyInfo>(10000);

	public static FieldInfo? GetField(Type type, BindingFlags flags, string fieldName)
	{
		(Type, BindingFlags, string) key = (type, flags, fieldName);
		return _fieldCache.GetOrAdd(key, ((Type, BindingFlags, string) tuple) => tuple.Item1.GetField(tuple.Item3, tuple.Item2));
	}

	public static PropertyInfo? GetProperty(Type type, BindingFlags flags, string propertyName)
	{
		(Type, BindingFlags, string) key = (type, flags, propertyName);
		return _propertyCache.GetOrAdd(key, ((Type, BindingFlags, string) tuple) => tuple.Item1.GetProperty(tuple.Item3, tuple.Item2));
	}

	public static void ClearFieldCache(ILogger? logger = null)
	{
		logger?.LogDebug("Clearing field lookup cache with {_fieldCacheCount} entries (capacity: {_fieldCacheCapacity}).", _fieldCache.Count, _fieldCache.Capacity);
		_fieldCache.Clear();
	}

	public static void ClearPropertyCache(ILogger? logger = null)
	{
		logger?.LogDebug("Clearing property lookup cache with {_propertyCacheCount} entries (capacity: {_propertyCacheCapacity}).", _propertyCache.Count, _propertyCache.Capacity);
		_propertyCache.Clear();
	}

	public static void ClearAllCaches(ILogger? logger = null)
	{
		ClearFieldCache(logger);
		ClearPropertyCache(logger);
	}
}
}
