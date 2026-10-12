using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Feeder.Json;
using Feeder.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Feeder.ToolHost.Common.Model;
using Feeder.ToolHost.Utils;
using Feeder.Reflect;
using Feeder.Logging;

namespace Feeder.ToolHost
{
public class RunPrompt : MethodWrapper, IRunPrompt, IEnabled
{
	public bool Enabled { get; set; } = true;

	public string Name { get; protected set; }

	public string? Title { get; protected set; }

	public Role Role { get; protected set; }

	public MethodInfo Method => _methodInfo;

	protected string? RequestID { get; set; }

	public static RunPrompt CreateFromStaticMethod(Reflector reflector, string name, ILogger? logger, MethodInfo methodInfo, string? title = null, bool? enabled = null)
	{
		return new RunPrompt(reflector, name, logger, methodInfo)
		{
			Title = title,
			Enabled = (enabled ?? true)
		};
	}

	public static RunPrompt CreateFromInstanceMethod(Reflector reflector, ILogger? logger, string name, object targetInstance, MethodInfo methodInfo, string? title = null, bool? enabled = null)
	{
		return new RunPrompt(reflector, name, logger, targetInstance, methodInfo)
		{
			Title = title,
			Enabled = (enabled ?? true)
		};
	}

	public static RunPrompt CreateFromClassMethod(Reflector reflector, string name, ILogger? logger, Type classType, MethodInfo methodInfo, string? title = null, bool? enabled = null)
	{
		return new RunPrompt(reflector, name, logger, classType, methodInfo)
		{
			Title = title,
			Enabled = (enabled ?? true)
		};
	}

	public RunPrompt(Reflector reflector, string name, ILogger? logger, MethodInfo methodInfo)
		: base(reflector, logger, methodInfo)
	{
		Name = name;
		Role = GetFirstAiPromptAttribute(methodInfo)?.Role ?? Role.User;
	}

	public RunPrompt(Reflector reflector, string name, ILogger? logger, object targetInstance, MethodInfo methodInfo)
		: base(reflector, logger, targetInstance, methodInfo)
	{
		Name = name;
		Role = GetFirstAiPromptAttribute(methodInfo)?.Role ?? Role.User;
	}

	public RunPrompt(Reflector reflector, string name, ILogger? logger, Type classType, MethodInfo methodInfo)
		: base(reflector, logger, classType, methodInfo)
	{
		Name = name;
		Role = GetFirstAiPromptAttribute(methodInfo)?.Role ?? Role.User;
	}

	private static AiPromptAttribute? GetFirstAiPromptAttribute(MethodInfo methodInfo)
	{
		return methodInfo.GetCustomAttributes(typeof(AiPromptAttribute), inherit: false).Cast<AiPromptAttribute>().FirstOrDefault();
	}

	protected override object? GetParameterValue(Reflector reflector, ParameterInfo paramInfo, object? value)
	{
		if (paramInfo.GetCustomAttribute<RequestIDAttribute>() != null)
		{
			_logger?.LogTrace("Injecting RequestID parameter: {RequestID}", RequestID);
			return RequestID;
		}
		return FixEnumConversion(paramInfo, base.GetParameterValue(reflector, paramInfo, value));
	}

	protected override object? GetParameterValue(Reflector reflector, ParameterInfo paramInfo, IReadOnlyDictionary<string, object?>? namedParameters)
	{
		if (paramInfo.GetCustomAttribute<RequestIDAttribute>() != null)
		{
			_logger?.LogTrace("Injecting RequestID parameter: {RequestID}", RequestID);
			return RequestID;
		}
		return FixEnumConversion(paramInfo, base.GetParameterValue(reflector, paramInfo, namedParameters));
	}

	protected override object? GetDefaultParameterValue(Reflector reflector, ParameterInfo methodParameter)
	{
		if (methodParameter.GetCustomAttribute<RequestIDAttribute>() != null)
		{
			_logger?.LogTrace("Injecting RequestID parameter: {RequestID}", RequestID);
			return RequestID;
		}
		return FixEnumConversion(methodParameter, base.GetDefaultParameterValue(reflector, methodParameter));
	}

	private object? FixEnumConversion(ParameterInfo paramInfo, object? value)
	{
		if (value != null)
		{
			Type parameterType = paramInfo.ParameterType;
			Type type = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
			if (type.IsEnum && value.GetType() != type)
			{
				return Enum.ToObject(type, value);
			}
		}
		return value;
	}

	public async Task<ResponseGetPrompt> Run(string requestId, CancellationToken cancellationToken = default(CancellationToken), params object?[] parameters)
	{
		ResponseGetPrompt responseGetPrompt = ValidateRunParameters(requestId, parameters);
		if (responseGetPrompt != null)
		{
			return responseGetPrompt;
		}
		RequestID = requestId;
		try
		{
			string description = Method.GetCustomAttribute<DescriptionAttribute>()?.Description;
			object obj = await Invoke(cancellationToken, parameters);
			if (obj == null)
			{
				return ResponseGetPrompt.Error("[Error] No result returned").SetRequestID(requestId);
			}
			if (obj is ResponseGetPrompt responseGetPrompt2)
			{
				return responseGetPrompt2.SetRequestID(requestId);
			}
			return ResponseGetPrompt.Success(obj.ToString(), Role.Assistant, description).SetRequestID(requestId);
		}
		catch (ArgumentException ex)
		{
			string text = "Parameter validation failed for prompt '" + (Name ?? Method?.Name) + "': " + ex.Message;
			_logger?.LogError(ex, text);
			return ResponseGetPrompt.Error(text).SetRequestID(requestId);
		}
		catch (TargetParameterCountException exception)
		{
			string text2 = $"Parameter count mismatch for prompt '{Name ?? Method?.Name}'. Expected {Method?.GetParameters().Length} parameters, but received {parameters?.Length}";
			_logger?.LogError(exception, text2);
			return ResponseGetPrompt.Error(text2).SetRequestID(requestId);
		}
		catch (Exception ex2)
		{
			string text3 = "Prompt execution failed for '" + (Name ?? Method?.Name) + "': " + (ex2.InnerException ?? ex2).Message;
			_logger?.LogError(ex2, text3 + "\n" + ex2.StackTrace);
			return ResponseGetPrompt.Error(text3).SetRequestID(requestId);
		}
	}

	public async Task<ResponseGetPrompt> Run(string requestId, IReadOnlyDictionary<string, JsonElement>? namedParameters, CancellationToken cancellationToken = default(CancellationToken))
	{
		ResponseGetPrompt responseGetPrompt = ValidateRunParameters(requestId, namedParameters);
		if (responseGetPrompt != null)
		{
			return responseGetPrompt;
		}
		RequestID = requestId;
		try
		{
			Dictionary<string, object> namedParameters2 = ConvertNamedParameters(namedParameters);
			string description = Method.GetCustomAttribute<DescriptionAttribute>()?.Description;
			object obj = await InvokeDict(namedParameters2, cancellationToken);
			if (obj == null)
			{
				return ResponseGetPrompt.Error("[Error] No result returned").SetRequestID(requestId);
			}
			if (obj is ResponseGetPrompt responseGetPrompt2)
			{
				return responseGetPrompt2.SetRequestID(requestId);
			}
			return ResponseGetPrompt.Success(obj.ToString(), GetFirstAiPromptAttribute(Method)?.Role ?? Role.User, description).SetRequestID(requestId);
		}
		catch (ArgumentException ex)
		{
			string text = "Parameter validation failed for prompt '" + (Name ?? Method?.Name) + "': " + ex.Message;
			_logger?.LogError(ex, text);
			return ResponseGetPrompt.Error(text).SetRequestID(requestId);
		}
		catch (JsonException ex2)
		{
			string text2 = "JSON parameter parsing failed for prompt '" + (Name ?? Method?.Name) + "': " + ex2.Message;
			_logger?.LogError(ex2, text2);
			return ResponseGetPrompt.Error(text2).SetRequestID(requestId);
		}
		catch (Exception ex3)
		{
			string text3 = "Prompt execution failed for '" + (Name ?? Method?.Name) + "': " + (ex3.InnerException ?? ex3).Message;
			_logger?.LogError(ex3, text3 + "\n" + ex3.StackTrace);
			return ResponseGetPrompt.Error(text3).SetRequestID(requestId);
		}
	}

	private ResponseGetPrompt? ValidateRunParameters(string requestId, object? parameters = null)
	{
		if (string.IsNullOrWhiteSpace(requestId))
		{
			string text = "Request ID cannot be null or empty for prompt '" + (Name ?? Method?.Name) + "'";
			_logger?.LogError(text);
			return ResponseGetPrompt.Error(text);
		}
		if (Method == null)
		{
			string text2 = "Method information is not available for prompt '" + Name + "'";
			_logger?.LogError(text2);
			return ResponseGetPrompt.Error(text2).SetRequestID(requestId);
		}
		if (!Method.IsPublic && !Method.IsFamily)
		{
			string text3 = "Method '" + Method.Name + "' in prompt '" + Name + "' is not accessible (must be public or protected)";
			_logger?.LogError(text3);
			return ResponseGetPrompt.Error(text3).SetRequestID(requestId);
		}
		return null;
	}

	private Dictionary<string, object?>? ConvertNamedParameters(IReadOnlyDictionary<string, JsonElement>? namedParameters)
	{
		if (namedParameters == null)
		{
			return null;
		}
		try
		{
			return namedParameters.ToDictionary<KeyValuePair<string, JsonElement>, string, object>((KeyValuePair<string, JsonElement> kvp) => kvp.Key, (KeyValuePair<string, JsonElement> kvp) => kvp.Value);
		}
		catch (Exception ex)
		{
			throw new ArgumentException("Failed to convert named parameters: " + ex.Message, ex);
		}
	}

	protected override JsonNode? CreateInputSchema(Reflector reflector, MethodInfo methodInfo)
	{
		JsonNode jsonNode = base.CreateInputSchema(reflector, methodInfo);
		if (jsonNode == null)
		{
			return null;
		}
		ArgumentUtils.RemoveRequestIDParameters(jsonNode, methodInfo);
		return jsonNode;
	}
}
}
