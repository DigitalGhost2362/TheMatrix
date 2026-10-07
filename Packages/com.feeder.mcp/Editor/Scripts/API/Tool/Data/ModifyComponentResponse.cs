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
    public class ModifyComponentResponse
    {
        [Description("Whether the modification was successful.")]
        public bool Success { get; set; } = false;

        [Description("Reference to the modified component.")]
        public ComponentRef? Reference { get; set; }

        [Description("Index of the component in the GameObject's component list.")]
        public int Index { get; set; }

        [Description("Updated component information after modification.")]
        public ComponentDataShallow? Component { get; set; }
        [Description("Log of modifications made and any warnings/errors encountered.")]
        public string[]? Logs { get; set; }
    }
}
