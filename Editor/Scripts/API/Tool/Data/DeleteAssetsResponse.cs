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
    public class DeleteAssetsResponse
    {
        [Description("List of paths of deleted assets.")]
        public List<string>? DeletedPaths { get; set; }
        [Description("List of errors encountered during delete operations.")]
        public List<string>? Errors { get; set; }
    }
}
