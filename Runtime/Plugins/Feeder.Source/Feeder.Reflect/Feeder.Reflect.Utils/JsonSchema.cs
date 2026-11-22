using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Feeder.Json.Nodes;
using Feeder.Json.Serialization;
using System.Threading.Tasks;
using Feeder.Reflect.Json;

namespace Feeder.Reflect.Utils
{
public class JsonSchema
{
	public const string Type = "type";

	public const string Object = "object";

	public const string Description = "description";

	public const string Properties = "properties";

	public const string Pattern = "pattern";

	public const string Items = "items";

	public const string Array = "array";

	public const string AnyOf = "anyOf";

	public const string Required = "required";

	public const string Result = "result";

	public const string Error = "error";

	public const string AdditionalProperties = "additionalProperties";

	public const string Null = "null";

	public const string String = "string";

	public const string Integer = "integer";

	public const string Number = "number";

	public const string Boolean = "boolean";

	public const string Enum = "enum";

	public const string Minimum = "minimum";

	public const string Maximum = "maximum";

	public const string Id = "$id";

	public const string Defs = "$defs";

	public const string Ref = "$ref";

	public const string RefValue = "#/$defs/";

	public const string SchemaDraft = "$schema";

	public const string SchemaDraftValue = "https://json-schema.org/draft/2020-12/schema";

	public const string Reference = "Reference";

	public JsonNode GetSchema<T>(Reflector reflector, JsonObject? defines = null)
	{
		return GetSchema(reflector, typeof(T), defines);
	}

	public JsonNode GetSchemaRef<T>(Reflector reflector)
	{
		return GetSchemaRef(reflector, typeof(T));
	}

	public JsonNode GetSchema(Reflector reflector, Type type, JsonObject? defines = null)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		JsonNode jsonNode = null;
		string schemaTypeId = type.GetSchemaTypeId();
		try
		{
			bool flag = defines == null;
			if (defines == null)
			{
				defines = new JsonObject();
			}
			bool flag2 = defines.ContainsKey(schemaTypeId);
			if (reflector.JsonSerializerOptions.GetConverter(type) is IJsonSchemaConverter jsonSchemaConverter)
			{
				if (flag && !flag2)
				{
					defines[schemaTypeId] = new JsonObject { ["type"] = "object" };
				}
				jsonNode = jsonSchemaConverter.GetSchema();
				foreach (Type definedType in jsonSchemaConverter.GetDefinedTypes())
				{
					string schemaTypeId2 = definedType.GetSchemaTypeId();
					if (!defines.ContainsKey(schemaTypeId2))
					{
						defines[schemaTypeId2] = new JsonObject { ["type"] = "object" };
						JsonNode schema = GetSchema(reflector, definedType, defines);
						if (schema != null)
						{
							defines[schemaTypeId2] = schema;
						}
					}
				}
			}
			else if (TypeUtils.IsDictionary(type))
			{
				Type[] dictionaryGenericArguments = TypeUtils.GetDictionaryGenericArguments(type);
				if (dictionaryGenericArguments == null)
				{
					throw new InvalidOperationException("Unable to get generic arguments for dictionary type '" + type.GetTypeId() + "'.");
				}
				Type[] array = dictionaryGenericArguments;
				foreach (Type type2 in array)
				{
					if (TypeUtils.IsPrimitive(type2))
					{
						continue;
					}
					string schemaTypeId3 = type2.GetSchemaTypeId();
					if (!defines.ContainsKey(schemaTypeId3))
					{
						JsonNode schema2 = GetSchema(reflector, type2, defines);
						if (schema2 != null)
						{
							defines[schemaTypeId3] = schema2;
						}
					}
				}
				if (flag && !flag2)
				{
					defines[schemaTypeId] = new JsonObject { ["type"] = "object" };
				}
				if (dictionaryGenericArguments.Length >= 2)
				{
					JsonNode schema3 = GetSchema(reflector, dictionaryGenericArguments[1], defines);
					jsonNode = new JsonObject
					{
						["type"] = "object",
						["additionalProperties"] = schema3
					};
				}
				else
				{
					jsonNode = new JsonObject
					{
						["type"] = "object",
						["additionalProperties"] = true
					};
				}
			}
			else
			{
				if (flag && !flag2)
				{
					defines[schemaTypeId] = new JsonObject { ["type"] = "object" };
				}
				jsonNode = GenerateSchemaFromType(reflector, type, defines);
			}
			string description = TypeUtils.GetDescription(type);
			if (!string.IsNullOrEmpty(description))
			{
				jsonNode["description"] = JsonValue.Create(description);
			}
			if (flag)
			{
				if (!flag2)
				{
					defines[schemaTypeId] = jsonNode.DeepClone();
				}
				if (defines.Count > 0)
				{
					jsonNode["$defs"] = defines;
				}
			}
		}
		catch (Exception ex)
		{
			JsonObject jsonObject = new JsonObject();
			jsonObject["error"] = "Failed to get schema for '" + type.GetTypeId() + "':\n" + ex.Message + "\n" + ex.StackTrace + "\n";
			return jsonObject;
		}
		if (jsonNode == null)
		{
			throw new InvalidOperationException("Failed to get schema for type '" + type.GetTypeId() + "'.");
		}
		PostprocessFields(jsonNode);
		if (!(jsonNode is JsonObject))
		{
			return new JsonObject { ["error"] = "Unexpected schema type for '" + type.GetTypeId() + "'. Json Schema type: " + jsonNode.GetType().GetTypeId() };
		}
		return jsonNode;
	}

	public JsonNode GetSchemaRef(Reflector reflector, Type type)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		JsonNode jsonNode = null;
		try
		{
			if (reflector.JsonSerializerOptions.GetConverter(type) is IJsonSchemaConverter jsonSchemaConverter)
			{
				jsonNode = jsonSchemaConverter.GetSchemaRef();
			}
			else
			{
				string schemaTypeId = type.GetSchemaTypeId();
				jsonNode = new JsonObject { ["$ref"] = "#/$defs/" + schemaTypeId };
			}
			string description = TypeUtils.GetDescription(type);
			if (!string.IsNullOrEmpty(description))
			{
				jsonNode["description"] = JsonValue.Create(description);
			}
		}
		catch (Exception ex)
		{
			JsonObject jsonObject = new JsonObject();
			jsonObject["error"] = "Failed to get schema for '" + type.GetTypeId() + "':\n" + ex.Message + "\n" + ex.StackTrace + "\n";
			return jsonObject;
		}
		if (jsonNode == null)
		{
			throw new InvalidOperationException("Failed to get schema for type '" + type.GetTypeId() + "'.");
		}
		PostprocessFields(jsonNode);
		if (!(jsonNode is JsonObject))
		{
			return new JsonObject { ["error"] = "Unexpected schema type for '" + type.GetTypeId() + "'. Json Schema type: " + jsonNode.GetType().GetTypeId() };
		}
		return jsonNode;
	}

	private void CollectNestedTypes(Reflector reflector, Type type, HashSet<Type>? visitedTypes = null)
	{
		if (visitedTypes == null)
		{
			visitedTypes = new HashSet<Type>();
		}
		if (visitedTypes.Contains(type))
		{
			return;
		}
		visitedTypes.Add(type);
		if (TypeUtils.IsPrimitive(type))
		{
			return;
		}
		foreach (Type genericType in TypeUtils.GetGenericTypes(type))
		{
			CollectNestedTypes(reflector, genericType, visitedTypes);
		}
		if (TypeUtils.IsIEnumerable(type))
		{
			Type enumerableItemType = TypeUtils.GetEnumerableItemType(type);
			if (enumerableItemType != null)
			{
				CollectNestedTypes(reflector, enumerableItemType, visitedTypes);
			}
		}
		IEnumerable<PropertyInfo> serializableProperties = reflector.GetSerializableProperties(type);
		if (serializableProperties != null)
		{
			foreach (PropertyInfo item in serializableProperties)
			{
				CollectNestedTypes(reflector, item.PropertyType, visitedTypes);
			}
		}
		IEnumerable<FieldInfo> serializableFields = reflector.GetSerializableFields(type);
		if (serializableFields == null)
		{
			return;
		}
		foreach (FieldInfo item2 in serializableFields)
		{
			CollectNestedTypes(reflector, item2.FieldType, visitedTypes);
		}
	}

	public JsonNode GetArgumentsSchema(Reflector reflector, MethodInfo method, bool justRef = false, JsonObject? defines = null)
	{
		if (method == null)
		{
			throw new ArgumentNullException("method");
		}
		ParameterInfo[] parameters = method.GetParameters();
		if (parameters.Length == 0)
		{
			return new JsonObject { ["type"] = "object" };
		}
		IEnumerable<(Type, string, string, bool, ICustomAttributeProvider)> types = parameters.Select((ParameterInfo p) => ((Type type, string name, string description, bool required, ICustomAttributeProvider attributeProvider))(type: p.ParameterType, name: p.Name ?? throw new InvalidOperationException("Parameter in method '" + method.Name + "' has no name."), description: TypeUtils.GetDescription(p), required: !p.HasDefaultValue, attributeProvider: p));
		return GenerateSchema(reflector, types, justRef, defines);
	}

	public JsonNode? GetReturnSchema(Reflector reflector, MethodInfo methodInfo, bool justRef = false, JsonObject? defines = null)
	{
		if (methodInfo == null)
		{
			throw new ArgumentNullException("methodInfo");
		}
		Type returnType = methodInfo.ReturnType;
		if (returnType == typeof(void) || returnType == typeof(Task) || returnType == typeof(ValueTask))
		{
			return null;
		}
		bool num = returnType.IsGenericType && (returnType.GetGenericTypeDefinition() == typeof(Task<>) || returnType.GetGenericTypeDefinition() == typeof(ValueTask<>));
		bool flag = MethodUtils.IsReturnTypeNullable(methodInfo);
		Type type = (num ? returnType.GetGenericArguments()[0] : returnType);
		Type underlyingType = Nullable.GetUnderlyingType(type);
		if (underlyingType != null)
		{
			type = underlyingType;
		}
		(Type, string, string, bool)[] types = new(Type, string, string, bool)[1] { (type, "result", null, !flag) };
		return GenerateSchema(reflector, types, justRef, defines);
	}

	public JsonNode GenerateSchema(Reflector reflector, IEnumerable<(Type type, string name, string? description, bool required)> types, bool justRef = false, JsonObject? defines = null)
	{
		return GenerateSchema(reflector, types.Select<(Type, string, string, bool), (Type, string, string, bool, ICustomAttributeProvider)>(((Type type, string name, string description, bool required) t) => ((Type type, string name, string description, bool required, ICustomAttributeProvider attributeProvider))(type: t.type, name: t.name, description: t.description, required: t.required, attributeProvider: null)), justRef, defines);
	}

	public JsonNode GenerateSchema(Reflector reflector, IEnumerable<(Type type, string name, string? description, bool required, ICustomAttributeProvider? attributeProvider)> types, bool justRef = false, JsonObject? defines = null)
	{
		bool flag = defines == null;
		if (defines == null)
		{
			defines = new JsonObject();
		}
		JsonObject jsonObject = new JsonObject();
		JsonArray jsonArray = new JsonArray();
		JsonObject jsonObject2 = new JsonObject { ["type"] = "object" };
		foreach (var type2 in types)
		{
			JsonNode jsonNode = null;
			if (TypeUtils.IsPrimitive(type2.type))
			{
				Type type = Nullable.GetUnderlyingType(type2.type);
				if ((object)type == null)
				{
					(type, _, _, _, _) = type2;
				}
				jsonNode = ((!(type == typeof(string)) || type2.attributeProvider == null || type2.attributeProvider.GetCustomAttributes(typeof(JsonStringOrObjectAttribute), inherit: true).Length == 0) ? GetSchema(reflector, type2.type, defines) : JsonStringOrObjectAttribute.Schema);
			}
			else
			{
				jsonNode = GetSchemaRef(reflector, type2.type);
				string schemaTypeId = type2.type.GetSchemaTypeId();
				if (!defines.ContainsKey(schemaTypeId))
				{
					JsonNode schema = GetSchema(reflector, type2.type, defines);
					if (schema == null)
					{
						continue;
					}
					defines[schemaTypeId] = schema;
				}
			}
			if (jsonNode == null)
			{
				continue;
			}
			jsonObject[type2.name] = jsonNode;
			if (jsonNode is JsonObject jsonObject3)
			{
				string item = type2.description;
				if (!string.IsNullOrEmpty(item))
				{
					jsonObject3["description"] = JsonValue.Create(item);
				}
			}
			if (type2.required)
			{
				jsonArray.Add(type2.name);
			}
			foreach (Type genericType in TypeUtils.GetGenericTypes(type2.type))
			{
				if (TypeUtils.IsPrimitive(genericType))
				{
					continue;
				}
				string schemaTypeId2 = genericType.GetSchemaTypeId();
				if (!defines.ContainsKey(schemaTypeId2))
				{
					JsonNode schema2 = GetSchema(reflector, genericType, defines);
					if (schema2 != null)
					{
						defines[schemaTypeId2] = schema2;
					}
				}
			}
		}
		if (jsonObject.Count > 0)
		{
			jsonObject2["properties"] = jsonObject;
		}
		if ((defines.Count > 0) & flag)
		{
			jsonObject2["$defs"] = defines;
		}
		if (jsonArray.Count > 0)
		{
			jsonObject2["required"] = jsonArray;
		}
		return jsonObject2;
	}

	public static List<JsonNode?> FindAllProperties(JsonNode node, string fieldName)
	{
		List<JsonNode> list = new List<JsonNode>();
		if (node is JsonObject jsonObject)
		{
			foreach (KeyValuePair<string, JsonNode> item in jsonObject)
			{
				if (item.Value == null)
				{
					if (item.Key == fieldName)
					{
						list.Add(null);
					}
					continue;
				}
				if (item.Key == fieldName)
				{
					list.Add(item.Value);
				}
				list.AddRange(FindAllProperties(item.Value, fieldName));
			}
		}
		else if (node is JsonArray jsonArray)
		{
			foreach (JsonNode item2 in jsonArray)
			{
				if (item2 != null)
				{
					list.AddRange(FindAllProperties(item2, fieldName));
				}
			}
		}
		return list;
	}

	private void PostprocessFields(JsonNode? node)
	{
		if (node == null)
		{
			return;
		}
		if (node is JsonObject jsonObject)
		{
			if (jsonObject.TryGetPropertyValue("type", out JsonNode jsonNode))
			{
				if (jsonNode is JsonValue)
				{
					if (jsonNode.ToString() == "array" && jsonObject.TryGetPropertyValue("items", out JsonNode jsonNode2))
					{
						PostprocessFields(jsonNode2);
					}
				}
				else if (jsonNode is JsonArray source)
				{
					string text = source.FirstOrDefault((JsonNode x) => x is JsonValue jsonValue && jsonValue.ToString() != "null")?.ToString();
					if (text != null)
					{
						jsonObject["type"] = JsonValue.Create(text.ToString());
					}
				}
			}
			foreach (KeyValuePair<string, JsonNode> item in jsonObject)
			{
				if (!(item.Key == "type") && item.Value != null)
				{
					PostprocessFields(item.Value);
				}
			}
		}
		if (!(node is JsonArray jsonArray))
		{
			return;
		}
		foreach (JsonNode item2 in jsonArray)
		{
			if (item2 != null)
			{
				PostprocessFields(item2);
			}
		}
	}

	private JsonNode GenerateSchemaFromType(Reflector reflector, Type type, JsonObject defines)
	{
		if (TypeUtils.IsPrimitive(type))
		{
			return GeneratePrimitiveSchema(type);
		}
		if (TypeUtils.IsIEnumerable(type))
		{
			if (type.IsArray && type.GetArrayRank() > 1)
			{
				int arrayRank = type.GetArrayRank();
				Type elementType = type.GetElementType();
				if (elementType != null)
				{
					Type type2 = ((arrayRank == 2) ? elementType.MakeArrayType() : elementType.MakeArrayType(arrayRank - 1));
					return new JsonObject
					{
						["type"] = "array",
						["items"] = GetSchema(reflector, type2, defines)
					};
				}
			}
			Type enumerableItemType = TypeUtils.GetEnumerableItemType(type);
			if (enumerableItemType != null)
			{
				string schemaTypeId = enumerableItemType.GetSchemaTypeId();
				bool flag = TypeUtils.IsPrimitive(enumerableItemType);
				if (!flag && !defines.ContainsKey(schemaTypeId))
				{
					defines[schemaTypeId] = new JsonObject { ["type"] = "object" };
					defines[schemaTypeId] = GetSchema(reflector, enumerableItemType, defines);
				}
				string schemaTypeId2 = type.GetSchemaTypeId();
				if (!defines.ContainsKey(schemaTypeId2))
				{
					defines[schemaTypeId2] = new JsonObject { ["type"] = "array" };
					defines[schemaTypeId2] = new JsonObject
					{
						["type"] = "array",
						["items"] = (flag ? GetSchema(reflector, enumerableItemType, defines) : GetSchemaRef(reflector, enumerableItemType))
					};
				}
				return new JsonObject
				{
					["type"] = "array",
					["items"] = (flag ? GetSchema(reflector, enumerableItemType, defines) : GetSchemaRef(reflector, enumerableItemType))
				};
			}
		}
		JsonObject jsonObject = new JsonObject();
		JsonArray jsonArray = new JsonArray();
		JsonObject jsonObject2 = new JsonObject { ["type"] = "object" };
		IEnumerable<FieldInfo> serializableFields = reflector.GetSerializableFields(type);
		if (serializableFields != null)
		{
			foreach (FieldInfo item in serializableFields)
			{
				if (item.GetCustomAttribute<JsonIgnoreAttribute>() != null)
				{
					continue;
				}
				Type? underlyingType = Nullable.GetUnderlyingType(item.FieldType);
				bool num = TypeUtils.IsPrimitive(underlyingType ?? item.FieldType);
				string text = item.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? item.Name;
				JsonNode jsonNode = (num ? GetSchema(reflector, item.FieldType, defines) : GetSchemaRef(reflector, item.FieldType));
				if (!num)
				{
					string schemaTypeId3 = item.FieldType.GetSchemaTypeId();
					if (!defines.ContainsKey(schemaTypeId3))
					{
						defines[schemaTypeId3] = new JsonObject { ["type"] = "object" };
						defines[schemaTypeId3] = GetSchema(reflector, item.FieldType, defines);
					}
				}
				string fieldDescription = TypeUtils.GetFieldDescription(item);
				if (!string.IsNullOrEmpty(fieldDescription) && jsonNode is JsonObject jsonObject3)
				{
					jsonObject3["description"] = JsonValue.Create(fieldDescription);
				}
				jsonObject[text] = jsonNode;
				if (underlyingType == null && !item.FieldType.IsClass)
				{
					jsonArray.Add(text);
				}
			}
		}
		IEnumerable<PropertyInfo> serializableProperties = reflector.GetSerializableProperties(type);
		if (serializableProperties != null)
		{
			foreach (PropertyInfo item2 in serializableProperties)
			{
				if (item2.GetCustomAttribute<JsonIgnoreAttribute>() != null)
				{
					continue;
				}
				Type? underlyingType2 = Nullable.GetUnderlyingType(item2.PropertyType);
				bool num2 = TypeUtils.IsPrimitive(underlyingType2 ?? item2.PropertyType);
				string text2 = item2.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? item2.Name;
				JsonNode jsonNode2 = (num2 ? GetSchema(reflector, item2.PropertyType, defines) : GetSchemaRef(reflector, item2.PropertyType));
				if (!num2)
				{
					string schemaTypeId4 = item2.PropertyType.GetSchemaTypeId();
					if (!defines.ContainsKey(schemaTypeId4))
					{
						defines[schemaTypeId4] = new JsonObject { ["type"] = "object" };
						defines[schemaTypeId4] = GetSchema(reflector, item2.PropertyType, defines);
					}
				}
				string propertyDescription = TypeUtils.GetPropertyDescription(item2);
				if (!string.IsNullOrEmpty(propertyDescription) && jsonNode2 is JsonObject jsonObject4)
				{
					jsonObject4["description"] = JsonValue.Create(propertyDescription);
				}
				jsonObject[text2] = jsonNode2;
				if (underlyingType2 == null && !item2.PropertyType.IsClass && item2.CanWrite)
				{
					jsonArray.Add(text2);
				}
			}
		}
		if (jsonObject.Count > 0)
		{
			jsonObject2["properties"] = jsonObject;
		}
		if (jsonArray.Count > 0)
		{
			jsonObject2["required"] = jsonArray;
		}
		return jsonObject2;
	}

	private JsonNode GeneratePrimitiveSchema(Type type)
	{
		Type type2 = Nullable.GetUnderlyingType(type) ?? type;
		if (type2 == typeof(string))
		{
			return new JsonObject { ["type"] = "string" };
		}
		if (type2 == typeof(int) || type2 == typeof(long) || type2 == typeof(short) || type2 == typeof(byte) || type2 == typeof(sbyte) || type2 == typeof(ushort) || type2 == typeof(uint) || type2 == typeof(ulong))
		{
			return new JsonObject { ["type"] = "integer" };
		}
		if (type2 == typeof(float) || type2 == typeof(double) || type2 == typeof(decimal))
		{
			return new JsonObject { ["type"] = "number" };
		}
		if (type2 == typeof(bool))
		{
			return new JsonObject { ["type"] = "boolean" };
		}
		if (type2 == typeof(DateTime) || type2 == typeof(DateTimeOffset))
		{
			return new JsonObject
			{
				["type"] = "string",
				["format"] = "date-time"
			};
		}
		if (type2 == typeof(Guid))
		{
			return new JsonObject
			{
				["type"] = "string",
				["format"] = "uuid"
			};
		}
		if (type2.IsEnum)
		{
			JsonArray jsonArray = new JsonArray();
			foreach (object value in System.Enum.GetValues(type2))
			{
				jsonArray.Add(JsonValue.Create(value.ToString()));
			}
			return new JsonObject
			{
				["type"] = "string",
				["enum"] = jsonArray
			};
		}
		return new JsonObject { ["type"] = "string" };
	}
}
}
