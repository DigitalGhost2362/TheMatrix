using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Feeder.Json.Serialization;

namespace Feeder.Reflect.Model
{
[Description("Method reference. Used to find method in codebase of the project.")]
public class MethodRef
{
	[Description("Parameter of a method. Contains type and name of the parameter.")]
	public class Parameter
	{
		[JsonInclude]
		[JsonPropertyName("typeName")]
		[Description("Type of the parameter including namespace. Sample: 'System.String', 'System.Int32', 'UnityEngine.GameObject', etc.")]
		public string? TypeName { get; set; }

		[JsonInclude]
		[JsonPropertyName("name")]
		[Description("Name of the parameter. It may be empty if the name is unknown.")]
		public string? Name { get; set; }

		public Parameter()
		{
		}

		public Parameter(string typeName, string? name)
		{
			TypeName = typeName;
			Name = name;
		}

		public Parameter(ParameterInfo parameter)
		{
			TypeName = parameter.ParameterType.GetTypeId();
			Name = parameter.Name;
		}

		public override string ToString()
		{
			return TypeName + " " + Name;
		}
	}

	[JsonInclude]
	[JsonPropertyName("namespace")]
	[Description("Namespace of the class. It may be empty if the class is in the global namespace or the namespace is unknown.")]
	public string? Namespace { get; set; }

	[JsonInclude]
	[JsonPropertyName("typeName")]
	[Description("Class name, or substring a class name. It may be empty if the class is unknown.")]
	public string TypeName { get; set; } = string.Empty;

	[JsonInclude]
	[JsonPropertyName("methodName")]
	[Description("Method name, or substring of the method name. It may be empty if the method is unknown.")]
	public string MethodName { get; set; } = string.Empty;

	[JsonInclude]
	[JsonPropertyName("inputParameters")]
	[Description("List of input parameters. Can be null if the method has no parameters or the parameters are unknown.")]
	public List<Parameter>? InputParameters { get; set; }

	[JsonIgnore]
	public bool IsValid
	{
		get
		{
			if (string.IsNullOrEmpty(TypeName))
			{
				return false;
			}
			if (string.IsNullOrEmpty(MethodName))
			{
				return false;
			}
			if (InputParameters != null && InputParameters.Count > 0)
			{
				foreach (Parameter inputParameter in InputParameters)
				{
					if (inputParameter == null)
					{
						return false;
					}
					if (string.IsNullOrEmpty(inputParameter.TypeName))
					{
						return false;
					}
					if (string.IsNullOrEmpty(inputParameter.Name))
					{
						return false;
					}
				}
			}
			return true;
		}
	}

	public MethodRef()
	{
	}

	public MethodRef(MethodInfo methodInfo)
	{
		Namespace = methodInfo.DeclaringType?.Namespace;
		TypeName = methodInfo.DeclaringType?.GetTypeShortName() ?? string.Empty;
		MethodName = methodInfo.Name;
		InputParameters = methodInfo.GetParameters()?.Select((ParameterInfo parameter) => new Parameter(parameter))?.ToList();
	}

	public MethodRef(PropertyInfo methodInfo)
	{
		Namespace = methodInfo.DeclaringType?.Namespace;
		TypeName = methodInfo.DeclaringType?.GetTypeShortName() ?? string.Empty;
		MethodName = methodInfo.Name;
		InputParameters = null;
	}

	public override string ToString()
	{
		if (InputParameters != null)
		{
			if (!string.IsNullOrEmpty(Namespace))
			{
				return Namespace + "." + TypeName + "." + MethodName + "(" + string.Join(", ", InputParameters) + ")";
			}
			return TypeName + "." + MethodName + "(" + string.Join(", ", InputParameters) + ")";
		}
		if (!string.IsNullOrEmpty(Namespace))
		{
			return Namespace + "." + TypeName + "." + MethodName + "()";
		}
		return TypeName + "." + MethodName + "()";
	}
}
}
