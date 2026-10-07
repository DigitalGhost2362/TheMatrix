#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Feeder.Json.Serialization;
using Feeder.ToolHost;
using Feeder.Reflect.Model;
using Feeder.MCP.Data;

namespace Feeder.MCP.Data
{
    [Description("MCP tool input argument.")]
    public class ToolInputData
    {
        [JsonInclude, JsonPropertyName("name")]
        [Description("Argument name.")]
        public string Name { get; set; } = string.Empty;

        [JsonInclude, JsonPropertyName("description")]
        [Description("Argument description.")]
        public string? Description { get; set; }
    }
}
