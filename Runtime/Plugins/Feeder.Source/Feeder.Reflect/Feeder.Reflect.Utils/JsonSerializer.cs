using System;
using Feeder.Json;
using Feeder.Json.Nodes;
using Feeder.Json.Serialization;
using Feeder.Json.Serialization.Metadata;
using Feeder.Reflect.Json;

namespace Feeder.Reflect.Utils
{
public class JsonSerializer
{
	public const string Null = "null";

	public const string EmptyJsonObject = "{}";

	public const string EmptyJsonArray = "[]";

	private readonly JsonSerializerOptions jsonSerializerOptions;

	public JsonSerializerOptions JsonSerializerOptions => jsonSerializerOptions;

	public JsonSerializer(Reflector reflector)
	{
		if (reflector == null)
		{
			throw new ArgumentNullException("reflector");
		}
		jsonSerializerOptions = new JsonSerializerOptions
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			ReferenceHandler = ReferenceHandler.IgnoreCycles,
			NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
			PropertyNamingPolicy = null,
			PropertyNameCaseInsensitive = true,
			IncludeFields = true,
			WriteIndented = true,
			TypeInfoResolver = JsonTypeInfoResolver.Combine((IJsonTypeInfoResolver[])(object)new IJsonTypeInfoResolver[1] { (IJsonTypeInfoResolver)new DefaultJsonTypeInfoResolver
			{
				Modifiers = { (Action<JsonTypeInfo>)JsonTypeInfoModifiers.ExcludeObsoleteMembers }
			} }),
			Converters = 
			{
				(JsonConverter)new ByteJsonConverter(),
				(JsonConverter)new SByteJsonConverter(),
				(JsonConverter)new Int16JsonConverter(),
				(JsonConverter)new UInt16JsonConverter(),
				(JsonConverter)new Int32JsonConverter(),
				(JsonConverter)new UInt32JsonConverter(),
				(JsonConverter)new Int64JsonConverter(),
				(JsonConverter)new UInt64JsonConverter(),
				(JsonConverter)new SingleJsonConverter(),
				(JsonConverter)new DoubleJsonConverter(),
				(JsonConverter)new BoolJsonConverter(),
				(JsonConverter)new CharJsonConverter(),
				(JsonConverter)new EnumJsonConverter(),
				(JsonConverter)new IntPtrJsonConverter(),
				(JsonConverter)new UIntPtrJsonConverter(),
				(JsonConverter)new DateTimeJsonConverter(),
				(JsonConverter)new DateTimeOffsetJsonConverter(),
				(JsonConverter)new DecimalJsonConverter(),
				(JsonConverter)new GuidJsonConverter(),
				(JsonConverter)new TimeSpanJsonConverter(),
				(JsonConverter)new TypeJsonConverter(),
				(JsonConverter)new UriJsonConverter(),
				(JsonConverter)new VersionJsonConverter(),
				(JsonConverter)new BigIntegerJsonConverter(),
				(JsonConverter)new ComplexJsonConverter(),
				(JsonConverter)new IPAddressJsonConverter(),
				(JsonConverter)new IPEndPointJsonConverter(),
				(JsonConverter)new JsonElementJsonConverter(),
				(JsonConverter)new JsonNodeConverter(),
				(JsonConverter)new JsonObjectJsonConverter(),
				(JsonConverter)new JsonArrayJsonConverter(),
				(JsonConverter)new JsonValueJsonConverter(),
				(JsonConverter)new AssemblyJsonConverter(),
				(JsonConverter)new ConstructorInfoConverter(),
				(JsonConverter)new FieldInfoConverter(),
				(JsonConverter)new MethodInfoConverter(),
				(JsonConverter)new PropertyInfoConverter(),
				(JsonConverter)new ParameterInfoConverter(),
				(JsonConverter)new ExceptionJsonConverter(),
				(JsonConverter)new MethodDataConverter(),
				(JsonConverter)new SerializedMemberConverter(reflector),
				(JsonConverter)new SerializedMemberListConverter(reflector)
			}
		};
	}

	public void AddConverter(JsonConverter converter)
	{
		if (converter == null)
		{
			throw new ArgumentNullException("converter");
		}
		jsonSerializerOptions.Converters.Add(converter);
	}

	public JsonConverter? GetJsonConverter(Type type)
	{
		foreach (JsonConverter converter in jsonSerializerOptions.Converters)
		{
			if (converter.CanConvert(type))
			{
				return converter;
			}
		}
		return null;
	}

	public void ClearConverters()
	{
		jsonSerializerOptions.Converters.Clear();
	}

	public string Serialize(object? data, JsonSerializerOptions? options = null)
	{
		return Feeder.Json.JsonSerializer.Serialize(data, options ?? jsonSerializerOptions);
	}

	public string Serialize(object? value, Type inputType, JsonSerializerOptions? options = null)
	{
		return Feeder.Json.JsonSerializer.Serialize(value, inputType, options ?? jsonSerializerOptions);
	}

	public JsonDocument SerializeToDocument(object? value, JsonSerializerOptions? options = null)
	{
		return Feeder.Json.JsonSerializer.SerializeToDocument(value, options ?? jsonSerializerOptions);
	}

	public JsonDocument SerializeToDocument(object? value, Type inputType, JsonSerializerOptions? options = null)
	{
		return Feeder.Json.JsonSerializer.SerializeToDocument(value, inputType, options ?? jsonSerializerOptions);
	}

	public JsonElement SerializeToElement(object data, JsonSerializerOptions? options = null)
	{
		return Feeder.Json.JsonSerializer.SerializeToElement(data, options ?? jsonSerializerOptions);
	}

	public JsonNode? SerializeToNode(object? value, JsonSerializerOptions? options = null)
	{
		return Feeder.Json.JsonSerializer.SerializeToNode(value, options ?? jsonSerializerOptions);
	}

	public JsonNode? SerializeToNode(object? value, Type inputType, JsonSerializerOptions? options = null)
	{
		return Feeder.Json.JsonSerializer.SerializeToNode(value, inputType, options ?? jsonSerializerOptions);
	}

	public byte[] SerializeToUtf8Bytes(object? value, JsonSerializerOptions? options = null)
	{
		return Feeder.Json.JsonSerializer.SerializeToUtf8Bytes(value, options ?? jsonSerializerOptions);
	}

	public byte[] SerializeToUtf8Bytes(object? value, Type inputType, JsonSerializerOptions? options = null)
	{
		return Feeder.Json.JsonSerializer.SerializeToUtf8Bytes(value, inputType, options ?? jsonSerializerOptions);
	}

	public object? Deserialize(JsonDocument document, Type returnType, JsonSerializerOptions? options = null)
	{
		return document.Deserialize(returnType, options ?? jsonSerializerOptions);
	}

	public object? Deserialize(JsonElement element, Type returnType, JsonSerializerOptions? options = null)
	{
		return element.Deserialize(returnType, options ?? jsonSerializerOptions);
	}

	public object? Deserialize(JsonNode? node, Type returnType, JsonSerializerOptions? options = null)
	{
		return node.Deserialize(returnType, options ?? jsonSerializerOptions);
	}

	public object? Deserialize(ReadOnlySpan<byte> utf8Json, Type returnType, JsonSerializerOptions? options = null)
	{
		return Feeder.Json.JsonSerializer.Deserialize(utf8Json, returnType, options ?? jsonSerializerOptions);
	}

	public object? Deserialize(Reflector reflector, JsonElement? jsonElement, Type type, JsonSerializerOptions? options = null)
	{
		if (!jsonElement.HasValue)
		{
			return reflector.GetDefaultValue(type);
		}
		return jsonElement.Value.Deserialize(type, options ?? jsonSerializerOptions);
	}

	public object? Deserialize(ref Utf8JsonReader reader, Type returnType, JsonSerializerOptions? options = null)
	{
		return Feeder.Json.JsonSerializer.Deserialize(ref reader, returnType, options ?? jsonSerializerOptions);
	}

	public object? Deserialize(string json, Type returnType, JsonSerializerOptions? options = null)
	{
		return Feeder.Json.JsonSerializer.Deserialize(json, returnType, options ?? jsonSerializerOptions);
	}

	public T? Deserialize<T>(Reflector reflector, JsonElement? jsonElement, JsonSerializerOptions? options = null)
	{
		if (!jsonElement.HasValue)
		{
			return reflector.GetDefaultValue<T>();
		}
		return jsonElement.Value.Deserialize<T>(options ?? jsonSerializerOptions);
	}

	public T? Deserialize<T>(string json, JsonSerializerOptions? options = null)
	{
		return Feeder.Json.JsonSerializer.Deserialize<T>(json, options ?? jsonSerializerOptions);
	}

	public TValue? Deserialize<TValue>(JsonDocument document, JsonSerializerOptions? options = null)
	{
		return document.Deserialize<TValue>(options ?? jsonSerializerOptions);
	}

	public TValue? Deserialize<TValue>(JsonElement element, JsonSerializerOptions? options = null)
	{
		return element.Deserialize<TValue>(options ?? jsonSerializerOptions);
	}

	public TValue? Deserialize<TValue>(JsonNode? node, JsonSerializerOptions? options = null)
	{
		return node.Deserialize<TValue>(options ?? jsonSerializerOptions);
	}

	public TValue? Deserialize<TValue>(ReadOnlySpan<byte> utf8Json, JsonSerializerOptions? options = null)
	{
		return Feeder.Json.JsonSerializer.Deserialize<TValue>(utf8Json, options ?? jsonSerializerOptions);
	}

	public TValue? Deserialize<TValue>(ref Utf8JsonReader reader, JsonSerializerOptions? options = null)
	{
		return Feeder.Json.JsonSerializer.Deserialize<TValue>(ref reader, options ?? jsonSerializerOptions);
	}
}
}
