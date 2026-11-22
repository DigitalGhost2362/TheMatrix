using Feeder.Json;
using Feeder.Json.Nodes;

namespace Feeder.Reflect.Json
{
public class JsonNodeConverter : JsonNodeJsonConverter<JsonNode>, IJsonSchemaConverter
{
	public static JsonNode Schema => JsonArrayJsonConverter.JsonAnySchema;

	public static JsonNode SchemaRef => new JsonObject { ["$ref"] = "#/$defs/" + JsonSchemaConverter<JsonNode>.StaticId };

	public override JsonNode GetSchema()
	{
		return Schema;
	}

	public override JsonNode GetSchemaRef()
	{
		return SchemaRef;
	}

	protected override JsonNode? CreateJsonNode(JsonElement element)
	{
		return element.ValueKind switch
		{
			JsonValueKind.Object => JsonObject.Create(element), 
			JsonValueKind.Array => JsonArray.Create(element), 
			_ => JsonValue.Create(element), 
		};
	}
}
}
