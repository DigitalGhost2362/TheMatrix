#nullable enable
using System;
using Feeder.Json;
using Feeder.Json.Nodes;
using Feeder.Reflect.Json;
using Feeder.Reflect.Utils;
using UnityEngine;

namespace Feeder.MCP.JsonConverters
{
    public class Vector3Converter : JsonSchemaConverter<Vector3>, IJsonSchemaConverter
    {
        public override JsonNode GetSchema() => new JsonObject
        {
            [JsonSchema.Type] = JsonSchema.Object,
            [JsonSchema.Properties] = new JsonObject
            {
                ["x"] = new JsonObject { [JsonSchema.Type] = JsonSchema.Number },
                ["y"] = new JsonObject { [JsonSchema.Type] = JsonSchema.Number },
                ["z"] = new JsonObject { [JsonSchema.Type] = JsonSchema.Number }
            },
            [JsonSchema.Required] = new JsonArray { "x", "y", "z" },
            [JsonSchema.AdditionalProperties] = false
        };
        public override JsonNode GetSchemaRef() => new JsonObject
        {
            [JsonSchema.Ref] = JsonSchema.RefValue + Id
        };

        public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => JsonFloatHelper.ReadVector3(ref reader, options);

        public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
            => JsonFloatHelper.WriteVector3(writer, value, options);
    }
}
