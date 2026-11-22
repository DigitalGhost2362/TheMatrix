using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Feeder.Json;
using Feeder.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Feeder.Reflect.Model;
using Feeder.Reflect.Utils;
using Feeder.Logging;

namespace Feeder.Reflect
{
public class MethodWrapper
{
	protected readonly Reflector _reflector;

	protected readonly MethodInfo _methodInfo;

	protected readonly object? _targetInstance;

	protected readonly Type? _classType;

	protected readonly string? _description;

	protected readonly ILogger? _logger;

	protected readonly JsonNode? _inputSchema;

	protected readonly JsonNode? _outputSchema;

	public JsonNode? InputSchema => _inputSchema;

	public JsonNode? OutputSchema => _outputSchema;

	public string? Description => _description;

	public MethodWrapper(Reflector reflector, ILogger? logger, MethodInfo methodInfo)
	{
		_logger = logger;
		_reflector = reflector ?? throw new ArgumentNullException("reflector");
		_methodInfo = methodInfo ?? throw new ArgumentNullException("methodInfo");
		if (!methodInfo.IsStatic)
		{
			throw new ArgumentException("The provided method must be static.");
		}
		_description = CreateDescription(reflector, methodInfo);
		_inputSchema = CreateInputSchema(reflector, methodInfo);
		_outputSchema = CreateOutputSchema(reflector, methodInfo);
	}

	public MethodWrapper(Reflector reflector, ILogger? logger, object targetInstance, MethodInfo methodInfo)
	{
		_logger = logger;
		_reflector = reflector ?? throw new ArgumentNullException("reflector");
		_targetInstance = targetInstance ?? throw new ArgumentNullException("targetInstance");
		_methodInfo = methodInfo ?? throw new ArgumentNullException("methodInfo");
		if (methodInfo.IsStatic)
		{
			throw new ArgumentException("The provided method must be an instance method. Use the other constructor for static methods.");
		}
		_description = CreateDescription(reflector, methodInfo);
		_inputSchema = CreateInputSchema(reflector, methodInfo);
		_outputSchema = CreateOutputSchema(reflector, methodInfo);
	}

	public MethodWrapper(Reflector reflector, ILogger? logger, Type classType, MethodInfo methodInfo)
	{
		_logger = logger;
		_reflector = reflector ?? throw new ArgumentNullException("reflector");
		_classType = classType ?? throw new ArgumentNullException("classType");
		_methodInfo = methodInfo ?? throw new ArgumentNullException("methodInfo");
		if (methodInfo.IsStatic)
		{
			throw new ArgumentException("The provided method must be an instance method. Use the other constructor for static methods.");
		}
		_description = CreateDescription(reflector, methodInfo);
		_inputSchema = CreateInputSchema(reflector, methodInfo);
		_outputSchema = CreateOutputSchema(reflector, methodInfo);
	}

	protected virtual string? CreateDescription(Reflector reflector, MethodInfo methodInfo)
	{
		return methodInfo.GetCustomAttribute<DescriptionAttribute>()?.Description;
	}

	protected virtual JsonNode? CreateInputSchema(Reflector reflector, MethodInfo methodInfo)
	{
		return reflector.GetArgumentsSchema(methodInfo);
	}

	protected virtual JsonNode? CreateOutputSchema(Reflector reflector, MethodInfo methodInfo)
	{
		return reflector.GetReturnSchema(methodInfo);
	}

	public virtual Task<object?> Invoke(params object?[] parameters)
	{
		return Invoke(CancellationToken.None, parameters);
	}

	public virtual async Task<object?> Invoke(CancellationToken cancellationToken, params object?[] parameters)
	{
		object obj = _targetInstance ?? ((_classType != null) ? Activator.CreateInstance(_classType) : null);
		object[] parameters2 = BuildParameters(parameters);
		PrintParameters(parameters2);
		object obj2 = _methodInfo.Invoke(obj, parameters2);
		if (obj2 is Task task)
		{
			await task.WithCancellation(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			return task.GetType().GetProperty("Result")?.GetValue(task);
		}
		if (obj2 is ValueTask valueTask)
		{
			await valueTask.AsTask().WithCancellation(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			return valueTask.GetType().GetProperty("Result")?.GetValue(valueTask);
		}
		return obj2;
	}

	public virtual async Task<object?> InvokeDict(IReadOnlyDictionary<string, object?>? namedParameters, CancellationToken cancellationToken = default(CancellationToken))
	{
		object obj = _targetInstance ?? ((_classType != null) ? _reflector.CreateInstance(_classType) : null);
		object[] parameters = BuildParameters(_reflector, namedParameters);
		PrintParameters(parameters);
		object obj2 = _methodInfo.Invoke(obj, parameters);
		if (obj2 is Task task)
		{
			await task.WithCancellation(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			return task.GetType().GetProperty("Result")?.GetValue(task);
		}
		return obj2;
	}

	public virtual bool VerifyParameters(IReadOnlyDictionary<string, object?>? namedParameters, out string? error)
	{
		ParameterInfo[] parameters = _methodInfo.GetParameters();
		if (parameters.Length == 0)
		{
			if (namedParameters == null || namedParameters.Count == 0)
			{
				error = null;
				return true;
			}
			error = $"Method '{_methodInfo.Name}' does not accept any parameters, but {namedParameters?.Count} were provided.";
			return false;
		}
		if (namedParameters == null)
		{
			error = "Method '" + _methodInfo.Name + "' requires parameters, but none were provided.";
			return false;
		}
		foreach (KeyValuePair<string, object> parameter in namedParameters)
		{
			ParameterInfo parameterInfo = parameters.FirstOrDefault((ParameterInfo p) => p.Name == parameter.Key);
			if (parameterInfo == null)
			{
				error = "Method '" + _methodInfo.Name + "' does not have a parameter named '" + parameter.Key + "'.";
				return false;
			}
			if (parameter.Value != null && !parameterInfo.ParameterType.IsInstanceOfType(parameter.Value))
			{
				error = $"Parameter '{parameter.Key}' type mismatch. Expected '{parameterInfo.ParameterType.GetTypeId()}', but got '{parameter.Value.GetType()}'.";
				return false;
			}
		}
		error = null;
		return true;
	}

	protected virtual object?[]? BuildParameters(object?[]? parameters)
	{
		ParameterInfo[] parameters2 = _methodInfo.GetParameters();
		object[] array = new object[parameters2.Length];
		for (int i = 0; i < parameters2.Length; i++)
		{
			if (parameters != null && i < parameters.Length)
			{
				array[i] = GetParameterValue(_reflector, parameters2[i], parameters[i]);
			}
			else
			{
				array[i] = GetDefaultParameterValue(_reflector, parameters2[i]);
			}
		}
		for (int j = 0; j < parameters2.Length; j++)
		{
			ParameterInfo parameterInfo = parameters2[j];
			if (array[j] != null && !parameterInfo.ParameterType.IsInstanceOfType(array[j]))
			{
				throw new ArgumentException($"Parameter '{parameterInfo.Name}' type mismatch. Expected '{parameterInfo.ParameterType.GetTypeId()}', but got '{array[j]?.GetType()}'.");
			}
		}
		return array;
	}

	protected virtual object? GetDefaultParameterValue(Reflector reflector, ParameterInfo methodParameter)
	{
		if (methodParameter.HasDefaultValue)
		{
			return methodParameter.DefaultValue;
		}
		throw new ArgumentException("No value provided for parameter '" + methodParameter.Name + "' and no default value is defined.");
	}

	protected virtual object? GetParameterValue(Reflector reflector, ParameterInfo methodParameter, object? value)
	{
		Type type = Nullable.GetUnderlyingType(methodParameter.ParameterType) ?? methodParameter.ParameterType;
		if (value is JsonElement value2)
		{
			if (type == typeof(string) && (value2.ValueKind == JsonValueKind.Object || value2.ValueKind == JsonValueKind.Array))
			{
				return value2.GetRawText();
			}
			if (!TypeUtils.IsPrimitive(type) && JsonUtils.TryUnstringifyJson(value2, out var result))
			{
				value = result;
				value2 = result.Value;
			}
			try
			{
				return value2.Deserialize(methodParameter.ParameterType, _reflector.JsonSerializerOptions);
			}
			catch (Exception ex)
			{
				try
				{
					SerializedMember serializedMember = value2.Deserialize<SerializedMember>();
					if (serializedMember == null)
					{
						throw new ArgumentException(string.Format("Failed to parse {0} for parameter '{1}'.\nInput value: {2}\nOriginal exception: {3}", "SerializedMember", methodParameter.Name, value2, ex.Message));
					}
					return _reflector.Deserialize(serializedMember, methodParameter.ParameterType, null, 0, null, _logger);
				}
				catch (Exception ex2)
				{
					throw new ArgumentException($"Unable to convert value to parameter '{methodParameter.Name}' of type '{methodParameter.ParameterType.GetTypeId()}'.\nInput value: {value2}\nOriginal exception: {ex.Message}\nSecond exception: {ex2.Message}");
				}
			}
		}
		if (type.IsInstanceOfType(value))
		{
			return value;
		}
		if (type.IsEnum)
		{
			return StringUtils.ConvertParameterStringToEnum(value, type, methodParameter.Name);
		}
		throw new ArgumentException($"Parameter '{methodParameter.Name}' type mismatch. Expected '{methodParameter.ParameterType.GetTypeId()}', but got '{value?.GetType()}'.");
	}

	protected virtual object?[]? BuildParameters(Reflector reflector, IReadOnlyDictionary<string, object?>? namedParameters)
	{
		ParameterInfo[] parameters = _methodInfo.GetParameters();
		object[] array = new object[parameters.Length];
		for (int i = 0; i < parameters.Length; i++)
		{
			ParameterInfo parameter = parameters[i];
			array[i] = GetParameterValue(reflector, parameter, namedParameters);
		}
		for (int j = 0; j < parameters.Length; j++)
		{
			ParameterInfo parameterInfo = parameters[j];
			if (array[j] != null && !parameterInfo.ParameterType.IsInstanceOfType(array[j]))
			{
				throw new ArgumentException($"Parameter '{parameterInfo.Name}' type mismatch. Expected '{parameterInfo.ParameterType.GetTypeId()}', but got '{array[j]?.GetType()}'.");
			}
		}
		return array;
	}

	protected virtual object? GetParameterValue(Reflector reflector, ParameterInfo parameter, IReadOnlyDictionary<string, object?>? namedParameters)
	{
		Type type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
		if (namedParameters != null && namedParameters.TryGetValue(parameter.Name, out object value))
		{
			if (value is JsonElement value2)
			{
				if (type == typeof(string) && (value2.ValueKind == JsonValueKind.Object || value2.ValueKind == JsonValueKind.Array))
				{
					return value2.GetRawText();
				}
				if (!TypeUtils.IsPrimitive(type) && JsonUtils.TryUnstringifyJson(value2, out var result))
				{
					value = result;
					value2 = result.Value;
				}
				try
				{
					return value2.Deserialize(parameter.ParameterType, _reflector.JsonSerializerOptions);
				}
				catch (Exception ex)
				{
					try
					{
						SerializedMember serializedMember = value2.Deserialize<SerializedMember>();
						if (serializedMember == null)
						{
							throw new ArgumentException(string.Format("Failed to parse {0} for parameter '{1}'.\nInput value: {2}\nOriginal exception: {3}", "SerializedMember", parameter.Name, value2, ex.Message));
						}
						return reflector.Deserialize(serializedMember, parameter.ParameterType, null, 0, null, _logger);
					}
					catch (Exception ex2)
					{
						throw new ArgumentException($"Unable to convert value to parameter '{parameter.Name}' of type '{parameter.ParameterType.GetTypeId()}'.\nInput value: {value2}\nOriginal exception: {ex.Message}\nSecond exception: {ex2.Message}");
					}
				}
			}
			if (type.IsInstanceOfType(value))
			{
				return value;
			}
			if (type.IsEnum)
			{
				return StringUtils.ConvertParameterStringToEnum(value, type, parameter.Name);
			}
			throw new ArgumentException($"Parameter '{parameter.Name}' type mismatch. Expected '{parameter.ParameterType.GetTypeId()}', but got '{value?.GetType()}'.");
		}
		if (parameter.HasDefaultValue)
		{
			return parameter.DefaultValue;
		}
		if (!parameter.ParameterType.IsValueType)
		{
			return null;
		}
		return Activator.CreateInstance(parameter.ParameterType);
	}

	protected virtual void PrintParameters(object?[]? parameters)
	{
		ILogger? logger = _logger;
		if (logger != null && logger.IsEnabled(LogLevel.Debug))
		{
			_logger.LogDebug((((parameters != null && parameters.Length != 0) ? 1 : 0) > (false ? 1 : 0)) ? ("Invoke method: " + _methodInfo.ReturnType.Name + " " + _methodInfo.Name + "(" + string.Join(", ", parameters.Select((object x) => x?.GetType()?.Name.ValueOrNull() ?? "")) + ")") : ("Invoke method: " + _methodInfo.ReturnType.Name + " " + _methodInfo.Name + "()"));
			ParameterInfo[] parameters2 = _methodInfo.GetParameters();
			int num = Math.Max(parameters2.Length, (parameters != null) ? parameters.Length : 0);
			string[] array = new string[num];
			for (int num2 = 0; num2 < num; num2++)
			{
				string text = ((num2 < parameters2.Length) ? parameters2[num2].ParameterType.ToString() : "N/A");
				string text2 = ((num2 < parameters2.Length) ? parameters2[num2].Name : "N/A");
				string text3 = ((num2 < ((parameters != null) ? parameters.Length : 0)) ? ((parameters != null) ? parameters[num2]?.ToString().ValueOrNull() : null) : "null");
				array[num2] = text + " " + text2 + " = " + text3;
			}
			string text4 = string.Join(Environment.NewLine, array);
			_logger?.LogDebug((text4.Length > 0) ? "Invoke method: Input: {0}, Provided: {1}\n{2}" : "Invoke method: Input: {0}, Provided: {1}{2}", parameters2.Length, parameters?.Length, text4);
		}
	}

	public static MethodWrapper Create(Reflector reflector, ILogger? logger, MethodInfo methodInfo)
	{
		if (!methodInfo.IsStatic)
		{
			return new MethodWrapper(reflector, logger, methodInfo.DeclaringType, methodInfo);
		}
		return new MethodWrapper(reflector, logger, methodInfo);
	}

	public static MethodWrapper CreateFromInstance(Reflector reflector, ILogger? logger, object targetInstance, MethodInfo methodInfo)
	{
		if (!methodInfo.IsStatic)
		{
			return new MethodWrapper(reflector, logger, targetInstance, methodInfo);
		}
		return new MethodWrapper(reflector, logger, methodInfo);
	}
}
}
