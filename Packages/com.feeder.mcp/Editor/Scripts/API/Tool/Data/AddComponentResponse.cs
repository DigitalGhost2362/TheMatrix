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
    public class AddComponentResponse
    {
        [Description("List of successfully added components.")]
        public List<ComponentDataShallow> AddedComponents { get; set; } = new List<ComponentDataShallow>();

        [Description("List of success messages for added components.")]
        public List<string>? Messages { get; set; }

        [Description("List of warnings encountered during component addition.")]
        public List<string>? Warnings { get; set; }

        [Description("List of errors encountered during component addition.")]
        public List<string>? Errors { get; set; }
    }
}
