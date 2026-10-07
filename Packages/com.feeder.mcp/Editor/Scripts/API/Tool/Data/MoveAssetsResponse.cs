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
    public class MoveAssetsResponse
    {
        [Description("List of destination paths of successfully moved assets.")]
        public List<string>? MovedPaths { get; set; }
        [Description("List of errors encountered during move operations.")]
        public List<string>? Errors { get; set; }
    }
}
