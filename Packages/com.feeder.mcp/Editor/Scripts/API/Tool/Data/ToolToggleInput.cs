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
    public class ToolToggleInput
    {
        [Description("Name of the MCP tool to enable or disable.")]
        public string Name { get; set; } = string.Empty;

        [Description("Whether the tool should be enabled (true) or disabled (false).")]
        public bool Enabled { get; set; }
    }
}
