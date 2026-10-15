using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Feeder.Json;
using Feeder.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Feeder.ToolHost.Common;
using Feeder.ToolHost.Common.Model;
using Feeder.ToolHost.Utils;
using Feeder.Reflect;
using Feeder.Reflect.Utils;
using Feeder.Logging;

namespace Feeder.ToolHost
{
public class RunTool : MethodWrapper, IRunTool, IEnabled
{
	private readonly Dictionary<string, string>? _paramNameLookup;

	private int? _cachedTokenCount;

	public string Name { get; private set; }

	public bool Enabled { get; set; } = true;

	public McpToolType ToolType { get; protected set; }

	public string? Title { get; protected set; }

	public bool? ReadOnlyHint { get; protected set; }

	public bool? DestructiveHint { get; protected set; }

	public bool? IdempotentHint { get; protected set; }

	public bool? OpenWorldHint { get; protected set; }

	public string? SkillDescription => Method?.GetCustomAttributes(typeof(AiSkillDescriptionAttribute), inherit: true).Cast<AiSkillDescriptionAttribute>().FirstOrDefault()?.Description;

	public string? SkillBody => Method?.GetCustomAttributes(typeof(AiSkillBodyAttribute), inherit: true).Cast<AiSkillBodyAttribute>().FirstOrDefault()?.Body;

	public MethodInfo Method => _methodInfo;

	protected string? RequestID { get; set; }

	public int TokenCount
	{
		get
		{
			if (_cachedTokenCount.HasValue)
			{
				return _cachedTokenCount.Value;
			}
			_cachedTokenCount = CalculateTokenCount();
			return _cachedTokenCount.Value;
		}
	}

	public RunTool(Reflector reflector, ILogger? logger, string name, MethodInfo methodInfo)
		: base(reflector, logger, methodInfo)
	{
		Name = name ?? throw new ArgumentNullException("name");
		_paramNameLookup = ParameterNameUtils.BuildParameterNameLookup(methodInfo?.GetParameters());
	}

	public RunTool(Reflector reflector, ILogger? logger, string name, object targetInstance, MethodInfo methodInfo)
		: base(reflector, logger, targetInstance, methodInfo)
	{
		Name = name ?? throw new ArgumentNullException("name");
		_paramNameLookup = ParameterNameUtils.BuildParameterNameLookup(methodInfo?.GetParameters());
	}

	public RunTool(Reflector reflector, ILogger? logger, string name, Type classType, MethodInfo methodInfo)
		: base(reflector, logger, classType, methodInfo)
	{
		Name = name ?? throw new ArgumentNullException("name");
		_paramNameLookup = ParameterNameUtils.BuildParameterNameLookup(methodInfo?.GetParameters());
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

	protected ResponseCallTool ProcessInvokeResult(string requestId, object? result)
	{
		if (result is ResponseCallTool responseCallTool)
		{
			return responseCallTool.SetRequestID(requestId);
		}
		if (result == null)
		{
			return ResponseCallTool.Success().SetRequestID(requestId);
		}
		return ResponseCallTool.SuccessStructured(Feeder.Json.JsonSerializer.SerializeToNode(result, _reflector.JsonSerializerOptions)).SetRequestID(requestId);
	}

	public async Task<ResponseCallTool> Run(string requestId, CancellationToken cancellationToken = default(CancellationToken), params object?[] parameters)
	{
		ResponseCallTool responseCallTool = ValidateRunParameters(requestId, parameters);
		if (responseCallTool != null)
		{
			return responseCallTool;
		}
		RequestID = requestId;
		try
		{
			return ProcessInvokeResult(requestId, await Invoke(cancellationToken, parameters));
		}
		catch (ArgumentException ex)
		{
			string message = "Parameter validation failed for tool '" + (Title ?? Method?.Name) + "': " + ex.Message;
			_logger?.LogError(ex, message);
			return ResponseCallTool.Error(message).SetRequestID(requestId);
		}
		catch (TargetParameterCountException exception)
		{
			string message2 = $"Parameter count mismatch for tool '{Title ?? Method?.Name}'. Expected {Method?.GetParameters().Length} parameters, but received {parameters?.Length}";
			_logger?.LogError(exception, message2);
			return ResponseCallTool.Error(message2).SetRequestID(requestId);
		}
		catch (Exception ex2)
		{
			if (CallerFault(ex2) is Exception caller)
				return CallerError(requestId, caller);
			string text = "Tool execution failed for '" + (Title ?? Method?.Name) + "': " + (ex2.InnerException ?? ex2).Message;
			_logger?.LogError(ex2, text + "\n" + ex2.StackTrace);
			return ResponseCallTool.Error(text).SetRequestID(requestId);
		}
	}

	public async Task<ResponseCallTool> Run(string requestId, IReadOnlyDictionary<string, JsonElement>? namedParameters, CancellationToken cancellationToken = default(CancellationToken))
	{
		ResponseCallTool responseCallTool = ValidateRunParameters(requestId, namedParameters);
		if (responseCallTool != null)
		{
			return responseCallTool;
		}
		RequestID = requestId;
		try
		{
			Dictionary<string, object> namedParameters2 = ConvertNamedParameters(namedParameters);
			return ProcessInvokeResult(requestId, await InvokeDict(namedParameters2, cancellationToken));
		}
		catch (ArgumentException ex)
		{
			string message = "Parameter validation failed for tool '" + (Title ?? Method?.Name) + "': " + ex.Message;
			_logger?.LogError(ex, message);
			return ResponseCallTool.Error(message).SetRequestID(requestId);
		}
		catch (JsonException ex2)
		{
			string message2 = "JSON parameter parsing failed for tool '" + (Title ?? Method?.Name) + "': " + ex2.Message;
			_logger?.LogError(ex2, message2);
			return ResponseCallTool.Error(message2).SetRequestID(requestId);
		}
		catch (Exception ex3)
		{
			if (CallerFault(ex3) is Exception caller)
				return CallerError(requestId, caller);
			string text = "Tool execution failed for '" + (Title ?? Method?.Name) + "': " + (ex3.InnerException ?? ex3).Message;
			_logger?.LogError(ex3, text + "\n" + ex3.StackTrace);
			return ResponseCallTool.Error(text).SetRequestID(requestId);
		}
	}

	// The caller's own mistake (ToolCallerException) or a domain reload (ThreadAbortException), however deeply wrapped.
	private static Exception? CallerFault(Exception exception)
	{
		for (Exception? current = exception; current != null; current = current.InnerException)
		{
			if (current is ToolCallerException || current is ThreadAbortException)
				return current;
		}
		return null;
	}

	// One console line, no stack: the caller still gets the whole message.
	private ResponseCallTool CallerError(string requestId, Exception fault)
	{
		string message = fault is ThreadAbortException
			? "It was cut off by a Unity domain reload before it finished. Its code may have run in part: check the editor state before running it again."
			: fault.Message;
		string firstLine = message.Split('\n')[0].Trim();
		UnityEngine.Debug.LogWarning("[Matrix tool] " + (Title ?? Name) + " (" + requestId + "): " + firstLine);
		return ResponseCallTool.Error("Tool execution failed for '" + (Title ?? Method?.Name) + "': " + message).SetRequestID(requestId);
	}

	private ResponseCallTool? ValidateRunParameters(string requestId, object? parameters = null)
	{
		if (string.IsNullOrWhiteSpace(requestId))
		{
			string message = "Request ID cannot be null or empty for tool '" + (Title ?? Method?.Name) + "'";
			_logger?.LogError(message);
			return ResponseCallTool.Error(message).SetRequestID(requestId);
		}
		if (Method == null)
		{
			string message2 = "Method information is not available for tool '" + Title + "'";
			_logger?.LogError(message2);
			return ResponseCallTool.Error(message2).SetRequestID(requestId);
		}
		if (!Method.IsPublic && !Method.IsFamily)
		{
			string message3 = "Method '" + Method.Name + "' in tool '" + Title + "' is not accessible (must be public or protected)";
			_logger?.LogError(message3);
			return ResponseCallTool.Error(message3).SetRequestID(requestId);
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
			Dictionary<string, object> dictionary = new Dictionary<string, object>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, JsonElement> namedParameter in namedParameters)
			{
				string text = ParameterNameUtils.NormalizeParameterName(namedParameter.Key, _paramNameLookup);
				if (dictionary.ContainsKey(text))
				{
					throw new ArgumentException("Duplicate parameter detected after case-insensitive normalization: '" + namedParameter.Key + "' normalizes to '" + text + "' which already exists. Please provide each parameter only once.");
				}
				dictionary[text] = namedParameter.Value;
			}
			return dictionary;
		}
		catch (ArgumentException)
		{
			throw;
		}
		catch (Exception ex2)
		{
			throw new ArgumentException("Failed to convert named parameters: " + ex2.Message, ex2);
		}
	}

	protected override JsonNode? CreateInputSchema(Reflector reflector, MethodInfo methodInfo)
	{
		JsonNode jsonNode = base.CreateInputSchema(reflector, methodInfo);
		if (jsonNode == null)
		{
			return Feeder.ToolHost.Common.Consts.MCP.EmptyInputSchemaNode;
		}
		if (!(jsonNode is JsonObject jsonObject))
		{
			throw new InvalidOperationException("Expected schema to be a JsonObject.");
		}
		if (!jsonObject.TryGetPropertyValue("type", out JsonNode jsonNode2) || jsonNode2?.GetValue<string>() != "object")
		{
			throw new InvalidOperationException("Expected schema type to be 'object'.");
		}
		if (jsonObject.Count == 1 && jsonObject.TryGetPropertyValue("type", out JsonNode _))
		{
			jsonObject["additionalProperties"] = false;
		}
		ArgumentUtils.RemoveRequestIDParameters(jsonNode, methodInfo);
		return jsonNode;
	}

	protected override JsonNode? CreateOutputSchema(Reflector reflector, MethodInfo methodInfo)
	{
		Type type = methodInfo.ReturnType;
		bool flag = MethodUtils.IsReturnTypeNullable(methodInfo);
		if (type == typeof(void) || type == typeof(Task) || type == typeof(ValueTask))
		{
			return null;
		}
		if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>)))
		{
			type = type.GetGenericArguments()[0];
		}
		Type underlyingType = Nullable.GetUnderlyingType(type);
		if (underlyingType != null)
		{
			type = underlyingType;
			flag = true;
		}
		if (type.IsGenericType)
		{
			if (type.GetGenericTypeDefinition() == typeof(ResponseCallValueTool<>))
			{
				Type item = type.GetGenericArguments()[0];
				(Type, string, string, bool)[] types = new(Type, string, string, bool)[1] { (item, "result", null, !flag) };
				return reflector.JsonSchema.GenerateSchema(reflector, types);
			}
			if (type == typeof(ResponseCallTool) || type.IsSubclassOf(typeof(ResponseCallTool)))
			{
				return null;
			}
		}
		else if (type == typeof(ResponseCallTool) || type.IsSubclassOf(typeof(ResponseCallTool)))
		{
			return null;
		}
		return base.CreateOutputSchema(reflector, methodInfo);
	}

	public static RunTool CreateFromStaticMethod(Reflector reflector, ILogger? logger, string name, MethodInfo methodInfo, string? title = null, bool? readOnlyHint = null, bool? destructiveHint = null, bool? idempotentHint = null, bool? openWorldHint = null, bool? enabled = null, McpToolType toolType = McpToolType.Standard)
	{
		return new RunTool(reflector, logger, name, methodInfo)
		{
			Title = title,
			ToolType = toolType,
			ReadOnlyHint = readOnlyHint,
			DestructiveHint = destructiveHint,
			IdempotentHint = idempotentHint,
			OpenWorldHint = openWorldHint,
			Enabled = (enabled ?? true)
		};
	}

	public static RunTool CreateFromInstanceMethod(Reflector reflector, ILogger? logger, string name, object targetInstance, MethodInfo methodInfo, string? title = null, bool? readOnlyHint = null, bool? destructiveHint = null, bool? idempotentHint = null, bool? openWorldHint = null, bool? enabled = null, McpToolType toolType = McpToolType.Standard)
	{
		return new RunTool(reflector, logger, name, targetInstance, methodInfo)
		{
			Title = title,
			ToolType = toolType,
			ReadOnlyHint = readOnlyHint,
			DestructiveHint = destructiveHint,
			IdempotentHint = idempotentHint,
			OpenWorldHint = openWorldHint,
			Enabled = (enabled ?? true)
		};
	}

	public static RunTool CreateFromClassMethod(Reflector reflector, ILogger? logger, string name, Type classType, MethodInfo methodInfo, string? title = null, bool? readOnlyHint = null, bool? destructiveHint = null, bool? idempotentHint = null, bool? openWorldHint = null, bool? enabled = null, McpToolType toolType = McpToolType.Standard)
	{
		return new RunTool(reflector, logger, name, classType, methodInfo)
		{
			Title = title,
			ToolType = toolType,
			ReadOnlyHint = readOnlyHint,
			DestructiveHint = destructiveHint,
			IdempotentHint = idempotentHint,
			OpenWorldHint = openWorldHint,
			Enabled = (enabled ?? true)
		};
	}

	private int CalculateTokenCount()
	{
		try
		{
			return ToolTokenCount.Calculate(Name, Title, base.Description, base.InputSchema, base.OutputSchema);
		}
		catch (Exception exception)
		{
			_logger?.LogWarning(exception, "Failed to calculate token count for tool '{0}'. Returning 0.", Name);
			return 0;
		}
	}
}
}
