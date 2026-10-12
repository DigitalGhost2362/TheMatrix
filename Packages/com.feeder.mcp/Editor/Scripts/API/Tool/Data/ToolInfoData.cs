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
    [Description("MCP tool information.")]
    public class ToolInfoData
    {
        [JsonInclude, JsonPropertyName("name")]
        [Description("Tool name.")]
        public string Name { get; set; } = string.Empty;

        [JsonInclude, JsonPropertyName("enabled")]
        [Description("Whether the tool is enabled. A disabled tool is registered but cannot be called until tool-set-enabled-state turns it on.")]
        public bool Enabled { get; set; }

        [JsonInclude, JsonPropertyName("description")]
        [Description("Tool description.")]
        public string? Description { get; set; }

        [JsonInclude, JsonPropertyName("inputs")]
        [Description("Tool input arguments.")]
        public ToolInputData[]? Inputs { get; set; }
    }
}
