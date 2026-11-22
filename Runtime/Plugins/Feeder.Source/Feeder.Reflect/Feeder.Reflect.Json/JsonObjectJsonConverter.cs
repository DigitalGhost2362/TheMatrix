using Feeder.Json;
using Feeder.Json.Nodes;

namespace Feeder.Reflect.Json
{
public class JsonObjectJsonConverter : JsonNodeJsonConverter<JsonObject>, IJsonSchemaConverter
{
	public static JsonNode Schema => new JsonObject
	{
		["type"] = "object",
		["additionalProperties"] = true
	};

	public static JsonNode SchemaRef => new JsonObject { ["$ref"] = "#/$defs/" + JsonSchemaConverter<JsonObject>.StaticId };

	public override JsonNode GetSchema()
	{
		return Schema;
	}

	public override JsonNode GetSchemaRef()
	{
		return SchemaRef;
	}

	protected override JsonObject? CreateJsonNode(JsonElement element)
	{
		return JsonObject.Create(element);
	}
}
}
