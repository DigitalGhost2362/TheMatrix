using Feeder.Json;
using Feeder.Json.Nodes;

namespace Feeder.Reflect.Json
{
public class JsonArrayJsonConverter : JsonNodeJsonConverter<JsonArray>, IJsonSchemaConverter
{
	public static readonly JsonNode JsonAnySchema = new JsonObject { ["anyOf"] = new JsonArray
	{
		new JsonObject
		{
			["type"] = "object",
			["additionalProperties"] = true
		},
		new JsonObject
		{
			["type"] = "array",
			["items"] = new JsonObject()
		},
		new JsonObject { ["type"] = "string" },
		new JsonObject { ["type"] = "number" },
		new JsonObject { ["type"] = "boolean" },
		new JsonObject { ["type"] = "null" }
	} };

	public static JsonNode Schema => new JsonObject
	{
		["type"] = "array",
		["items"] = JsonAnySchema
	};

	public static JsonNode SchemaRef => new JsonObject { ["$ref"] = "#/$defs/" + JsonSchemaConverter<JsonArray>.StaticId };

	public override JsonNode GetSchema()
	{
		return Schema;
	}

	public override JsonNode GetSchemaRef()
	{
		return SchemaRef;
	}

	protected override JsonArray? CreateJsonNode(JsonElement element)
	{
		return JsonArray.Create(element);
	}
}
}
