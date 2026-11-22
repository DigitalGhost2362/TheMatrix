using System;
using System.ComponentModel;
using System.Linq;
using Feeder.Json;
using Feeder.Json.Nodes;
using Feeder.Json.Serialization;
using Feeder.Reflect.Utils;

namespace Feeder.Reflect.Model
{
[Serializable]
public class SerializedMember
{
	public const string ValueName = "value";

	[JsonInclude]
	[Description("Object name.")]
	public string? name = string.Empty;

	[JsonInclude]
	[Description("Full type name. Eg: 'System.String', 'System.Int32', 'UnityEngine.Vector3', etc.")]
	public string typeName = string.Empty;

	[JsonInclude]
	[Description("Fields of the object, serialized as a list of 'SerializedMember'.")]
	public SerializedMemberList? fields;

	[JsonInclude]
	[Description("Properties of the object, serialized as a list of 'SerializedMember'.")]
	public SerializedMemberList? props;

	[JsonInclude]
	[JsonPropertyName("value")]
	[Description("Value of the object, serialized as a non stringified JSON element. Can be null if the value is not set. Can be default value if the value is an empty object or array json.")]
	public JsonElement? valueJsonElement;

	public SerializedMember()
	{
	}

	protected SerializedMember(Type type, string? name = null)
	{
		this.name = name;
		typeName = type.GetTypeId() ?? throw new ArgumentNullException("type");
	}

	public SerializedMember SetName(string? name)
	{
		this.name = name;
		return this;
	}

	public SerializedMember? GetField(string name)
	{
		return fields?.FirstOrDefault((SerializedMember x) => x.name == name);
	}

	public SerializedMember SetFieldValue<T>(Reflector reflector, string name, T value)
	{
		SerializedMember field = GetField(name);
		if (field == null)
		{
			field = FromValue(reflector, typeof(T), value, name);
			if (fields == null)
			{
				fields = new SerializedMemberList();
			}
			fields.Add(field);
			return this;
		}
		field.SetValue(reflector, value);
		return this;
	}

	public SerializedMember AddField(SerializedMember field)
	{
		if (fields == null)
		{
			fields = new SerializedMemberList();
		}
		fields.Add(field);
		return this;
	}

	public SerializedMember? GetProperty(string name)
	{
		return props?.FirstOrDefault((SerializedMember x) => x.name == name);
	}

	public SerializedMember SetPropertyValue<T>(Reflector reflector, string name, T value)
	{
		SerializedMember property = GetProperty(name);
		if (property == null)
		{
			property = FromValue(reflector, typeof(T), value, name);
			if (props == null)
			{
				props = new SerializedMemberList();
			}
			props.Add(property);
			return this;
		}
		property.SetValue(reflector, value);
		return this;
	}

	public SerializedMember AddProperty(SerializedMember property)
	{
		if (props == null)
		{
			props = new SerializedMemberList();
		}
		props.Add(property);
		return this;
	}

	public bool IsNull()
	{
		if (valueJsonElement.HasValue)
		{
			return valueJsonElement.Value.ValueKind == JsonValueKind.Null;
		}
		return true;
	}

	public T? GetValue<T>(Reflector reflector)
	{
		return valueJsonElement.Deserialize<T>(reflector);
	}

	public SerializedMember SetValue(Reflector reflector, object? value)
	{
		string jsonValue = reflector.JsonSerializer.Serialize(value);
		return SetJsonValue(jsonValue);
	}

	public SerializedMember SetJsonValue(string? json)
	{
		if (StringUtils.IsNullOrEmpty(json))
		{
			valueJsonElement = null;
			return this;
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(json);
		valueJsonElement = jsonDocument.RootElement.Clone();
		return this;
	}

	public SerializedMember SetJsonValue(JsonElement jsonElement)
	{
		valueJsonElement = jsonElement;
		return this;
	}

	public static SerializedMember FromReference(string path, string? name)
	{
		JsonObject node = new JsonObject { ["$ref"] = path };
		return new SerializedMember
		{
			name = name,
			typeName = "Reference",
			valueJsonElement = node.ToJsonElement()
		};
	}

	public static SerializedMember Null(Type type, string? name = null)
	{
		return new SerializedMember(type, name);
	}

	public static SerializedMember FromJson(Type type, JsonElement json, string? name = null)
	{
		return new SerializedMember(type, name).SetJsonValue(json);
	}

	public static SerializedMember FromJson(Type type, string? json, string? name = null)
	{
		return new SerializedMember(type, name).SetJsonValue(json);
	}

	public static SerializedMember FromValue(Reflector reflector, Type type, object? value, string? name = null)
	{
		return new SerializedMember(type, name).SetValue(reflector, value);
	}

	public static SerializedMember FromValue<T>(Reflector reflector, T? value, string? name = null)
	{
		return new SerializedMember(typeof(T), name).SetValue(reflector, value);
	}
}
}
