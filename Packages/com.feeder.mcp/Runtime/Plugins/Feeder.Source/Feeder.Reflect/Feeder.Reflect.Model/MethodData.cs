using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Feeder.Json.Nodes;
using Feeder.Json.Serialization;

namespace Feeder.Reflect.Model
{
[Description("Method data. Used for providing detailed readonly information about a method in codebase of the project.")]
public class MethodData : MethodRef
{
	[JsonInclude]
	[JsonPropertyName("isPublic")]
	[Description("Indicates if the method is public.")]
	public bool IsPublic { get; set; }

	[JsonInclude]
	[JsonPropertyName("isStatic")]
	[Description("Indicates if the method is static.")]
	public bool IsStatic { get; set; }

	[JsonInclude]
	[JsonPropertyName("returnType")]
	[Description("Return type of the method. It may be null if the method has no return type.")]
	public string? ReturnType { get; set; }

	[JsonInclude]
	[JsonPropertyName("returnSchema")]
	[Description("JSON schema of the return type. It may be null if the method has no return type.")]
	public JsonNode? ReturnSchema { get; set; }

	[JsonInclude]
	[JsonPropertyName("inputParametersSchema")]
	[Description("JSON schema of the input parameters. It may be null if the method has no parameters or the parameters are unknown.")]
	public List<JsonNode>? InputParametersSchema { get; set; }

	public MethodData()
	{
	}

	public MethodData(Reflector reflector, MethodInfo methodInfo, bool justRef = false)
		: base(methodInfo)
	{
		IsStatic = methodInfo.IsStatic;
		IsPublic = methodInfo.IsPublic;
		ReturnType = methodInfo.ReturnType.GetTypeId();
		ReturnSchema = ((methodInfo.ReturnType == typeof(void)) ? null : (justRef ? reflector.GetSchemaRef(methodInfo.ReturnType) : reflector.GetSchema(methodInfo.ReturnType)));
		InputParametersSchema = methodInfo.GetParameters()?.Select((ParameterInfo parameter) => (!justRef) ? reflector.GetSchema(parameter.ParameterType) : reflector.GetSchemaRef(parameter.ParameterType))?.ToList();
	}
}
}
