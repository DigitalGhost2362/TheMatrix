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
    public class PassData
    {
        [Description("Index of this pass within the subshader.")]
        public int Index { get; set; }

        [Description("Name of the pass. Null if unnamed.")]
        public string? Name { get; set; }

        [Description("Source code of the pass. Null if unavailable.")]
        public string? SourceCode { get; set; }
    }
}
