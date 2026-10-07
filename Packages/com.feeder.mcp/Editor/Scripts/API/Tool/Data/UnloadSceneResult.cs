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
    public class UnloadSceneResult
    {
        [Description("Name of the unloaded scene.")]
        public string? Name { get; set; }
        [Description("Reference to the unloaded scene asset.")]
        public AssetObjectRef? AssetObjectRef { get; set; } = null!;
    }
}
