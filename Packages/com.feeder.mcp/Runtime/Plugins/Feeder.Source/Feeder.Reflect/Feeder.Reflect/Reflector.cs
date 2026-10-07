using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Feeder.Json;
using Feeder.Json.Nodes;
using Feeder.Json.Serialization;
using System.Text.RegularExpressions;
using Feeder.Reflect.Converter;
using Feeder.Reflect.Model;
using Feeder.Reflect.Utils;
using Feeder.Logging;

namespace Feeder.Reflect
{
public class Reflector
{
	public static class Error
	{
		public static string DataTypeIsEmpty()
		{
			return "[Error] Data type is empty.";
		}

		public static string NotFoundType(string? typeFullName)
		{
			return "[Error] Type '" + typeFullName.ValueOrNull() + "' not found.";
		}

		public static string TargetObjectIsNull()
		{
			return "Target object is null.";
		}

		public static string TypeMismatch(string? expectedType, string? objType)
		{
			return "Type mismatch between '" + expectedType.ValueOrNull() + "' (expected) and '" + objType.ValueOrNull() + "'.";
		}

		public static string FieldNameIsEmpty()
		{
			return "[Error] Field name is empty.";
		}

		public static string FieldTypeIsEmpty()
		{
			return "[Error] Field type is empty.";
		}

		public static string PropertyNameIsEmpty()
		{
			return "[Error] Property name is empty. It should be a valid property name.";
		}

		public static string PropertyTypeIsEmpty()
		{
			return "[Error] Property type is empty. It should be a valid property type.";
		}

		public static string InvalidInstanceID(Type holderType, string? fieldName)
		{
			return "[Error] Invalid instanceID '" + fieldName.ValueOrNull() + "' for '" + holderType.GetTypeId() + "'. It should be a valid field name.";
		}

		public static string InvalidPropertyType(SerializedMember serializedProperty, PropertyInfo propertyInfo)
		{
			return "[Error] Invalid property type '" + serializedProperty.typeName.ValueOrNull() + "' for '" + propertyInfo.Name + "'. Expected '" + propertyInfo.PropertyType.GetTypeId() + "' or extended from it.";
		}

		public static string InvalidFieldType(SerializedMember serializedProperty, FieldInfo propertyInfo)
		{
			return "[Error] Invalid field type '" + serializedProperty.typeName.ValueOrNull() + "' for '" + propertyInfo.Name + "'. Expected '" + propertyInfo.FieldType.GetTypeId() + "' or extended from it.";
		}

		public static string NotSupportedInRuntime(Type type)
		{
			return "[Error] Type '" + type.GetTypeId().ValueOrNull() + "' is not supported in runtime for now.";
		}

		public static string MoreThanOneMethodFound(Reflector reflector, List<MethodInfo> methods, ILogger? logger = null)
		{
			string arg = methods.Select((MethodInfo method) => new MethodData(reflector, method)).ToJson(reflector, null, 0, logger);
			return $"[Error] Found more than one method. Only single method should be targeted. Please specify the method name more precisely.\nFound {methods.Count} method(s):\n```json\n{arg}\n```";
		}
	}

	public class Registry
	{
		private const int MaxBlacklistCacheSize = 1000;

		private ConcurrentBag<IReflectionConverter> _serializers = new ConcurrentBag<IReflectionConverter>();

		private readonly ConcurrentDictionary<Type, byte> _blacklistedTypes = new ConcurrentDictionary<Type, byte>();

		private ConcurrentDictionary<Type, bool> _blacklistCache = new ConcurrentDictionary<Type, bool>();

		private ConcurrentDictionary<Type, IReflectionConverter?> _converterCache = new ConcurrentDictionary<Type, IReflectionConverter>();

		public Registry()
		{
			Add(new PrimitiveReflectionConverter());
			Add(new ArrayReflectionConverter());
			Add(new GenericReflectionConverter<object>());
			Add(new TypeReflectionConverter());
			Add(new AssemblyReflectionConverter());
		}

		public void Add(IReflectionConverter serializer)
		{
			if (serializer != null)
			{
				_serializers.Add(serializer);
				_converterCache = new ConcurrentDictionary<Type, IReflectionConverter>();
			}
		}

		public void Remove<T>() where T : IReflectionConverter
		{
			IReflectionConverter serializer = _serializers.FirstOrDefault((IReflectionConverter s) => s is T);
			if (serializer != null)
			{
				_serializers = new ConcurrentBag<IReflectionConverter>(_serializers.Where((IReflectionConverter s) => s != serializer));
				_converterCache = new ConcurrentDictionary<Type, IReflectionConverter>();
			}
		}

		public IReadOnlyList<IReflectionConverter> GetAllSerializers()
		{
			return _serializers.ToList();
		}

		public bool BlacklistType(Type type)
		{
			if (type == null)
			{
				return false;
			}
			if (_blacklistedTypes.TryAdd(type, 0))
			{
				_blacklistCache = new ConcurrentDictionary<Type, bool>();
				return true;
			}
			return false;
		}

		public bool BlacklistTypes(params Type[] types)
		{
			bool flag = false;
			foreach (Type type in types)
			{
				if (type != null && _blacklistedTypes.TryAdd(type, 0))
				{
					flag = true;
				}
			}
			if (flag)
			{
				_blacklistCache = new ConcurrentDictionary<Type, bool>();
			}
			return flag;
		}

		public bool BlacklistType(string typeFullName)
		{
			Type type = TypeUtils.GetType(typeFullName);
			if (type != null)
			{
				return BlacklistType(type);
			}
			return false;
		}

		public bool BlacklistTypeInAssembly(string assemblyNamePrefix, string typeFullName)
		{
			if (string.IsNullOrEmpty(assemblyNamePrefix) || string.IsNullOrEmpty(typeFullName))
			{
				return false;
			}
			Type type = TypeUtils.GetType(assemblyNamePrefix, typeFullName);
			if (type != null)
			{
				return BlacklistType(type);
			}
			return false;
		}

		public bool BlacklistTypes(params string[] typeFullNames)
		{
			bool flag = false;
			for (int i = 0; i < typeFullNames.Length; i++)
			{
				Type type = TypeUtils.GetType(typeFullNames[i]);
				if (type != null && _blacklistedTypes.TryAdd(type, 0))
				{
					flag = true;
				}
			}
			if (flag)
			{
				_blacklistCache = new ConcurrentDictionary<Type, bool>();
			}
			return flag;
		}

		public bool BlacklistTypesInAssembly(string assemblyNamePrefix, params string[] typeFullNames)
		{
			if (string.IsNullOrEmpty(assemblyNamePrefix))
			{
				return false;
			}
			bool flag = false;
			foreach (Assembly item in AssemblyUtils.GetAssembliesStartingWith(assemblyNamePrefix))
			{
				foreach (string typeName in typeFullNames)
				{
					Type type = TypeUtils.GetType(item, typeName);
					if (type != null && _blacklistedTypes.TryAdd(type, 0))
					{
						flag = true;
					}
				}
			}
			if (flag)
			{
				_blacklistCache = new ConcurrentDictionary<Type, bool>();
			}
			return flag;
		}

		public bool IsTypeBlacklisted(Type type)
		{
			if (type == null)
			{
				return false;
			}
			if (_blacklistedTypes.IsEmpty)
			{
				return false;
			}
			ConcurrentDictionary<Type, bool> blacklistCache;
			bool flag;
			do
			{
				blacklistCache = _blacklistCache;
				if (blacklistCache.TryGetValue(type, out var value))
				{
					return value;
				}
				flag = IsTypeBlacklistedInternal(type, new HashSet<Type>(), blacklistCache);
			}
			while (_blacklistCache != blacklistCache);
			if (blacklistCache.Count >= 1000)
			{
				_blacklistCache = new ConcurrentDictionary<Type, bool>();
			}
			_blacklistCache.TryAdd(type, flag);
			return flag;
		}

		private bool IsTypeBlacklistedInternal(Type? type, HashSet<Type> visited, ConcurrentDictionary<Type, bool> cache)
		{
			if (type == null)
			{
				return false;
			}
			if (cache.TryGetValue(type, out var value))
			{
				return value;
			}
			if (!visited.Add(type))
			{
				return false;
			}
			if (_blacklistedTypes.ContainsKey(type))
			{
				return true;
			}
			if (type.BaseType != null && IsTypeBlacklistedInternal(type.BaseType, visited, cache))
			{
				return true;
			}
			Type[] interfaces = type.GetInterfaces();
			for (int i = 0; i < interfaces.Length; i++)
			{
				if (IsTypeBlacklistedInternal(interfaces[i], visited, cache))
				{
					return true;
				}
			}
			if (type.IsArray)
			{
				Type elementType = type.GetElementType();
				if (elementType != null && IsTypeBlacklistedInternal(elementType, visited, cache))
				{
					return true;
				}
			}
			if (type.IsGenericType)
			{
				if (!type.IsGenericTypeDefinition)
				{
					Type genericTypeDefinition = type.GetGenericTypeDefinition();
					if (_blacklistedTypes.ContainsKey(genericTypeDefinition))
					{
						return true;
					}
				}
				Type[] genericArguments = type.GetGenericArguments();
				for (int j = 0; j < genericArguments.Length; j++)
				{
					if (IsTypeBlacklistedInternal(genericArguments[j], visited, cache))
					{
						return true;
					}
				}
			}
			return false;
		}

		public bool RemoveBlacklistedType(Type type)
		{
			if (_blacklistedTypes.TryRemove(type, out var _))
			{
				_blacklistCache = new ConcurrentDictionary<Type, bool>();
				return true;
			}
			return false;
		}

		public IReadOnlyList<Type> GetAllBlacklistedTypes()
		{
			return _blacklistedTypes.Keys.ToList();
		}

		private IEnumerable<IReflectionConverter> FindRelevantConverters(Type type)
		{
			return from s in _serializers
				select (s: s, s.SerializationPriority(type)) into s
				where s.Item2 > 0
				orderby s.Item2 descending
				select s.s;
		}

		public IReflectionConverter? GetConverter(Type type)
		{
			return _converterCache.GetOrAdd(type, (Type t) => FindRelevantConverters(t).FirstOrDefault());
		}
	}

	private sealed class ObjectReferenceEqualityComparer : IEqualityComparer<object>
	{
		public static readonly ObjectReferenceEqualityComparer Instance = new ObjectReferenceEqualityComparer();

		private ObjectReferenceEqualityComparer()
		{
		}

		public new bool Equals(object? x, object? y)
		{
			return x == y;
		}

		public int GetHashCode(object obj)
		{
			return RuntimeHelpers.GetHashCode(obj);
		}
	}

	private readonly Feeder.Reflect.Utils.JsonSerializer jsonSerializer;

	private readonly JsonSchema jsonSchema = new JsonSchema();

	public Registry Converters { get; }

	public static IEnumerable<MethodInfo> AllMethods => from method in AssemblyUtils.AllTypes.SelectMany((Type type) => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
		where method.DeclaringType != null && !method.DeclaringType.IsAbstract
		select method;

	public JsonSerializerOptions JsonSerializerOptions => jsonSerializer.JsonSerializerOptions;

	public Feeder.Reflect.Utils.JsonSerializer JsonSerializer => jsonSerializer;

	public JsonSchema JsonSchema => jsonSchema;

	public string MethodCall(Reflector reflector, MethodRef filter, bool knownNamespace = false, int typeNameMatchLevel = 1, int methodNameMatchLevel = 1, int parametersMatchLevel = 2, SerializedMember? targetObject = null, SerializedMemberList? inputParameters = null, bool executeInMainThread = true, ILogger? logger = null)
	{
		List<MethodRef.Parameter>? inputParameters2 = filter.InputParameters;
		if ((inputParameters2 == null || inputParameters2.Count == 0) && (inputParameters?.Count ?? 0) > 0)
		{
			filter.EnhanceInputParameters(inputParameters);
		}
		List<MethodInfo> list = FindMethod(filter, knownNamespace, typeNameMatchLevel, methodNameMatchLevel, parametersMatchLevel).ToList();
		if (list.Count == 0)
		{
			return $"[Error] Method not found.\n{filter}";
		}
		MethodInfo method = null;
		if (list.Count > 1)
		{
			bool flag = inputParameters.IsValidTypeNames("inputParameters", out string _);
			method = (flag ? list.FilterByParameters(inputParameters) : null);
			if (method == null)
			{
				return Error.MoreThanOneMethodFound(reflector, list);
			}
		}
		else
		{
			method = list.First();
		}
		inputParameters?.EnhanceNames(method);
		inputParameters?.EnhanceTypes(method);
		Func<string> func = delegate
		{
			Dictionary<string, object> dictionary = inputParameters?.ToDictionary((SerializedMember p) => p.name, (SerializedMember p) => reflector.Deserialize(p, null, null, 0, null, logger));
			MethodWrapper methodWrapper = null;
			if (method.IsStatic)
			{
				methodWrapper = new MethodWrapper(reflector, logger, method);
			}
			else if (targetObject != null && !string.IsNullOrEmpty(targetObject.typeName))
			{
				object obj = reflector.Deserialize(targetObject, method.DeclaringType, null, 0, null, logger);
				if (obj == null)
				{
					return "[Error] 'targetObject' deserialized instance is null. Please specify the 'targetObject' properly.";
				}
				methodWrapper = new MethodWrapper(reflector, logger, obj, method);
			}
			else
			{
				methodWrapper = new MethodWrapper(reflector, logger, method.DeclaringType, method);
			}
			if (!methodWrapper.VerifyParameters(dictionary, out string error2))
			{
				return "[Error] " + error2;
			}
			object result = ((dictionary != null) ? methodWrapper.InvokeDict(dictionary) : methodWrapper.Invoke()).Result;
			return "[Success] Execution result:\n```json\n" + result.ToJson(reflector, null, 0, logger) + "\n```";
		};
		if (executeInMainThread)
		{
			return MainThread.Instance.Run(func);
		}
		return func();
	}

	public Reflector()
	{
		Converters = new Registry();
		jsonSerializer = new Feeder.Reflect.Utils.JsonSerializer(this);
	}

	private object? ResolveReference(SerializedMember data, DeserializationContext context, int depth, Logs? logs, ILogger? logger)
	{
		string padding = StringUtils.GetPadding(depth);
		if (!data.valueJsonElement.HasValue)
		{
			if (logger != null && logger.IsEnabled(LogLevel.Warning))
			{
				logger.LogWarning(padding + "⚠\ufe0f Reference has no value");
			}
			logs?.Warning("Reference has no value", depth);
			return null;
		}
		if (!data.valueJsonElement.Value.TryGetProperty("$ref", out var value))
		{
			if (logger != null && logger.IsEnabled(LogLevel.Warning))
			{
				logger.LogWarning(padding + "⚠\ufe0f Reference value missing $ref property");
			}
			logs?.Warning("Reference value missing $ref property", depth);
			return null;
		}
		string text = value.GetString();
		if (string.IsNullOrEmpty(text))
		{
			if (logger != null && logger.IsEnabled(LogLevel.Warning))
			{
				logger.LogWarning(padding + "⚠\ufe0f Reference path is null or empty");
			}
			logs?.Warning("Reference path is null or empty", depth);
			return null;
		}
		if (logger != null && logger.IsEnabled(LogLevel.Trace))
		{
			logger.LogTrace(padding + "⚪ Resolving reference: " + text);
		}
		if (context.TryResolve(text, out object result))
		{
			if (logger != null && logger.IsEnabled(LogLevel.Trace))
			{
				logger.LogTrace(padding + "\ud83d\udfe2 Resolved reference to object of type: " + result?.GetType().GetTypeShortName());
			}
			return result;
		}
		if (logger != null && logger.IsEnabled(LogLevel.Warning))
		{
			logger.LogWarning(padding + "⚠\ufe0f Unable to resolve reference: " + text);
		}
		logs?.Warning("Unable to resolve reference: " + text, depth);
		return null;
	}

	public T? CreateInstance<T>()
	{
		return (T)CreateInstance(typeof(T));
	}

	public object? CreateInstance(Type type)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		return (Converters.GetConverter(type) ?? throw new ArgumentException("[Error] Type '" + type?.GetTypeId().ValueOrNull() + "' not supported for creating instance.")).CreateInstance(this, type);
	}

	public T? GetDefaultValue<T>()
	{
		return (T)GetDefaultValue(typeof(T));
	}

	public object? GetDefaultValue(Type type)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		return (Converters.GetConverter(type) ?? throw new ArgumentException("[Error] Type '" + type?.GetTypeId().ValueOrNull() + "' not supported for default value.")).GetDefaultValue(this, type);
	}

	public T? Deserialize<T>(SerializedMember data, string? fallbackName = null, int depth = 0, Logs? logs = null, ILogger? logger = null, DeserializationContext? context = null) where T : class
	{
		return Deserialize(data, typeof(T), fallbackName, depth, logs, logger, context) as T;
	}

	public object? Deserialize(SerializedMember data, Type? fallbackType = null, string? fallbackName = null, int depth = 0, Logs? logs = null, ILogger? logger = null, DeserializationContext? context = null)
	{
		if (data == null)
		{
			if (fallbackType != null)
			{
				return GetDefaultValue(fallbackType);
			}
			throw new ArgumentException(Error.DataTypeIsEmpty());
		}
		string padding = StringUtils.GetPadding(depth);
		string text = (StringUtils.IsNullOrEmpty(data.name) ? fallbackName : data.name);
		if (context == null)
		{
			context = new DeserializationContext();
		}
		if (data.typeName == "Reference")
		{
			return ResolveReference(data, context, depth, logs, logger);
		}
		context.Enter(text);
		try
		{
			Type typeWithNamePriority = TypeUtils.GetTypeWithNamePriority(data, fallbackType, out string error);
			if (typeWithNamePriority == null)
			{
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError("{padding}{error}", padding, error ?? "Unknown error");
				}
				logs?.Error(error ?? "Unknown error", depth);
				throw new ArgumentException(error);
			}
			if (Converters.IsTypeBlacklisted(typeWithNamePriority))
			{
				if (logger != null && logger.IsEnabled(LogLevel.Trace))
				{
					logger.LogTrace(padding + "Deserialize. Type '" + typeWithNamePriority.GetTypeId() + "' is blacklisted, skipping.");
				}
				return GetDefaultValue(typeWithNamePriority);
			}
			JsonConverter jsonConverter = JsonSerializer.GetJsonConverter(typeWithNamePriority);
			if (jsonConverter != null)
			{
				if (logger != null && logger.IsEnabled(LogLevel.Trace))
				{
					logger.LogTrace(padding + "\ud83d\ude80 Deserialize type='" + typeWithNamePriority.GetTypeId() + "' name='" + text.ValueOrNull() + "' JsonConverter: " + jsonConverter.GetType().GetTypeShortName());
				}
				return data.valueJsonElement.Deserialize(typeWithNamePriority, this);
			}
			IReflectionConverter converter = Converters.GetConverter(typeWithNamePriority);
			if (converter == null)
			{
				if (typeWithNamePriority.IsInterface)
				{
					if (data.IsNull())
					{
						return null;
					}
					if (logger != null && logger.IsEnabled(LogLevel.Error))
					{
						logger.LogError(padding + "\ud83d\ude80 Deserialize type='" + typeWithNamePriority.GetTypeId() + "' name='" + text.ValueOrNull() + "'. No converter can handle interface types with not null values.");
					}
					throw new TypeInstantiationException("Cannot deserialize interface type '" + typeWithNamePriority.GetTypeId() + "'", typeWithNamePriority);
				}
				if (typeWithNamePriority.IsAbstract)
				{
					if (data.IsNull())
					{
						return null;
					}
					if (logger != null && logger.IsEnabled(LogLevel.Error))
					{
						logger.LogError(padding + "\ud83d\ude80 Deserialize type='" + typeWithNamePriority.GetTypeId() + "' name='" + text.ValueOrNull() + "'. No converter can handle abstract types with not null values.");
					}
					throw new TypeInstantiationException("Cannot deserialize abstract type '" + typeWithNamePriority.GetTypeId() + "'", typeWithNamePriority);
				}
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + "\ud83d\ude80 Deserialize type='" + typeWithNamePriority.GetTypeId() + "' name='" + text.ValueOrNull() + "'. No converter found for type.");
				}
				throw new ArgumentException("Type '" + typeWithNamePriority.GetTypeId().ValueOrNull() + "' not supported for deserialization.");
			}
			if (logger != null && logger.IsEnabled(LogLevel.Trace))
			{
				logger.LogTrace(padding + "\ud83d\ude80 Deserialize type='" + typeWithNamePriority.GetTypeId() + "' name='" + text.ValueOrNull() + "' converter='" + converter.GetType().GetTypeShortName() + "'");
			}
			return converter.Deserialize(this, data, typeWithNamePriority, fallbackName, depth + 1, logs, logger, context);
		}
		finally
		{
			context.Exit(text);
		}
	}

	public bool AreEqual(object? a, object? b)
	{
		if (a == null && b == null)
		{
			return true;
		}
		if (a == b)
		{
			return true;
		}
		if (a == null || b == null)
		{
			return false;
		}
		if (a.GetType() != b.GetType())
		{
			return false;
		}
		Type type = a.GetType();
		IReflectionConverter converter = Converters.GetConverter(a.GetType());
		if (converter == null)
		{
			return a.Equals(b);
		}
		IEnumerable<FieldInfo> serializableFields = converter.GetSerializableFields(this, type);
		if (serializableFields != null)
		{
			foreach (FieldInfo item in serializableFields)
			{
				object value = item.GetValue(a);
				object value2 = item.GetValue(b);
				if (!AreEqual(value, value2))
				{
					return false;
				}
			}
		}
		IEnumerable<PropertyInfo> serializableProperties = converter.GetSerializableProperties(this, type);
		if (serializableProperties != null)
		{
			foreach (PropertyInfo item2 in serializableProperties)
			{
				object value3 = item2.GetValue(a);
				object value4 = item2.GetValue(b);
				if (!AreEqual(value3, value4))
				{
					return false;
				}
			}
		}
		return true;
	}

	private static int Compare(string original, string value)
	{
		if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(value))
		{
			return 0;
		}
		if (original.Equals(value, StringComparison.OrdinalIgnoreCase))
		{
			if (!original.Equals(value, StringComparison.Ordinal))
			{
				return 5;
			}
			return 6;
		}
		if (original.StartsWith(value, StringComparison.OrdinalIgnoreCase))
		{
			if (!original.StartsWith(value))
			{
				return 3;
			}
			return 4;
		}
		if (original.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
		{
			if (!original.Contains(value))
			{
				return 1;
			}
			return 2;
		}
		return 0;
	}

	private static int Compare(ParameterInfo[] original, List<MethodRef.Parameter>? value)
	{
		if (original == null && value == null)
		{
			return 2;
		}
		if (original == null)
		{
			return 0;
		}
		if (value == null)
		{
			if (original.Length != 0)
			{
				return 0;
			}
			return 2;
		}
		if (value.Count < original.Length)
		{
			for (int i = value.Count; i < original.Length; i++)
			{
				if (!original[i].IsOptional)
				{
					return 0;
				}
			}
		}
		else if (original.Length != value.Count)
		{
			return 0;
		}
		for (int j = 0; j < value.Count && j < original.Length; j++)
		{
			ParameterInfo parameterInfo = original[j];
			MethodRef.Parameter parameter = value[j];
			if (parameterInfo.Name != parameter.Name)
			{
				return 1;
			}
			if (parameterInfo.ParameterType.GetTypeId() != parameter.TypeName)
			{
				return 1;
			}
		}
		return 2;
	}

	public IEnumerable<MethodInfo> FindMethod(MethodRef filter, bool knownNamespace = false, int typeNameMatchLevel = 1, int methodNameMatchLevel = 1, int parametersMatchLevel = 0, BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
	{
		filter.Namespace = filter.Namespace?.Trim()?.Replace("null", string.Empty);
		IEnumerable<Type> source = from type in AssemblyUtils.AllTypes
			where type.IsVisible
			where !type.IsInterface
			where !type.IsAbstract || type.IsSealed
			where !type.IsGenericTypeDefinition
			select type;
		if (knownNamespace)
		{
			source = source.Where((Type type) => type.Namespace == filter.Namespace);
		}
		if (typeNameMatchLevel > 0 && !string.IsNullOrEmpty(filter.TypeName))
		{
			source = from type in source
				select new
				{
					Type = type,
					MatchLevel = Compare(type.Name, filter.TypeName)
				} into entry
				where entry.MatchLevel >= typeNameMatchLevel
				orderby entry.MatchLevel descending
				select entry.Type;
		}
		IEnumerable<MethodInfo> enumerable = from method in source.ToList().SelectMany((Type type) => from method in type.GetMethods(bindingFlags)
				where method.DeclaringType == type
				select method)
			where method.DeclaringType != null
			where !method.DeclaringType.IsAbstract || method.DeclaringType.IsSealed
			where !method.IsGenericMethodDefinition
			select method;
		if (methodNameMatchLevel > 0 && !string.IsNullOrEmpty(filter.MethodName))
		{
			enumerable = from method in enumerable
				select new
				{
					Method = method,
					MatchLevel = Compare(method.Name, filter.MethodName)
				} into entry
				where entry.MatchLevel >= methodNameMatchLevel
				orderby entry.MatchLevel descending
				select entry.Method;
		}
		if (parametersMatchLevel > 0)
		{
			enumerable = from method in enumerable
				select new
				{
					Method = method,
					MatchLevel = Compare(method.GetParameters(), filter.InputParameters)
				} into entry
				where entry.MatchLevel >= parametersMatchLevel
				orderby entry.MatchLevel descending
				select entry.Method;
		}
		return enumerable;
	}

	public JsonNode GetSchema<T>()
	{
		return GetSchema(typeof(T));
	}

	public JsonNode GetSchemaRef<T>()
	{
		return jsonSchema.GetSchemaRef<T>(this);
	}

	public JsonNode GetSchema(Type type)
	{
		return jsonSchema.GetSchema(this, type);
	}

	public JsonNode GetSchemaRef(Type type)
	{
		return jsonSchema.GetSchemaRef(this, type);
	}

	public JsonNode GetArgumentsSchema(MethodInfo methodInfo, bool justRef = false, JsonObject? defines = null)
	{
		return jsonSchema.GetArgumentsSchema(this, methodInfo, justRef, defines);
	}

	public JsonNode? GetReturnSchema(MethodInfo methodInfo, bool justRef = false, JsonObject? defines = null)
	{
		return jsonSchema.GetReturnSchema(this, methodInfo, justRef, defines);
	}

	public bool TryModify(ref object? obj, SerializedMember data, Type? fallbackObjType = null, int depth = 0, Logs? logs = null, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		if (data == null)
		{
			throw new ArgumentNullException("data");
		}
		string padding = StringUtils.GetPadding(depth);
		if (obj == null && data.IsNull())
		{
			if (logger != null && logger.IsEnabled(LogLevel.Trace))
			{
				logger.LogTrace(padding + "Object modification skipped: both target object and data are null.");
			}
			return true;
		}
		Type type = TypeUtils.GetTypeWithNamePriority(data, fallbackObjType, out string error) ?? obj?.GetType();
		if (type == null)
		{
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(padding + "Object modification failed: " + error);
			}
			logs?.Error("Object modification failed: " + error, depth);
			return false;
		}
		IReflectionConverter converter = Converters.GetConverter(type);
		if (converter == null)
		{
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(padding + "No suitable converter found for type '" + type.GetTypeId().ValueOrNull() + "'");
			}
			logs?.Error("No suitable converter found for type '" + type.GetTypeId().ValueOrNull() + "'", depth);
			return false;
		}
		if (logger != null && logger.IsEnabled(LogLevel.Trace))
		{
			logger.LogTrace(padding + "Modify. " + converter.GetType().GetTypeShortName() + " used for type='" + type.GetTypeShortName().ValueOrNull() + "', name='" + data.name.ValueOrNull() + "'");
		}
		return converter.TryModify(this, ref obj, data, type, depth, logs, flags, logger);
	}

	public bool TryModifyAt(ref object? obj, string path, SerializedMember value, Type? fallbackObjType = null, int depth = 0, Logs? logs = null, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		string[] array = ParsePath(path);
		if (array.Length == 0)
		{
			return TryModify(ref obj, value, fallbackObjType, depth, logs, flags, logger);
		}
		if (obj == null)
		{
			string text = "Cannot navigate path '" + path + "': object is null.";
			logs?.Error(text, depth);
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(StringUtils.GetPadding(depth) + text);
			}
			return false;
		}
		string text2 = array[0];
		string remainingPath = ((array.Length > 1) ? string.Join("/", array, 1, array.Length - 1) : string.Empty);
		Type type = obj.GetType();
		if (TryParseBracketSegment(text2, out string innerKey))
		{
			return TryModifyAtBracketed(ref obj, text2, innerKey, remainingPath, value, type, depth, logs, flags, logger);
		}
		return TryModifyAtMember(ref obj, text2, remainingPath, value, type, depth, logs, flags, logger);
	}

	public bool TryModifyAt<T>(ref object? obj, string path, T value, Type? fallbackObjType = null, int depth = 0, Logs? logs = null, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		SerializedMember value2 = SerializedMember.FromValue(this, value);
		return TryModifyAt(ref obj, path, value2, fallbackObjType, depth, logs, flags, logger);
	}

	private static string[] ParsePath(string? path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return Array.Empty<string>();
		}
		if (path.StartsWith("#/"))
		{
			path = path.Substring(2);
		}
		else if (path.StartsWith("#"))
		{
			path = path.Substring(1);
		}
		return path.Split(new char[1] { '/' }, StringSplitOptions.RemoveEmptyEntries);
	}

	internal static bool TryParseBracketSegment(string segment, out string innerKey)
	{
		if (segment.Length > 2 && segment[0] == '[' && segment[segment.Length - 1] == ']')
		{
			innerKey = segment.Substring(1, segment.Length - 2);
			return true;
		}
		innerKey = string.Empty;
		return false;
	}

	private void ApplyTypeReplacementCheck(ref object? currentValue, SerializedMember value, Type declaredType, string remainingPath)
	{
		if (string.IsNullOrEmpty(remainingPath))
		{
			Type typeWithNamePriority = TypeUtils.GetTypeWithNamePriority(value, declaredType, out string _);
			if (typeWithNamePriority != null && currentValue != null && typeWithNamePriority != currentValue.GetType() && declaredType.IsAssignableFrom(typeWithNamePriority))
			{
				currentValue = null;
			}
		}
	}

	private bool TryModifyAtBracketed(ref object? obj, string segment, string innerKey, string remainingPath, SerializedMember value, Type objType, int depth, Logs? logs, BindingFlags flags, ILogger? logger)
	{
		if (!TryLookupBracketedElement(obj, segment, innerKey, objType, depth, logs, logger, out object currentElement, out Type elementType, out Action<object> writeBack))
		{
			return false;
		}
		ApplyTypeReplacementCheck(ref currentElement, value, elementType, remainingPath);
		bool num = TryModifyAt(ref currentElement, remainingPath, value, elementType, depth + 1, logs, flags, logger);
		if (num)
		{
			writeBack(currentElement);
		}
		return num;
	}

	private bool TryModifyAtMember(ref object? obj, string memberName, string remainingPath, SerializedMember value, Type objType, int depth, Logs? logs, BindingFlags flags, ILogger? logger)
	{
		if (!TryLookupMember(obj, memberName, objType, flags, depth, logs, logger, out object currentValue, out Type memberType, out Action<object> writeBack))
		{
			return false;
		}
		ApplyTypeReplacementCheck(ref currentValue, value, memberType, remainingPath);
		bool num = TryModifyAt(ref currentValue, remainingPath, value, memberType, depth + 1, logs, flags, logger);
		if (num)
		{
			writeBack(currentValue);
		}
		return num;
	}

	private bool TryLookupMember(object? obj, string memberName, Type objType, BindingFlags flags, int depth, Logs? logs, ILogger? logger, out object? currentValue, out Type memberType, out Action<object?>? writeBack)
	{
		string padding = StringUtils.GetPadding(depth);
		FieldInfo fieldInfo = TypeMemberUtils.GetField(objType, flags, memberName);
		if (fieldInfo != null)
		{
			try
			{
				currentValue = fieldInfo.GetValue(obj);
			}
			catch (Exception ex)
			{
				string text = "Field '" + memberName + "' on type '" + objType.GetTypeShortName() + "' getter threw: " + ex.Message;
				logs?.Error(text, depth);
				ILogger? logger2 = logger;
				if (logger2 != null && logger2.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text);
				}
				currentValue = null;
				memberType = fieldInfo.FieldType;
				writeBack = null;
				return false;
			}
			memberType = fieldInfo.FieldType;
			writeBack = delegate(object? v)
			{
				try
				{
					fieldInfo.SetValue(obj, v);
				}
				catch (Exception ex3)
				{
					logger?.LogError(padding + "Field '" + memberName + "' setter threw: " + ex3.Message);
				}
			};
			return true;
		}
		PropertyInfo propInfo = TypeMemberUtils.GetProperty(objType, flags, memberName);
		if (propInfo != null)
		{
			if (!propInfo.CanWrite)
			{
				string text2 = "Property '" + memberName + "' on type '" + objType.GetTypeShortName() + "' is read-only.";
				logs?.Error(text2, depth);
				ILogger? logger3 = logger;
				if (logger3 != null && logger3.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text2);
				}
				currentValue = null;
				memberType = propInfo.PropertyType;
				writeBack = null;
				return false;
			}
			if (!propInfo.CanRead)
			{
				string text3 = "Property '" + memberName + "' on type '" + objType.GetTypeShortName() + "' is write-only (no getter); cannot read current value.";
				logs?.Error(text3, depth);
				ILogger? logger4 = logger;
				if (logger4 != null && logger4.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text3);
				}
				currentValue = null;
				memberType = propInfo.PropertyType;
				writeBack = null;
				return false;
			}
			try
			{
				currentValue = propInfo.GetValue(obj);
			}
			catch (Exception ex2)
			{
				string text4 = "Property '" + memberName + "' on type '" + objType.GetTypeShortName() + "' getter threw: " + ex2.Message;
				logs?.Error(text4, depth);
				ILogger? logger5 = logger;
				if (logger5 != null && logger5.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text4);
				}
				currentValue = null;
				memberType = propInfo.PropertyType;
				writeBack = null;
				return false;
			}
			memberType = propInfo.PropertyType;
			writeBack = delegate(object? v)
			{
				try
				{
					propInfo.SetValue(obj, v);
				}
				catch (Exception ex3)
				{
					logger?.LogError(padding + "Property '" + memberName + "' setter threw: " + ex3.Message);
				}
			};
			return true;
		}
		IEnumerable<FieldInfo> serializableFields = GetSerializableFields(objType, flags, logger);
		IEnumerable<PropertyInfo> serializableProperties = GetSerializableProperties(objType, flags, logger);
		string text5 = ((serializableFields != null && serializableFields.Any()) ? string.Join(", ", serializableFields.Select((FieldInfo f) => f.Name)) : "none");
		string text6 = ((serializableProperties != null && serializableProperties.Any()) ? string.Join(", ", serializableProperties.Select((PropertyInfo p) => p.Name)) : "none");
		string text7 = "Segment '" + memberName + "' not found on type '" + objType.GetTypeShortName() + "'.\nAvailable fields: " + text5 + "\nAvailable properties: " + text6;
		logs?.Error(text7, depth);
		ILogger? logger6 = logger;
		if (logger6 != null && logger6.IsEnabled(LogLevel.Error))
		{
			logger.LogError(padding + text7);
		}
		currentValue = null;
		memberType = typeof(object);
		writeBack = null;
		return false;
	}

	private static bool TryLookupBracketedElement(object? obj, string segment, string innerKey, Type objType, int depth, Logs? logs, ILogger? logger, out object? currentElement, out Type elementType, out Action<object?>? writeBack)
	{
		string padding = StringUtils.GetPadding(depth);
		currentElement = null;
		elementType = typeof(object);
		writeBack = null;
		if (obj is IList)
		{
			if (!int.TryParse(innerKey, out var idx))
			{
				string text = "Bracket segment '" + segment + "' cannot be used as index on type '" + objType.GetTypeShortName() + "'. Expected integer index.";
				logs?.Error(text, depth);
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text);
				}
				return false;
			}
			elementType = TypeUtils.GetEnumerableItemType(objType) ?? typeof(object);
			Array array = obj as Array;
			if (array != null)
			{
				if (idx < 0 || idx >= array.Length)
				{
					string text2 = $"Bracket segment '{segment}' index out of range on type '{objType.GetTypeShortName()}'. Array length is {array.Length}.";
					logs?.Error(text2, depth);
					if (logger != null && logger.IsEnabled(LogLevel.Error))
					{
						logger.LogError(padding + text2);
					}
					return false;
				}
				currentElement = array.GetValue(idx);
				writeBack = delegate(object? v)
				{
					array.SetValue(v, idx);
				};
				return true;
			}
			IList list = (IList)obj;
			if (idx < 0 || idx >= list.Count)
			{
				string text3 = $"Bracket segment '{segment}' index out of range on type '{objType.GetTypeShortName()}'. List count is {list.Count}.";
				logs?.Error(text3, depth);
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text3);
				}
				return false;
			}
			currentElement = list[idx];
			writeBack = delegate(object? v)
			{
				list[idx] = v;
			};
			return true;
		}
		if (TypeUtils.IsDictionary(objType))
		{
			Type[] dictionaryGenericArguments = TypeUtils.GetDictionaryGenericArguments(objType);
			Type type = ((dictionaryGenericArguments != null) ? dictionaryGenericArguments[0] : null) ?? typeof(object);
			elementType = ((dictionaryGenericArguments != null) ? dictionaryGenericArguments[1] : null) ?? typeof(object);
			object dictKey;
			try
			{
				dictKey = Convert.ChangeType(innerKey, type);
			}
			catch (Exception ex)
			{
				string text4 = "Bracket segment '" + segment + "' cannot be converted to key type '" + type.GetTypeShortName() + "' on type '" + objType.GetTypeShortName() + "': " + ex.Message;
				logs?.Error(text4, depth);
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text4);
				}
				return false;
			}
			IDictionary dict = (IDictionary)obj;
			currentElement = (dict.Contains(dictKey) ? dict[dictKey] : null);
			writeBack = delegate(object? v)
			{
				dict[dictKey] = v;
			};
			return true;
		}
		string text5 = "Bracket segment '" + segment + "' cannot be used on type '" + objType.GetTypeShortName() + "'. Type is not an array, list, or dictionary.";
		logs?.Error(text5, depth);
		if (logger != null && logger.IsEnabled(LogLevel.Error))
		{
			logger.LogError(padding + text5);
		}
		return false;
	}

	public bool TryPatch(ref object? obj, string json, Type? fallbackObjType = null, int depth = 0, Logs? logs = null, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		JsonDocument jsonDocument;
		try
		{
			jsonDocument = JsonDocument.Parse(json);
		}
		catch (Exception ex)
		{
			string text = "Failed to parse JSON patch: " + ex.Message;
			logs?.Error(text, depth);
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(StringUtils.GetPadding(depth) + text);
			}
			return false;
		}
		using (jsonDocument)
		{
			return TryPatch(ref obj, jsonDocument.RootElement, fallbackObjType, depth, logs, flags, logger);
		}
	}

	public bool TryPatch(ref object? obj, JsonElement patch, Type? fallbackObjType = null, int depth = 0, Logs? logs = null, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		Type objType = obj?.GetType() ?? fallbackObjType;
		return TryPatchInternal(ref obj, patch, objType, depth, logs, flags, logger);
	}

	private bool TryPatchInternal(ref object? obj, JsonElement patch, Type? objType, int depth, Logs? logs, BindingFlags flags, ILogger? logger)
	{
		string padding = StringUtils.GetPadding(depth);
		if (patch.ValueKind == JsonValueKind.Null)
		{
			if (objType != null && objType.IsValueType && Nullable.GetUnderlyingType(objType) == null)
			{
				string text = "Cannot set null on non-nullable value type '" + objType.GetTypeShortName() + "'.";
				logs?.Error(text, depth);
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text);
				}
				return false;
			}
			obj = null;
			return true;
		}
		if (patch.ValueKind != JsonValueKind.Object)
		{
			SerializedMember data = SerializedMember.FromJson(objType ?? obj?.GetType() ?? typeof(object), patch);
			return TryModify(ref obj, data, objType, depth, logs, flags, logger);
		}
		if (depth > 0 && objType != null && !patch.TryGetProperty("$type", out var _))
		{
			IReflectionConverter converter = Converters.GetConverter(objType);
			if (converter != null && converter.TreatJsonObjectAsAtomicValue(objType))
			{
				SerializedMember data2 = SerializedMember.FromJson(objType, patch);
				return TryModify(ref obj, data2, objType, depth, logs, flags, logger);
			}
		}
		string text2 = null;
		if (patch.TryGetProperty("$type", out var value2))
		{
			text2 = value2.GetString();
		}
		bool flag = true;
		if (text2 != null)
		{
			Type type = TypeUtils.GetType(text2);
			Type type2 = objType ?? obj?.GetType();
			if (type == null)
			{
				string text3 = "$type hint '" + text2 + "' could not be resolved to a known type.";
				logs?.Error(text3, depth);
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text3);
				}
				flag = false;
			}
			else if (type2 != null && !type2.IsAssignableFrom(type))
			{
				string text4 = "$type hint '" + text2 + "' ('" + type.GetTypeShortName() + "') is not assignable to declared type '" + type2.GetTypeShortName() + "'.";
				logs?.Error(text4, depth);
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text4);
				}
				flag = false;
			}
			else if (obj != null && type != obj.GetType())
			{
				obj = null;
				objType = type;
			}
			else if (objType == null || (obj == null && type != objType))
			{
				objType = type;
			}
		}
		if (obj == null && objType != null)
		{
			try
			{
				obj = CreateInstance(objType);
			}
			catch (Exception ex)
			{
				string text5 = "Cannot create instance of type '" + objType.GetTypeShortName() + "' for patch application: " + ex.Message;
				logs?.Error(text5, depth);
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text5);
				}
				return false;
			}
			if (obj == null)
			{
				string text6 = "Cannot create instance of type '" + objType.GetTypeShortName() + "' for patch application.";
				logs?.Error(text6, depth);
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text6);
				}
				return false;
			}
		}
		if (obj == null)
		{
			string text7 = "Cannot apply JSON patch: target object is null and no type is known.";
			logs?.Error(text7, depth);
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(padding + text7);
			}
			return false;
		}
		objType = obj.GetType();
		foreach (JsonProperty item in patch.EnumerateObject())
		{
			if (!(item.Name == "$type"))
			{
				bool flag2 = ((!TryParseBracketSegment(item.Name, out string innerKey)) ? TryPatchMember(ref obj, item.Name, item.Value, objType, depth, logs, flags, logger) : TryPatchBracketed(ref obj, item.Name, innerKey, item.Value, objType, depth, logs, flags, logger));
				flag &= flag2;
			}
		}
		return flag;
	}

	private bool TryPatchMember(ref object? obj, string memberName, JsonElement patchValue, Type? objType, int depth, Logs? logs, BindingFlags flags, ILogger? logger)
	{
		if (objType == null || obj == null)
		{
			string text = "Cannot navigate to member '" + memberName + "': object is null.";
			logs?.Error(text, depth);
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(StringUtils.GetPadding(depth) + text);
			}
			return false;
		}
		string padding = StringUtils.GetPadding(depth);
		FieldInfo field = TypeMemberUtils.GetField(objType, flags, memberName);
		if (field != null)
		{
			object currentValue;
			try
			{
				currentValue = field.GetValue(obj);
			}
			catch (Exception ex)
			{
				string text2 = "Failed to get value of field '" + memberName + "' on type '" + objType.GetTypeShortName() + "': " + ex.Message;
				logs?.Error(text2, depth);
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text2);
				}
				return false;
			}
			ApplyPatchTypeReplacement(ref currentValue, patchValue, field.FieldType);
			Type objType2 = currentValue?.GetType() ?? field.FieldType;
			bool flag = TryPatchInternal(ref currentValue, patchValue, objType2, depth + 1, logs, flags, logger);
			if (flag)
			{
				try
				{
					field.SetValue(obj, currentValue);
				}
				catch (Exception ex2)
				{
					string text3 = "Failed to set value of field '" + memberName + "' on type '" + objType.GetTypeShortName() + "': " + ex2.Message;
					logs?.Error(text3, depth);
					if (logger != null && logger.IsEnabled(LogLevel.Error))
					{
						logger.LogError(padding + text3);
					}
					return false;
				}
			}
			return flag;
		}
		PropertyInfo property = TypeMemberUtils.GetProperty(objType, flags, memberName);
		if (property != null)
		{
			if (!property.CanWrite)
			{
				string text4 = "Property '" + memberName + "' on type '" + objType.GetTypeShortName() + "' is read-only.";
				logs?.Error(text4, depth);
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text4);
				}
				return false;
			}
			object currentValue2 = null;
			if (property.CanRead)
			{
				try
				{
					currentValue2 = property.GetValue(obj);
				}
				catch (Exception ex3)
				{
					string text5 = "Failed to get value of property '" + memberName + "' on type '" + objType.GetTypeShortName() + "': " + ex3.Message;
					logs?.Error(text5, depth);
					if (logger != null && logger.IsEnabled(LogLevel.Error))
					{
						logger.LogError(padding + text5);
					}
					return false;
				}
			}
			ApplyPatchTypeReplacement(ref currentValue2, patchValue, property.PropertyType);
			Type objType3 = currentValue2?.GetType() ?? property.PropertyType;
			bool flag2 = TryPatchInternal(ref currentValue2, patchValue, objType3, depth + 1, logs, flags, logger);
			if (flag2)
			{
				try
				{
					property.SetValue(obj, currentValue2);
				}
				catch (Exception ex4)
				{
					string text6 = "Failed to set value of property '" + memberName + "' on type '" + objType.GetTypeShortName() + "': " + ex4.Message;
					logs?.Error(text6, depth);
					if (logger != null && logger.IsEnabled(LogLevel.Error))
					{
						logger.LogError(padding + text6);
					}
					return false;
				}
			}
			return flag2;
		}
		List<string> list = (from f in objType.GetFields(flags)
			select f.Name).ToList();
		List<string> list2 = (from p in objType.GetProperties(flags)
			select p.Name).ToList();
		string text7 = ((list.Count > 0) ? string.Join(", ", list) : "none");
		string text8 = ((list2.Count > 0) ? string.Join(", ", list2) : "none");
		string text9 = "Segment '" + memberName + "' not found on type '" + objType.GetTypeShortName() + "'.\nAvailable fields: " + text7 + "\nAvailable properties: " + text8;
		logs?.Error(text9, depth);
		if (logger != null && logger.IsEnabled(LogLevel.Error))
		{
			logger.LogError(padding + text9);
		}
		return false;
	}

	private bool TryPatchBracketed(ref object? obj, string segment, string innerKey, JsonElement patchValue, Type? objType, int depth, Logs? logs, BindingFlags flags, ILogger? logger)
	{
		if (obj == null || objType == null)
		{
			string text = "Cannot navigate bracket segment '" + segment + "': object is null.";
			logs?.Error(text, depth);
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(StringUtils.GetPadding(depth) + text);
			}
			return false;
		}
		string padding = StringUtils.GetPadding(depth);
		if (obj is IList)
		{
			if (!int.TryParse(innerKey, out var result))
			{
				string text2 = "Bracket segment '" + segment + "' cannot be used as index on type '" + objType.GetTypeShortName() + "'. Expected integer index.";
				logs?.Error(text2, depth);
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text2);
				}
				return false;
			}
			return TryPatchArrayIndex(ref obj, segment, result, patchValue, objType, depth, logs, flags, logger);
		}
		if (TypeUtils.IsDictionary(objType))
		{
			Type[]? dictionaryGenericArguments = TypeUtils.GetDictionaryGenericArguments(objType);
			Type type = ((dictionaryGenericArguments != null) ? dictionaryGenericArguments[0] : null) ?? typeof(object);
			Type valueType = ((dictionaryGenericArguments != null) ? dictionaryGenericArguments[1] : null) ?? typeof(object);
			object dictKey;
			try
			{
				dictKey = Convert.ChangeType(innerKey, type);
			}
			catch (Exception ex)
			{
				string text3 = "Bracket segment '" + segment + "' cannot be converted to key type '" + type.GetTypeShortName() + "' on type '" + objType.GetTypeShortName() + "': " + ex.Message;
				logs?.Error(text3, depth);
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text3);
				}
				return false;
			}
			return TryPatchDictKey(ref obj, segment, dictKey, patchValue, valueType, depth, logs, flags, logger);
		}
		string text4 = "Bracket segment '" + segment + "' cannot be used on type '" + objType.GetTypeShortName() + "'. Type is not an array, list, or dictionary.";
		logs?.Error(text4, depth);
		if (logger != null && logger.IsEnabled(LogLevel.Error))
		{
			logger.LogError(padding + text4);
		}
		return false;
	}

	private bool TryPatchArrayIndex(ref object? obj, string segment, int idx, JsonElement patchValue, Type objType, int depth, Logs? logs, BindingFlags flags, ILogger? logger)
	{
		Type enumerableItemType = TypeUtils.GetEnumerableItemType(objType);
		string padding = StringUtils.GetPadding(depth);
		if (obj is Array array)
		{
			if (idx < 0 || idx >= array.Length)
			{
				string text = $"Bracket segment '{segment}' index out of range on type '{objType.GetTypeShortName()}'. Array length is {array.Length}.";
				logs?.Error(text, depth);
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text);
				}
				return false;
			}
			object currentValue = array.GetValue(idx);
			ApplyPatchTypeReplacement(ref currentValue, patchValue, enumerableItemType ?? typeof(object));
			Type objType2 = currentValue?.GetType() ?? enumerableItemType;
			bool num = TryPatchInternal(ref currentValue, patchValue, objType2, depth + 1, logs, flags, logger);
			if (num)
			{
				array.SetValue(currentValue, idx);
			}
			return num;
		}
		if (obj is IList list)
		{
			if (idx < 0 || idx >= list.Count)
			{
				string text2 = $"Bracket segment '{segment}' index out of range on type '{objType.GetTypeShortName()}'. List count is {list.Count}.";
				logs?.Error(text2, depth);
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text2);
				}
				return false;
			}
			object currentValue2 = list[idx];
			ApplyPatchTypeReplacement(ref currentValue2, patchValue, enumerableItemType ?? typeof(object));
			Type objType3 = currentValue2?.GetType() ?? enumerableItemType;
			bool num2 = TryPatchInternal(ref currentValue2, patchValue, objType3, depth + 1, logs, flags, logger);
			if (num2)
			{
				list[idx] = currentValue2;
			}
			return num2;
		}
		string text3 = "Bracket segment '" + segment + "' cannot be applied: type '" + objType.GetTypeShortName() + "' is not an array or list.";
		logs?.Error(text3, depth);
		if (logger != null && logger.IsEnabled(LogLevel.Error))
		{
			logger.LogError(padding + text3);
		}
		return false;
	}

	private bool TryPatchDictKey(ref object? obj, string segment, object dictKey, JsonElement patchValue, Type valueType, int depth, Logs? logs, BindingFlags flags, ILogger? logger)
	{
		IDictionary dictionary = (IDictionary)obj;
		object currentValue = (dictionary.Contains(dictKey) ? dictionary[dictKey] : null);
		ApplyPatchTypeReplacement(ref currentValue, patchValue, valueType);
		Type objType = currentValue?.GetType() ?? valueType;
		bool num = TryPatchInternal(ref currentValue, patchValue, objType, depth + 1, logs, flags, logger);
		if (num)
		{
			dictionary[dictKey] = currentValue;
		}
		return num;
	}

	private static void ApplyPatchTypeReplacement(ref object? currentValue, JsonElement patchValue, Type declaredType)
	{
		if (patchValue.ValueKind != JsonValueKind.Object || !patchValue.TryGetProperty("$type", out var value))
		{
			return;
		}
		string text = value.GetString();
		if (!string.IsNullOrEmpty(text))
		{
			Type type = TypeUtils.GetType(text);
			if (type != null && currentValue != null && type != currentValue.GetType() && declaredType.IsAssignableFrom(type))
			{
				currentValue = null;
			}
		}
	}

	public bool TryReadAt(object? obj, string path, out SerializedMember? result, Type? fallbackObjType = null, int depth = 0, Logs? logs = null, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		string[] array = ParsePath(path);
		object obj2 = obj;
		Type objType = obj?.GetType() ?? fallbackObjType;
		for (int i = 0; i < array.Length; i++)
		{
			if (!TryNavigateOneSegment(ref obj2, ref objType, array[i], depth + i, logs, flags, logger))
			{
				result = null;
				return false;
			}
		}
		object obj3 = obj2;
		Type fallbackType = objType;
		int depth2 = depth + array.Length;
		result = Serialize(obj3, fallbackType, null, recursive: true, flags, depth2, logs, logger);
		return true;
	}

	private bool TryNavigateOneSegment(ref object? obj, ref Type? objType, string segment, int depth, Logs? logs, BindingFlags flags, ILogger? logger)
	{
		string padding = StringUtils.GetPadding(depth);
		if (obj == null)
		{
			string text = "Cannot navigate segment '" + segment + "': object is null.";
			logs?.Error(text, depth);
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(padding + text);
			}
			return false;
		}
		Type type = obj.GetType();
		if (TryParseBracketSegment(segment, out string innerKey))
		{
			if (obj is IList list)
			{
				if (!int.TryParse(innerKey, out var result))
				{
					string text2 = "Bracket segment '" + segment + "' cannot be used as index on type '" + type.GetTypeShortName() + "'. Expected integer index.";
					logs?.Error(text2, depth);
					if (logger != null && logger.IsEnabled(LogLevel.Error))
					{
						logger.LogError(padding + text2);
					}
					return false;
				}
				if (result < 0 || result >= list.Count)
				{
					string text3 = $"Bracket segment '{segment}' index out of range on type '{type.GetTypeShortName()}'. Array/list length is {list.Count}.";
					logs?.Error(text3, depth);
					if (logger != null && logger.IsEnabled(LogLevel.Error))
					{
						logger.LogError(padding + text3);
					}
					return false;
				}
				obj = list[result];
				objType = TypeUtils.GetEnumerableItemType(type);
				return true;
			}
			if (TypeUtils.IsDictionary(type))
			{
				Type[]? dictionaryGenericArguments = TypeUtils.GetDictionaryGenericArguments(type);
				Type type2 = ((dictionaryGenericArguments != null) ? dictionaryGenericArguments[0] : null) ?? typeof(object);
				Type type3 = ((dictionaryGenericArguments != null) ? dictionaryGenericArguments[1] : null) ?? typeof(object);
				object key;
				try
				{
					key = Convert.ChangeType(innerKey, type2);
				}
				catch (Exception ex)
				{
					string text4 = "Bracket segment '" + segment + "' cannot be converted to key type '" + type2.GetTypeShortName() + "' on type '" + type.GetTypeShortName() + "': " + ex.Message;
					logs?.Error(text4, depth);
					if (logger != null && logger.IsEnabled(LogLevel.Error))
					{
						logger.LogError(padding + text4);
					}
					return false;
				}
				IDictionary dictionary = (IDictionary)obj;
				if (!dictionary.Contains(key))
				{
					string text5 = "Bracket segment '" + segment + "' key not found in dictionary of type '" + type.GetTypeShortName() + "'.";
					logs?.Error(text5, depth);
					if (logger != null && logger.IsEnabled(LogLevel.Error))
					{
						logger.LogError(padding + text5);
					}
					return false;
				}
				obj = dictionary[key];
				objType = type3;
				return true;
			}
			string text6 = "Bracket segment '" + segment + "' cannot be used on type '" + type.GetTypeShortName() + "'. Type is not an array, list, or dictionary.";
			logs?.Error(text6, depth);
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(padding + text6);
			}
			return false;
		}
		FieldInfo field = TypeMemberUtils.GetField(type, flags, segment);
		if (field != null)
		{
			try
			{
				obj = field.GetValue(obj);
				objType = field.FieldType;
				return true;
			}
			catch (Exception ex2)
			{
				string text7 = "Failed to read field '" + segment + "' on type '" + type.GetTypeShortName() + "': " + ex2.Message;
				logs?.Error(text7, depth);
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text7);
				}
				return false;
			}
		}
		PropertyInfo property = TypeMemberUtils.GetProperty(type, flags, segment);
		if (property != null)
		{
			if (!property.CanRead)
			{
				string text8 = "Property '" + segment + "' on type '" + type.GetTypeShortName() + "' is write-only and cannot be read.";
				logs?.Error(text8, depth);
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text8);
				}
				return false;
			}
			try
			{
				obj = property.GetValue(obj);
				objType = property.PropertyType;
				return true;
			}
			catch (Exception ex3)
			{
				string text9 = "Failed to read property '" + segment + "' on type '" + type.GetTypeShortName() + "': " + ex3.Message;
				logs?.Error(text9, depth);
				if (logger != null && logger.IsEnabled(LogLevel.Error))
				{
					logger.LogError(padding + text9);
				}
				return false;
			}
		}
		IEnumerable<FieldInfo> serializableFields = GetSerializableFields(type, flags, logger);
		IEnumerable<PropertyInfo> serializableProperties = GetSerializableProperties(type, flags, logger);
		string text10 = ((serializableFields != null) ? string.Join(", ", serializableFields.Select((FieldInfo f) => f.Name)) : "none");
		string text11 = ((serializableProperties != null) ? string.Join(", ", serializableProperties.Select((PropertyInfo p) => p.Name)) : "none");
		if (text10.Length == 0)
		{
			text10 = "none";
		}
		if (text11.Length == 0)
		{
			text11 = "none";
		}
		string text12 = "Segment '" + segment + "' not found on type '" + type.GetTypeShortName() + "'.\nAvailable fields: " + text10 + "\nAvailable properties: " + text11;
		logs?.Error(text12, depth);
		if (logger != null && logger.IsEnabled(LogLevel.Error))
		{
			logger.LogError(padding + text12);
		}
		return false;
	}

	public SerializedMember Serialize(object? obj, Type? fallbackType = null, string? name = null, bool recursive = true, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, int depth = 0, Logs? logs = null, ILogger? logger = null, SerializationContext? context = null)
	{
		if (context == null)
		{
			context = new SerializationContext();
		}
		if (obj != null && !context.Enter(obj, name))
		{
			return SerializedMember.FromReference(context.GetPath(obj), name);
		}
		try
		{
			Type typeWithObjectPriority = TypeUtils.GetTypeWithObjectPriority(obj, fallbackType, out string error);
			if (typeWithObjectPriority == null)
			{
				throw new ArgumentException(error);
			}
			if (Converters.IsTypeBlacklisted(typeWithObjectPriority))
			{
				if (logger != null && logger.IsEnabled(LogLevel.Trace))
				{
					logger.LogTrace("{padding}Serialize skip for '{name}' of type '{type}', it is blacklisted type.", StringUtils.GetPadding(depth), name.ValueOrNull(), typeWithObjectPriority.GetTypeId().ValueOrNull());
				}
				return SerializedMember.Null(typeWithObjectPriority, name);
			}
			if (obj == null)
			{
				if (typeWithObjectPriority.IsInterface)
				{
					if (logger != null && logger.IsEnabled(LogLevel.Trace))
					{
						logger.LogTrace("{padding}Serialize null '{name}' of interface type '{type}'.", StringUtils.GetPadding(depth), name.ValueOrNull(), typeWithObjectPriority.GetTypeId().ValueOrNull());
					}
					return SerializedMember.Null(typeWithObjectPriority, name);
				}
				if (typeWithObjectPriority.IsAbstract)
				{
					if (logger != null && logger.IsEnabled(LogLevel.Trace))
					{
						logger.LogTrace("{padding}Serialize null '{name}' of abstract type '{type}'.", StringUtils.GetPadding(depth), name.ValueOrNull(), typeWithObjectPriority.GetTypeId().ValueOrNull());
					}
					return SerializedMember.Null(typeWithObjectPriority, name);
				}
			}
			JsonConverter jsonConverter = JsonSerializer.GetJsonConverter(typeWithObjectPriority);
			if (jsonConverter != null)
			{
				if (logger != null && logger.IsEnabled(LogLevel.Trace))
				{
					logger.LogTrace("{padding}Serialize '{name}' of type '{type}'. JsonConverter: {converter}", StringUtils.GetPadding(depth), name.ValueOrNull(), typeWithObjectPriority.GetTypeId().ValueOrNull(), jsonConverter.GetType().GetTypeId().ValueOrNull());
				}
				return SerializedMember.FromJson(typeWithObjectPriority, obj.ToJson(this, null, depth, logger), name);
			}
			IReflectionConverter converter = Converters.GetConverter(typeWithObjectPriority);
			if (logger != null && logger.IsEnabled(LogLevel.Trace))
			{
				logger.LogTrace("{padding}Serialize '{name}' of type '{type}'. ReflectionConverter: {converter}", StringUtils.GetPadding(depth), name.ValueOrNull(), typeWithObjectPriority.GetTypeId().ValueOrNull(), converter?.GetType().GetTypeShortName()?.ValueOrNull());
			}
			if (converter == null)
			{
				throw new ArgumentException("Failed to serialize '" + name.ValueOrNull() + "'. Type '" + typeWithObjectPriority.GetTypeId().ValueOrNull() + "' not supported for serialization.");
			}
			return converter.Serialize(this, obj, typeWithObjectPriority, name, recursive, flags, depth, logs, logger, context);
		}
		finally
		{
			if (obj != null)
			{
				context.Exit(obj, name);
			}
		}
	}

	public IEnumerable<FieldInfo>? GetSerializableFields(Type type, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		return Converters.GetConverter(type)?.GetSerializableFields(this, type, flags, logger);
	}

	public IEnumerable<PropertyInfo>? GetSerializableProperties(Type type, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		return Converters.GetConverter(type)?.GetSerializableProperties(this, type, flags, logger);
	}

	public SerializedMember? View(object? obj, ViewQuery? query = null, Type? fallbackObjType = null, int depth = 0, Logs? logs = null, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		object obj2 = obj;
		Type objType = obj?.GetType() ?? fallbackObjType;
		if (!string.IsNullOrEmpty(query?.Path))
		{
			string[] array = ParsePath(query.Path);
			for (int i = 0; i < array.Length; i++)
			{
				if (!TryNavigateOneSegment(ref obj2, ref objType, array[i], depth + i, logs, flags, logger))
				{
					return null;
				}
			}
			depth += array.Length;
		}
		SerializedMember serializedMember;
		try
		{
			object obj3 = obj2;
			Type fallbackType = objType ?? fallbackObjType;
			int depth2 = depth;
			serializedMember = Serialize(obj3, fallbackType, null, recursive: true, flags, depth2, logs, logger);
		}
		catch (ArgumentException ex)
		{
			logs?.Error(ex.Message, depth);
			if (logger != null && logger.IsEnabled(LogLevel.Error))
			{
				logger.LogError(StringUtils.GetPadding(depth) + ex.Message);
			}
			return null;
		}
		if (serializedMember == null)
		{
			return null;
		}
		if (query != null)
		{
			if (query.MaxDepth.HasValue)
			{
				serializedMember = PruneToDepth(serializedMember, query.MaxDepth.Value);
			}
			if (!string.IsNullOrEmpty(query.NamePattern))
			{
				Regex regex = TryCompilePattern(query.NamePattern, logs, depth);
				serializedMember = ((regex != null) ? FilterByNamePattern(serializedMember, regex) : null) ?? new SerializedMember
				{
					name = serializedMember.name,
					typeName = serializedMember.typeName
				};
			}
			if (query.TypeFilter != null)
			{
				serializedMember = FilterByType(serializedMember, query.TypeFilter) ?? new SerializedMember
				{
					name = serializedMember.name,
					typeName = serializedMember.typeName
				};
			}
		}
		return serializedMember;
	}

	public IReadOnlyList<ViewMatch> Grep(object? obj, string namePattern, int? maxDepth = null, Type? fallbackObjType = null, int depth = 0, Logs? logs = null, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, ILogger? logger = null)
	{
		Regex regex = TryCompilePattern(namePattern, logs, depth);
		if (regex == null)
		{
			return new List<ViewMatch>().AsReadOnly();
		}
		List<ViewMatch> list = new List<ViewMatch>();
		HashSet<object> visited = new HashSet<object>(ObjectReferenceEqualityComparer.Instance);
		GrepWalk(obj, regex, maxDepth, 0, string.Empty, list, visited, depth, logs, flags, logger);
		return list.AsReadOnly();
	}

	private void GrepWalk(object? obj, Regex rx, int? maxDepth, int currentDepth, string currentPath, List<ViewMatch> matches, HashSet<object> visited, int serializeDepth, Logs? logs, BindingFlags flags, ILogger? logger)
	{
		if (obj == null || (maxDepth.HasValue && currentDepth > maxDepth.Value))
		{
			return;
		}
		Type type = obj.GetType();
		if (!type.IsValueType && !visited.Add(obj))
		{
			return;
		}
		if (obj is IList list)
		{
			for (int i = 0; i < list.Count; i++)
			{
				string currentPath2 = ((currentPath.Length == 0) ? $"[{i}]" : $"{currentPath}/[{i}]");
				GrepWalk(list[i], rx, maxDepth, currentDepth + 1, currentPath2, matches, visited, serializeDepth, logs, flags, logger);
			}
			return;
		}
		IEnumerable<FieldInfo> serializableFields = GetSerializableFields(type, flags, logger);
		if (serializableFields != null)
		{
			foreach (FieldInfo item in serializableFields)
			{
				string text = ((currentPath.Length == 0) ? item.Name : (currentPath + "/" + item.Name));
				object value;
				try
				{
					value = item.GetValue(obj);
				}
				catch
				{
					continue;
				}
				if (rx.IsMatch(item.Name))
				{
					Type fieldType = item.FieldType;
					string name = item.Name;
					int depth = serializeDepth;
					Logs logs2 = logs;
					SerializedMember value2 = Serialize(value, fieldType, name, recursive: true, flags, depth, logs2, logger);
					matches.Add(new ViewMatch(text, value2));
				}
				if (!TypeUtils.IsPrimitive(item.FieldType))
				{
					GrepWalk(value, rx, maxDepth, currentDepth + 1, text, matches, visited, serializeDepth, logs, flags, logger);
				}
			}
		}
		IEnumerable<PropertyInfo> serializableProperties = GetSerializableProperties(type, flags, logger);
		if (serializableProperties != null)
		{
			foreach (PropertyInfo item2 in serializableProperties)
			{
				if (item2.CanRead)
				{
					string text2 = ((currentPath.Length == 0) ? item2.Name : (currentPath + "/" + item2.Name));
					object value3;
					try
					{
						value3 = item2.GetValue(obj);
					}
					catch
					{
						continue;
					}
					if (rx.IsMatch(item2.Name))
					{
						Type propertyType = item2.PropertyType;
						string name2 = item2.Name;
						int depth = serializeDepth;
						Logs logs2 = logs;
						SerializedMember value4 = Serialize(value3, propertyType, name2, recursive: true, flags, depth, logs2, logger);
						matches.Add(new ViewMatch(text2, value4));
					}
					if (!TypeUtils.IsPrimitive(item2.PropertyType))
					{
						GrepWalk(value3, rx, maxDepth, currentDepth + 1, text2, matches, visited, serializeDepth, logs, flags, logger);
					}
				}
			}
		}
		if (!TypeUtils.IsDictionary(type))
		{
			return;
		}
		IDictionary dictionary = (IDictionary)obj;
		foreach (object key in dictionary.Keys)
		{
			string currentPath3 = ((currentPath.Length == 0) ? $"[{key}]" : $"{currentPath}/[{key}]");
			GrepWalk(dictionary[key], rx, maxDepth, currentDepth + 1, currentPath3, matches, visited, serializeDepth, logs, flags, logger);
		}
	}

	private static SerializedMember PruneToDepth(SerializedMember m, int maxRelativeDepth)
	{
		if (maxRelativeDepth <= 0)
		{
			return new SerializedMember
			{
				name = m.name,
				typeName = m.typeName,
				valueJsonElement = m.valueJsonElement
			};
		}
		SerializedMember serializedMember = new SerializedMember
		{
			name = m.name,
			typeName = m.typeName,
			valueJsonElement = m.valueJsonElement
		};
		if (m.fields != null)
		{
			serializedMember.fields = new SerializedMemberList(m.fields.Count);
			foreach (SerializedMember field in m.fields)
			{
				serializedMember.fields.Add(PruneToDepth(field, maxRelativeDepth - 1));
			}
		}
		if (m.props != null)
		{
			serializedMember.props = new SerializedMemberList(m.props.Count);
			foreach (SerializedMember prop in m.props)
			{
				serializedMember.props.Add(PruneToDepth(prop, maxRelativeDepth - 1));
			}
		}
		return serializedMember;
	}

	private static SerializedMember? FilterByNamePattern(SerializedMember m, Regex rx)
	{
		bool flag = m.name != null && rx.IsMatch(m.name);
		SerializedMemberList serializedMemberList = FilterListByNamePattern(m.fields, rx);
		SerializedMemberList serializedMemberList2 = FilterListByNamePattern(m.props, rx);
		if (!flag && serializedMemberList == null && serializedMemberList2 == null)
		{
			return null;
		}
		return new SerializedMember
		{
			name = m.name,
			typeName = m.typeName,
			valueJsonElement = m.valueJsonElement,
			fields = (flag ? m.fields : serializedMemberList),
			props = (flag ? m.props : serializedMemberList2)
		};
	}

	private static SerializedMemberList? FilterListByNamePattern(SerializedMemberList? list, Regex rx)
	{
		if (list == null || list.Count == 0)
		{
			return null;
		}
		SerializedMemberList serializedMemberList = null;
		foreach (SerializedMember item in list)
		{
			SerializedMember serializedMember = FilterByNamePattern(item, rx);
			if (serializedMember != null)
			{
				if (serializedMemberList == null)
				{
					serializedMemberList = new SerializedMemberList();
				}
				serializedMemberList.Add(serializedMember);
			}
		}
		return serializedMemberList;
	}

	private static SerializedMember? FilterByType(SerializedMember m, Type typeFilter)
	{
		Type type = TypeUtils.GetType(m.typeName);
		bool flag = type != null && typeFilter.IsAssignableFrom(type);
		SerializedMemberList serializedMemberList = FilterListByType(m.fields, typeFilter);
		SerializedMemberList serializedMemberList2 = FilterListByType(m.props, typeFilter);
		if (!flag && serializedMemberList == null && serializedMemberList2 == null)
		{
			return null;
		}
		return new SerializedMember
		{
			name = m.name,
			typeName = m.typeName,
			valueJsonElement = m.valueJsonElement,
			fields = (flag ? m.fields : serializedMemberList),
			props = (flag ? m.props : serializedMemberList2)
		};
	}

	private static SerializedMemberList? FilterListByType(SerializedMemberList? list, Type typeFilter)
	{
		if (list == null || list.Count == 0)
		{
			return null;
		}
		SerializedMemberList serializedMemberList = null;
		foreach (SerializedMember item in list)
		{
			SerializedMember serializedMember = FilterByType(item, typeFilter);
			if (serializedMember != null)
			{
				if (serializedMemberList == null)
				{
					serializedMemberList = new SerializedMemberList();
				}
				serializedMemberList.Add(serializedMember);
			}
		}
		return serializedMemberList;
	}

	private static Regex? TryCompilePattern(string pattern, Logs? logs, int depth = 0)
	{
		try
		{
			return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
		}
		catch (ArgumentException ex)
		{
			logs?.Error("Invalid regex pattern '" + pattern + "': " + ex.Message, depth);
			return null;
		}
	}
}
}
