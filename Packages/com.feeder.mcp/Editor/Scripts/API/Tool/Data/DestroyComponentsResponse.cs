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
    public class DestroyComponentsResponse
    {
        [Description("List of destroyed components.")]
        public ComponentRefList? DestroyedComponents { get; set; }
    }
}
