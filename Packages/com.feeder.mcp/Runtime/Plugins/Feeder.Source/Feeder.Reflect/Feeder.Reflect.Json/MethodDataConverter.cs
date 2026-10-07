using System;
using System.Collections.Generic;
using System.Linq;
using Feeder.Json;
using Feeder.Json.Nodes;
using Feeder.Reflect.Model;
using Feeder.Reflect.Utils;

namespace Feeder.Reflect.Json
{
public class MethodDataConverter : JsonSchemaConverter<MethodData>
{
	public static JsonNode Schema => new JsonObject
	{
		["type"] = "object",
		["properties"] = new JsonObject
		{
			["IsPublic"] = new JsonObject
			{
				["type"] = "boolean",
				["description"] = TypeUtils.GetDescription(typeof(MethodData).GetMember("IsPublic").First())
			},
			["IsStatic"] = new JsonObject
			{
				["type"] = "boolean",
				["description"] = TypeUtils.GetDescription(typeof(MethodData).GetMember("IsStatic").First())
			},
			["ReturnType"] = new JsonObject
			{
				["type"] = "string",
				["description"] = TypeUtils.GetDescription(typeof(MethodData).GetMember("ReturnType").First())
			},
			["ReturnSchema"] = new JsonObject
			{
				["type"] = "object",
				["description"] = TypeUtils.GetDescription(typeof(MethodData).GetMember("ReturnSchema").First())
			},
			["InputParametersSchema"] = new JsonObject
			{
				["type"] = "array",
				["items"] = new JsonObject
				{
					["type"] = "object",
					["additionalProperties"] = true
				},
				["description"] = TypeUtils.GetDescription(typeof(MethodData).GetMember("InputParametersSchema").First())
			},
			["Namespace"] = new JsonObject
			{
				["type"] = "string",
				["description"] = TypeUtils.GetDescription(typeof(MethodData).GetMember("Namespace").First())
			},
			["TypeName"] = new JsonObject
			{
				["type"] = "string",
				["description"] = TypeUtils.GetDescription(typeof(MethodData).GetMember("TypeName").First())
			},
			["MethodName"] = new JsonObject
			{
				["type"] = "string",
				["description"] = TypeUtils.GetDescription(typeof(MethodData).GetMember("MethodName").First())
			},
			["InputParameters"] = new JsonObject
			{
				["type"] = "array",
				["items"] = new JsonObject { ["$ref"] = "#/$defs/" + TypeUtils.GetSchemaTypeId<MethodRef.Parameter>() },
				["description"] = TypeUtils.GetDescription(typeof(MethodData).GetMember("InputParameters").First())
			}
		},
		["additionalProperties"] = false
	};

	public static JsonNode SchemaRef => new JsonObject { ["$ref"] = "#/$defs/" + JsonSchemaConverter<MethodData>.StaticId };

	public override JsonNode GetSchemaRef()
	{
		return SchemaRef;
	}

	public override JsonNode GetSchema()
	{
		return Schema;
	}

	public override IEnumerable<Type> GetDefinedTypes()
	{
		yield return typeof(MethodRef.Parameter);
	}

	public override MethodData? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return null;
		}
		if (reader.TokenType != JsonTokenType.StartObject)
		{
			throw new JsonException($"Expected start of object, but got {reader.TokenType}");
		}
		MethodData methodData = new MethodData();
		while (reader.Read())
		{
			if (reader.TokenType == JsonTokenType.EndObject)
			{
				return methodData;
			}
			if (reader.TokenType == JsonTokenType.PropertyName)
			{
				string text = reader.GetString();
				reader.Read();
				switch (text)
				{
				case "IsPublic":
					methodData.IsPublic = reader.GetBoolean();
					break;
				case "IsStatic":
					methodData.IsStatic = reader.GetBoolean();
					break;
				case "ReturnType":
					methodData.ReturnType = reader.GetString();
					break;
				case "ReturnSchema":
					methodData.ReturnSchema = ((reader.TokenType != JsonTokenType.Null) ? JsonNode.Parse(ref reader) : null);
					break;
				case "InputParametersSchema":
					methodData.InputParametersSchema = Feeder.Json.JsonSerializer.Deserialize<List<JsonNode>>(ref reader, options);
					break;
				case "Namespace":
					methodData.Namespace = reader.GetString();
					break;
				case "TypeName":
					methodData.TypeName = reader.GetString() ?? throw new JsonException("'TypeName' cannot be null.");
					break;
				case "MethodName":
					methodData.MethodName = reader.GetString() ?? throw new JsonException("'MethodName' cannot be null.");
					break;
				case "InputParameters":
					methodData.InputParameters = Feeder.Json.JsonSerializer.Deserialize<List<MethodRef.Parameter>>(ref reader, options);
					break;
				default:
					throw new JsonException("Unexpected property name: '" + text + "'. Did you want to use 'IsPublic', 'InputParameters', 'ReturnType', 'ReturnSchema', 'InputParametersSchema', 'Namespace, 'TypeName or 'InputParameters'?");
				}
			}
		}
		throw new JsonException("Unexpected end of JSON while reading MethodData.");
	}

	public override void Write(Utf8JsonWriter writer, MethodData? value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
			return;
		}
		writer.WriteStartObject();
		writer.WriteBoolean("IsPublic", value.IsPublic);
		writer.WriteBoolean("IsStatic", value.IsStatic);
		writer.WriteString("ReturnType", value.ReturnType);
		if (value.ReturnSchema != null)
		{
			writer.WritePropertyName("ReturnSchema");
			value.ReturnSchema.WriteTo(writer, options);
		}
		if (value.InputParametersSchema != null)
		{
			writer.WritePropertyName("InputParametersSchema");
			Feeder.Json.JsonSerializer.Serialize(writer, value.InputParametersSchema, options);
		}
		writer.WriteString("Namespace", value.Namespace);
		writer.WriteString("TypeName", value.TypeName);
		writer.WriteString("MethodName", value.MethodName);
		if (value.InputParameters != null)
		{
			writer.WritePropertyName("InputParameters");
			Feeder.Json.JsonSerializer.Serialize(writer, value.InputParameters, options);
		}
		writer.WriteEndObject();
	}
}
}
