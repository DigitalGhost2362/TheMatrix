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
    public class CreateFolderResponse
    {
        [Description("List of GUIDs of created folders.")]
        public List<string>? CreatedFolderGuids { get; set; }
        [Description("List of errors encountered during folder creation.")]
        public List<string>? Errors { get; set; }
    }
}
